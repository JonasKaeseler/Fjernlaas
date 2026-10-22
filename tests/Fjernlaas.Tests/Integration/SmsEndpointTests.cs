using System.Net;
using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Fjernlaas.Tests.Integration;

/// <summary>Webhook-endpointet gennem HTTP, uden login og med en falsk TTLock.</summary>
[Collection(SqlServerCollection.Name)]
public class SmsEndpointTests(SqlServerFixture fixture)
{
    private static Aktoer Platform() => new(Guid.NewGuid(), null, true, false);

    private sealed class AppFactory(string forbindelse, FalskTTLockKlient ttlock) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default", forbindelse);

            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PlatformAdmin:Email"] = "",
                    ["PlatformAdmin:Password"] = "",
                    ["TTLock:ClientId"] = "id",
                    ["TTLock:ClientSecret"] = "hemmelig"
                }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITTLockKlient>();
                services.AddSingleton<ITTLockKlient>(ttlock);
            });
        }
    }

    private static HttpClient NyKlient(AppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Opretter en organisation med dør, lås, SMS-nummer og nøgleord ROSHAGE gennem appens egne services.</summary>
    private static async Task<(Guid OrganisationId, string Token)> OpsaetAsync(AppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var admin = Platform();

        var org = (await sp.GetRequiredService<OrganisationService>().OpretAsync($"Org {Guid.NewGuid():N}")).Organisation!.Id;
        var lokation = (await sp.GetRequiredService<LokationService>().OpretAsync(admin, org, "Butik", null, "UTC")).Lokation!;
        var ressource = (await sp.GetRequiredService<RessourceService>().OpretAsync(admin, lokation.Id, "Hoveddør")).Ressource!;
        var forbindelse = (await sp.GetRequiredService<TTLockForbindelseService>()
            .OpretAsync(admin, org, "Konto", "lukas@example.com", "MinKode123")).Forbindelse!;
        Assert.True((await sp.GetRequiredService<RessourceService>().KoblAsync(admin, ressource.Id, forbindelse.Id, 777)).Ok);

        var nummer = await sp.GetRequiredService<SmsNummerService>().OpretAsync(admin, org, "Nummer", null);
        Assert.True((await sp.GetRequiredService<NoegleordService>().OpretAsync(admin, nummer.SmsNummer!.Id, ressource.Id, "ROSHAGE")).Ok);

        return (org, nummer.Token!);
    }

    private async Task<List<Oplaasningsforsoeg>> LogAsync(Guid organisationId)
    {
        await using var db = fixture.CreateContext();
        db.SaetTenant(true, null);
        return await db.Forsoeg.AsNoTracking().Where(f => f.OrganisationId == organisationId).ToListAsync();
    }

    [Fact]
    public async Task Ukendt_token_giver_404_uden_login()
    {
        var ttlock = new FalskTTLockKlient();
        using var factory = new AppFactory(fixture.ConnectionString, ttlock);

        var svar = await NyKlient(factory).PostAsync("/api/sms/findes-ikke", new StringContent("4512345678;Roshage"));

        Assert.Equal(HttpStatusCode.NotFound, svar.StatusCode);
        Assert.Equal(0, ttlock.AabnKald);
    }

    [Fact]
    public async Task Post_med_raa_tekst_aabner_doeren_og_svarer_ok()
    {
        var ttlock = new FalskTTLockKlient();
        using var factory = new AppFactory(fixture.ConnectionString, ttlock);
        var (org, token) = await OpsaetAsync(factory);

        var svar = await NyKlient(factory).PostAsync($"/api/sms/{token}", new StringContent("4512345678;Roshage\r"));

        Assert.Equal(HttpStatusCode.OK, svar.StatusCode);
        Assert.Equal("OK", await svar.Content.ReadAsStringAsync());
        Assert.Equal(1, ttlock.AabnKald);
        Assert.Equal(777, ttlock.SidsteLockId);
        Assert.Equal(Udfald.Aabnet, Assert.Single(await LogAsync(org)).Udfald);
    }

    [Fact]
    public async Task Get_med_value_og_formular_med_value_virker_ogsaa()
    {
        var ttlock = new FalskTTLockKlient();
        using var factory = new AppFactory(fixture.ConnectionString, ttlock);
        var (org, token) = await OpsaetAsync(factory);
        var klient = NyKlient(factory);

        var get = await klient.GetAsync($"/api/sms/{token}?value={Uri.EscapeDataString("4511111111;Roshage")}");
        var form = await klient.PostAsync($"/api/sms/{token}", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["value"] = "4522222222;Roshage" }));

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Equal(2, ttlock.AabnKald);
        Assert.Equal(2, (await LogAsync(org)).Count(f => f.Udfald == Udfald.Aabnet));
    }

    [Fact]
    public async Task Afvist_forsoeg_svarer_stadig_200_men_aabner_ikke()
    {
        var ttlock = new FalskTTLockKlient();
        using var factory = new AppFactory(fixture.ConnectionString, ttlock);
        var (org, token) = await OpsaetAsync(factory);

        var svar = await NyKlient(factory).PostAsync($"/api/sms/{token}", new StringContent("4512345678;Forkert"));

        Assert.Equal(HttpStatusCode.OK, svar.StatusCode);
        Assert.Equal(0, ttlock.AabnKald);
        Assert.Equal(Udfald.AfvistUkendtOrd, Assert.Single(await LogAsync(org)).Udfald);
    }

    [Fact]
    public async Task Tom_body_logges_som_ugyldigt_format()
    {
        var ttlock = new FalskTTLockKlient();
        using var factory = new AppFactory(fixture.ConnectionString, ttlock);
        var (org, token) = await OpsaetAsync(factory);

        var svar = await NyKlient(factory).PostAsync($"/api/sms/{token}", new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.OK, svar.StatusCode);
        Assert.Equal(Udfald.AfvistUgyldigtFormat, Assert.Single(await LogAsync(org)).Udfald);
    }
}
