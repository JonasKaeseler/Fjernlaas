using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fjernlaas.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class TTLockForbindelseTests(SqlServerFixture fixture)
{
    private static string Unik() => Guid.NewGuid().ToString("N");

    private static Aktoer Platform() => new(Guid.NewGuid(), null, true, false);
    private static Aktoer OrgAdmin(Guid org) => new(Guid.NewGuid(), org, false, true);
    private static Aktoer Almindelig(Guid org) => new(Guid.NewGuid(), org, false, false);

    private static readonly IHemmelighedBeskytter Beskytter = new DataProtectionBeskytter(new EphemeralDataProtectionProvider());
    private static readonly TokenLaase Laase = new();

    private static IOptions<TTLockOptions> Opt(bool saet = true) =>
        Options.Create(saet ? new TTLockOptions { ClientId = "id", ClientSecret = "hemmelig" } : new TTLockOptions());

    private (TTLockForbindelseService Service, TTLockTokenService Tokens) Nyt(FalskTTLockKlient klient, TestTid tid, bool optionsSat = true)
    {
        var db = fixture.CreateContext();
        db.SaetTenant(true, null); // tokentjenesten forventer, at kalderen har sat tenant
        var tokens = new TTLockTokenService(db, klient, Beskytter, Laase, tid);
        return (new TTLockForbindelseService(db, klient, tokens, Beskytter, Opt(optionsSat)), tokens);
    }

    private async Task<Guid> NyOrganisationAsync()
    {
        var res = await new OrganisationService(fixture.CreateContext()).OpretAsync($"Org {Unik()}");
        return res.Organisation!.Id;
    }

    private async Task<TTLockForbindelse> NyForbindelseAsync(Guid org, FalskTTLockKlient klient, TestTid tid, string kode = "MinKode123")
    {
        var res = await Nyt(klient, tid).Service.OpretAsync(Platform(), org, "Roshage-konto", "lukas@example.com", kode);
        Assert.True(res.Ok, res.Fejl.ToString());
        return res.Forbindelse!;
    }

    [Fact]
    public async Task Opret_gemmer_kun_krypteret_hash_og_tokens()
    {
        var org = await NyOrganisationAsync();
        var klient = new FalskTTLockKlient();

        var f = await NyForbindelseAsync(org, klient, new TestTid());

        Assert.Equal("lukas@example.com", klient.SidsteLoginBrugernavn);
        Assert.Equal(TTLockKlient.Md5Hex("MinKode123"), klient.SidsteLoginHash);

        await using var db = fixture.CreateContext();
        db.SaetTenant(true, null);
        var gemt = await db.TTLockForbindelser.AsNoTracking().SingleAsync(x => x.Id == f.Id);

        Assert.NotEqual("MinKode123", gemt.AdgangskodeHashBeskyttet);
        Assert.NotEqual(TTLockKlient.Md5Hex("MinKode123"), gemt.AdgangskodeHashBeskyttet);
        Assert.DoesNotContain("MinKode123", gemt.AdgangskodeHashBeskyttet);
        Assert.Equal(TTLockKlient.Md5Hex("MinKode123"), Beskytter.FjernBeskyttelse(gemt.AdgangskodeHashBeskyttet));
        Assert.Equal("access-1", Beskytter.FjernBeskyttelse(gemt.AccessTokenBeskyttet!));
        Assert.Equal("refresh-1", Beskytter.FjernBeskyttelse(gemt.RefreshTokenBeskyttet!));
        Assert.Equal(ForbindelseStatus.Aktiv, gemt.Status);
    }

    [Fact]
    public async Task Opret_afvises_naar_ttlock_afviser_login_og_intet_gemmes()
    {
        var org = await NyOrganisationAsync();
        var klient = new FalskTTLockKlient { LoginSvar = TTLockSvar<TTLockToken>.Fejlet(TTLockKoder.ForkertLogin) };

        var res = await Nyt(klient, new TestTid()).Service.OpretAsync(Platform(), org, "Konto", "x@example.com", "forkert");

        Assert.Equal(ForbindelseFejl.TTLockAfviste, res.Fejl);
        Assert.Contains("brugernavn eller adgangskode", res.Detalje);

        await using var db = fixture.CreateContext();
        db.SaetTenant(true, null);
        Assert.Empty(await db.TTLockForbindelser.Where(f => f.OrganisationId == org).ToListAsync());
    }

    [Fact]
    public async Task Opret_kraever_at_appens_ttlock_oplysninger_er_sat()
    {
        var org = await NyOrganisationAsync();

        var res = await Nyt(new FalskTTLockKlient(), new TestTid(), optionsSat: false)
            .Service.OpretAsync(Platform(), org, "Konto", "x@example.com", "kode");

        Assert.Equal(ForbindelseFejl.IkkeKonfigureret, res.Fejl);
    }

    [Theory]
    [InlineData("", "x@example.com", "kode", ForbindelseFejl.NavnUgyldigt)]
    [InlineData("Konto", " ", "kode", ForbindelseFejl.BrugernavnUgyldigt)]
    [InlineData("Konto", "x@example.com", "", ForbindelseFejl.AdgangskodeMangler)]
    public async Task Opret_validerer(string navn, string brugernavn, string kode, ForbindelseFejl forventet)
    {
        var org = await NyOrganisationAsync();

        var res = await Nyt(new FalskTTLockKlient(), new TestTid()).Service.OpretAsync(Platform(), org, navn, brugernavn, kode);

        Assert.Equal(forventet, res.Fejl);
    }

    [Fact]
    public async Task Rettigheder_og_isolation()
    {
        var org1 = await NyOrganisationAsync();
        var org2 = await NyOrganisationAsync();
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var f1 = await NyForbindelseAsync(org1, klient, tid);
        var f2 = await NyForbindelseAsync(org2, klient, tid);

        Assert.Equal(ForbindelseFejl.IkkeTilladt, (await Nyt(klient, tid).Service.OpretAsync(Almindelig(org1), org1, "K", "x@example.com", "k")).Fejl);
        Assert.Equal(ForbindelseFejl.IkkeTilladt, (await Nyt(klient, tid).Service.OpretAsync(OrgAdmin(org1), org2, "K", "x@example.com", "k")).Fejl);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Nyt(klient, tid).Service.OversigtAsync(Almindelig(org1)));

        var liste = await Nyt(klient, tid).Service.OversigtAsync(OrgAdmin(org1));
        Assert.Contains(liste, x => x.Id == f1.Id);
        Assert.DoesNotContain(liste, x => x.Id == f2.Id);

        Assert.Equal(ForbindelseFejl.IkkeFundet, (await Nyt(klient, tid).Service.SaetStatusAsync(OrgAdmin(org1), f2.Id, ForbindelseStatus.Deaktiveret)).Fejl);
        Assert.Equal(ForbindelseFejl.IkkeFundet, (await Nyt(klient, tid).Service.LaaseAsync(OrgAdmin(org1), f2.Id)).Fejl);
    }

    [Fact]
    public async Task Laaseliste_hentes_med_gemt_token()
    {
        var org = await NyOrganisationAsync();
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var f = await NyForbindelseAsync(org, klient, tid);

        var res = await Nyt(klient, tid).Service.LaaseAsync(OrgAdmin(org), f.Id);

        Assert.True(res.Ok);
        Assert.Equal(111, Assert.Single(res.Laase).LockId);
        Assert.Equal(1, klient.LoginKald);
    }

    // --- Token ---

    [Fact]
    public async Task Token_genbruges_uden_nye_kald_naar_det_ikke_snart_udloeber()
    {
        var org = await NyOrganisationAsync();
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var f = await NyForbindelseAsync(org, klient, tid);

        var (_, tokens) = Nyt(klient, tid);
        var svar = await tokens.HentAccessTokenAsync(f.Id);

        Assert.True(svar.Ok);
        Assert.Equal("access-1", svar.Data);
        Assert.Equal(0, klient.FornyKald);
        Assert.Equal(1, klient.LoginKald);
    }

    [Fact]
    public async Task Token_fornyes_med_refresh_token_naar_det_snart_udloeber()
    {
        var org = await NyOrganisationAsync();
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var f = await NyForbindelseAsync(org, klient, tid);

        // Spoler uret så der er en dag tilbage af tokenets levetid.
        tid.Nu = new DateTimeOffset(f.TokenUdloeberTid!.Value, TimeSpan.Zero).AddDays(-1);
        klient.FornySvar = TTLockSvar<TTLockToken>.Succes(
            new TTLockToken("fornyet-1", "fornyet-refresh-1", tid.Nu.UtcDateTime.AddDays(56)));

        var (_, tokens) = Nyt(klient, tid);
        var svar = await tokens.HentAccessTokenAsync(f.Id);

        Assert.True(svar.Ok);
        Assert.Equal("fornyet-1", svar.Data);
        Assert.Equal(1, klient.FornyKald);

        // Det nye token er gemt, så næste kald genbruger det.
        var igen = await Nyt(klient, tid).Tokens.HentAccessTokenAsync(f.Id);
        Assert.Equal("fornyet-1", igen.Data);
        Assert.Equal(1, klient.FornyKald);
    }

    [Fact]
    public async Task Token_logger_ind_igen_naar_fornyelsen_afvises()
    {
        var org = await NyOrganisationAsync();
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var f = await NyForbindelseAsync(org, klient, tid);
        klient.FornySvar = TTLockSvar<TTLockToken>.Fejlet(TTLockKoder.UgyldigtRefreshToken);

        var svar = await Nyt(klient, tid).Tokens.HentAccessTokenAsync(f.Id, tvingNyt: true);

        Assert.True(svar.Ok);
        Assert.Equal("access-2", svar.Data);
        Assert.Equal(1, klient.FornyKald);
        Assert.Equal(2, klient.LoginKald);
    }

    [Fact]
    public async Task Token_gemmer_seneste_fejl_naar_login_afvises()
    {
        var org = await NyOrganisationAsync();
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var f = await NyForbindelseAsync(org, klient, tid);
        klient.FornySvar = TTLockSvar<TTLockToken>.Fejlet(TTLockKoder.UgyldigtRefreshToken);
        klient.LoginSvar = TTLockSvar<TTLockToken>.Fejlet(TTLockKoder.ForkertLogin);

        var svar = await Nyt(klient, tid).Tokens.HentAccessTokenAsync(f.Id, tvingNyt: true);

        Assert.False(svar.Ok);
        Assert.Equal(TTLockKoder.ForkertLogin, svar.ErrCode);

        await using var db = fixture.CreateContext();
        db.SaetTenant(true, null);
        var gemt = await db.TTLockForbindelser.AsNoTracking().SingleAsync(x => x.Id == f.Id);
        Assert.Contains("brugernavn eller adgangskode", gemt.SidsteFejl);
    }

    [Fact]
    public async Task Token_giver_fejl_i_stedet_for_at_kaste_naar_gemte_oplysninger_ikke_kan_laeses()
    {
        var org = await NyOrganisationAsync();
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var f = await NyForbindelseAsync(org, klient, tid);

        // En beskytter med andre nøgler kan ikke læse det, der er gemt.
        var andenBeskytter = new DataProtectionBeskytter(new EphemeralDataProtectionProvider());
        var db = fixture.CreateContext();
        db.SaetTenant(true, null);
        var tokens = new TTLockTokenService(db, klient, andenBeskytter, new TokenLaase(), tid);

        var svar = await tokens.HentAccessTokenAsync(f.Id);

        Assert.False(svar.Ok);
        Assert.Equal(TTLockKoder.OplysningerUlaeselige, svar.ErrCode);
    }
}
