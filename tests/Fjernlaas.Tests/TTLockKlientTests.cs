using System.Net;
using System.Text;
using Fjernlaas.Web.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fjernlaas.Tests;

public class TTLockKlientTests
{
    private sealed class FaktiskTid : TimeProvider
    {
        public DateTimeOffset Nu { get; set; } = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Nu;
    }

    private sealed class FalskHandler(Func<HttpRequestMessage, string, HttpResponseMessage> svar) : HttpMessageHandler
    {
        public List<(Uri Url, string Body)> Kald { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            Kald.Add((request.RequestUri!, body));
            return svar(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (TTLockKlient Klient, FalskHandler Handler, FaktiskTid Tid) Ny(Func<HttpRequestMessage, string, HttpResponseMessage> svar)
    {
        var handler = new FalskHandler(svar);
        var tid = new FaktiskTid();
        var options = Options.Create(new TTLockOptions { ClientId = "app-id", ClientSecret = "app-hemmelighed", BaseUrl = "https://euapi.example.test" });
        return (new TTLockKlient(new HttpClient(handler), options, tid), handler, tid);
    }

    [Fact]
    public void Md5_er_32_smaa_hextegn()
    {
        Assert.Equal("5f4dcc3b5aa765d61d8327deb882cf99", TTLockKlient.Md5Hex("password"));
    }

    [Fact]
    public async Task Hent_token_sender_loginoplysninger_og_laeser_svaret()
    {
        var (klient, handler, tid) = Ny((_, _) => Json("""
            {"access_token":"acc","refresh_token":"ref","uid":1,"openid":2,"scope":"user,key,room","token_type":"Bearer","expires_in":4841240}
            """));

        var svar = await klient.HentTokenAsync("mig@example.com", "abc123");

        Assert.True(svar.Ok);
        Assert.Equal("acc", svar.Data!.AccessToken);
        Assert.Equal("ref", svar.Data.RefreshToken);
        Assert.Equal(tid.Nu.UtcDateTime.AddSeconds(4841240), svar.Data.UdloeberUtc);

        var kald = Assert.Single(handler.Kald);
        Assert.Equal("https://euapi.example.test/oauth2/token", kald.Url.ToString());
        Assert.Contains("clientId=app-id", kald.Body);
        Assert.Contains("clientSecret=app-hemmelighed", kald.Body);
        Assert.Contains("username=mig%40example.com", kald.Body);
        Assert.Contains("password=abc123", kald.Body);
    }

    [Fact]
    public async Task Forny_token_bruger_refresh_token()
    {
        var (klient, handler, _) = Ny((_, _) => Json("""{"access_token":"a2","refresh_token":"r2","expires_in":100}"""));

        var svar = await klient.FornyTokenAsync("gammelt-refresh");

        Assert.True(svar.Ok);
        Assert.Equal("r2", svar.Data!.RefreshToken);
        Assert.Contains("grant_type=refresh_token", handler.Kald[0].Body);
        Assert.Contains("refresh_token=gammelt-refresh", handler.Kald[0].Body);
    }

    [Fact]
    public async Task Fejl_ligger_i_errcode_selv_naar_http_er_200()
    {
        var (klient, _, _) = Ny((_, _) => Json("""{"errcode":10007,"errmsg":"invalid account","description":"x"}"""));

        var svar = await klient.HentTokenAsync("mig", "hash");

        Assert.False(svar.Ok);
        Assert.Equal(TTLockKoder.ForkertLogin, svar.ErrCode);
        Assert.Contains("brugernavn eller adgangskode", svar.Fejl);
    }

    [Fact]
    public async Task Aabn_sender_laas_og_tidsstempel_i_millisekunder()
    {
        var (klient, handler, tid) = Ny((_, _) => Json("""{"errcode":0,"errmsg":"none error message","description":"ok"}"""));

        var svar = await klient.AabnAsync("tok", 32429590);

        Assert.True(svar.Ok);
        var kald = Assert.Single(handler.Kald);
        Assert.Equal("https://euapi.example.test/v3/lock/unlock", kald.Url.ToString());
        Assert.Contains("accessToken=tok", kald.Body);
        Assert.Contains("lockId=32429590", kald.Body);
        Assert.Contains($"date={tid.Nu.ToUnixTimeMilliseconds()}", kald.Body);
    }

    [Theory]
    [InlineData("""{"errcode":-2012,"errmsg":"no gateway"}""", -2012)]
    [InlineData("""{"errcode":-4043,"errmsg":"not supported"}""", -4043)]
    [InlineData("""{"errcode":10003,"errmsg":"invalid token"}""", 10003)]
    [InlineData("""{"errcode":30006,"errmsg":"too many"}""", 30006)]
    public async Task Aabn_giver_fejlkoden_videre(string json, int forventet)
    {
        var (klient, _, _) = Ny((_, _) => Json(json));

        var svar = await klient.AabnAsync("tok", 1);

        Assert.False(svar.Ok);
        Assert.Equal(forventet, svar.ErrCode);
    }

    [Fact]
    public async Task Aabn_uden_errcode_i_svaret_regnes_ikke_for_succes()
    {
        var (klient, _, _) = Ny((_, _) => Json("""{"hej":"verden"}"""));

        var svar = await klient.AabnAsync("tok", 1);

        Assert.False(svar.Ok);
        Assert.Equal(TTLockKoder.Teknisk, svar.ErrCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "ikke json")]
    [InlineData(HttpStatusCode.OK, "[1,2]")]
    public async Task Tekniske_fejl_giver_fejl_og_aldrig_succes(HttpStatusCode status, string body)
    {
        var (klient, _, _) = Ny((_, _) => Json(body, status));

        var svar = await klient.AabnAsync("tok", 1);

        Assert.False(svar.Ok);
        Assert.Equal(TTLockKoder.Teknisk, svar.ErrCode);
    }

    [Fact]
    public async Task Netvaerksfejl_giver_fejl_og_kaster_ikke()
    {
        var (klient, _, _) = Ny((_, _) => throw new HttpRequestException("nede"));

        var svar = await klient.AabnAsync("tok", 1);

        Assert.False(svar.Ok);
        Assert.Equal(TTLockKoder.Teknisk, svar.ErrCode);
    }

    [Fact]
    public async Task Laaseliste_laeser_felter_og_henter_naeste_side_naar_siden_er_fuld()
    {
        var side = 0;
        var (klient, handler, _) = Ny((_, body) =>
        {
            side++;
            if (side == 1)
            {
                var elementer = string.Join(",", Enumerable.Range(1, 100).Select(i => $$"""{"lockId":{{i}},"lockName":"L{{i}}"}"""));
                return Json($$"""{"list":[{{elementer}}]}""");
            }

            return Json("""{"list":[{"lockId":999,"lockName":"M201","lockAlias":"Hoveddør","electricQuantity":87,"hasGateway":1}]}""");
        });

        var svar = await klient.ListLaaseAsync("tok");

        Assert.True(svar.Ok);
        Assert.Equal(101, svar.Data!.Count);
        var sidste = svar.Data[^1];
        Assert.Equal(999, sidste.LockId);
        Assert.Equal("Hoveddør", sidste.Alias);
        Assert.Equal(87, sidste.Batteri);
        Assert.True(sidste.HarGateway);
        Assert.Equal(2, handler.Kald.Count);
        Assert.Contains("pageNo=2", handler.Kald[1].Body);
    }
}
