using System.Net;
using System.Text.RegularExpressions;
using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fjernlaas.Tests.Integration;

/// <summary>Hele login-forløbet gennem HTTP mod en rigtig SQL Server: beskyttelse, login, logout.</summary>
[Collection(SqlServerCollection.Name)]
public class LoginFlowTests(SqlServerFixture fixture)
{
    private const string Adgangskode = "Test-Pass1!";

    private sealed class AppFactory(string forbindelse) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default", forbindelse);

            // Ingen automatisk platformsadmin fra user-secrets: testene opretter selv deres brugere.
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PlatformAdmin:Email"] = "",
                    ["PlatformAdmin:Password"] = ""
                }));
        }
    }

    private static async Task<(string Email, Guid OrganisationId)> OpretBrugerAsync(AppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var org = (await scope.ServiceProvider.GetRequiredService<OrganisationService>()
            .OpretAsync($"Butik {Guid.NewGuid():N}")).Organisation!;
        var email = $"bruger-{Guid.NewGuid():N}@example.com";
        var resultat = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(
            new ApplicationUser { UserName = email, Email = email, Navn = "Fie Testesen", OrganisationId = org.Id },
            Adgangskode);
        Assert.True(resultat.Succeeded, string.Join("; ", resultat.Errors.Select(e => e.Description)));
        return (email, org.Id);
    }

    private static Dictionary<string, string> LaesFormfelter(string html)
    {
        var felter = new Dictionary<string, string>();
        foreach (Match input in Regex.Matches(html, "<input[^>]*>", RegexOptions.IgnoreCase))
        {
            var navn = Regex.Match(input.Value, "name=\"([^\"]*)\"").Groups[1].Value;
            if (navn.Length == 0) continue;
            var vaerdi = Regex.Match(input.Value, "value=\"([^\"]*)\"");
            felter[navn] = vaerdi.Success ? WebUtility.HtmlDecode(vaerdi.Groups[1].Value) : string.Empty;
        }
        return felter;
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string email, string adgangskode)
    {
        var side = await client.GetAsync("/login?ReturnUrl=%2F");
        Assert.Equal(HttpStatusCode.OK, side.StatusCode);
        var felter = LaesFormfelter(await side.Content.ReadAsStringAsync());

        felter[felter.Keys.Single(k => k.EndsWith(".Email"))] = email;
        felter[felter.Keys.Single(k => k.EndsWith(".Adgangskode"))] = adgangskode;

        return await client.PostAsync("/login?ReturnUrl=%2F", new FormUrlEncodedContent(felter));
    }

    private static HttpClient NyKlient(AppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Beskyttet_side_sender_ubesvaret_bruger_til_login()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var klient = NyKlient(factory);

        var svar = await klient.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, svar.StatusCode);
        Assert.Contains("/login", svar.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Loginside_og_health_er_aabne()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var klient = NyKlient(factory);

        Assert.Equal(HttpStatusCode.OK, (await klient.GetAsync("/login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await klient.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Login_med_rigtige_oplysninger_giver_adgang_og_logout_lukker_igen()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var (email, _) = await OpretBrugerAsync(factory);
        var klient = NyKlient(factory);

        var login = await PostLoginAsync(klient, email, Adgangskode);

        Assert.True(login.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther,
            $"Forventede redirect, fik {(int)login.StatusCode}");
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".Fjernlaas.Auth="));

        var forside = await klient.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, forside.StatusCode);
        var html = await forside.Content.ReadAsStringAsync();
        Assert.Contains("Fie Testesen", html);

        var token = LaesFormfelter(html)["__RequestVerificationToken"];
        var logout = await klient.PostAsync("/logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

        var efter = await klient.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, efter.StatusCode);
    }

    [Fact]
    public async Task Forkert_adgangskode_giver_fejlbesked_og_ingen_session()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var (email, _) = await OpretBrugerAsync(factory);
        var klient = NyKlient(factory);

        var svar = await PostLoginAsync(klient, email, "Forkert-Pass9!");

        Assert.Equal(HttpStatusCode.OK, svar.StatusCode);
        Assert.Contains("Login mislykkedes", await svar.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await klient.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Bruger_i_deaktiveret_organisation_faar_samme_fejl_som_forkert_adgangskode()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var (email, organisationId) = await OpretBrugerAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<OrganisationService>()
                .SaetStatusAsync(organisationId, OrganisationStatus.Deaktiveret);
        }
        var klient = NyKlient(factory);

        var svar = await PostLoginAsync(klient, email, Adgangskode);

        Assert.Equal(HttpStatusCode.OK, svar.StatusCode);
        Assert.Contains("Login mislykkedes", await svar.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Logout_uden_antiforgery_token_afvises_og_beholder_sessionen()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var (email, _) = await OpretBrugerAsync(factory);
        var klient = NyKlient(factory);
        await PostLoginAsync(klient, email, Adgangskode);

        var svar = await klient.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, svar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await klient.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Logout_uden_login_sender_til_login()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var klient = NyKlient(factory);

        var svar = await klient.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Redirect, svar.StatusCode);
        Assert.Contains("/login", svar.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Organisationssiden_kraever_platformsadmin()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var (email, _) = await OpretBrugerAsync(factory);
        var klient = NyKlient(factory);

        // Ikke logget ind: sendt til login.
        var ubesvaret = await klient.GetAsync("/organisationer");
        Assert.Equal(HttpStatusCode.Redirect, ubesvaret.StatusCode);
        Assert.Contains("/login", ubesvaret.Headers.Location!.ToString());

        // Logget ind som almindelig bruger i en organisation: ingen adgang.
        await PostLoginAsync(klient, email, Adgangskode);
        var almindelig = await klient.GetAsync("/organisationer");
        Assert.NotEqual(HttpStatusCode.OK, almindelig.StatusCode);
    }

    [Fact]
    public async Task Platformsadmin_ser_organisationerne_paa_siden()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var navn = $"Synlig {Guid.NewGuid():N}";
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.com";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<OrganisationService>().OpretAsync(navn);
            await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>()
                .SikrAsync(adminEmail, "Platformsadmin", Adgangskode);
        }
        var klient = NyKlient(factory);
        await PostLoginAsync(klient, adminEmail, Adgangskode);

        var svar = await klient.GetAsync("/organisationer");

        Assert.Equal(HttpStatusCode.OK, svar.StatusCode);
        var html = await svar.Content.ReadAsStringAsync();
        Assert.Contains(navn, html);
        Assert.Contains("Tilføj organisation", html);
    }

    [Fact]
    public async Task Menupunktet_organisationer_vises_kun_for_platformsadmin()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var (almindeligEmail, _) = await OpretBrugerAsync(factory);
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.com";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>()
                .SikrAsync(adminEmail, "Platformsadmin", Adgangskode);
        }

        var almindelig = NyKlient(factory);
        await PostLoginAsync(almindelig, almindeligEmail, Adgangskode);
        var almindeligHtml = await (await almindelig.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("href=\"organisationer\"", almindeligHtml);

        var admin = NyKlient(factory);
        await PostLoginAsync(admin, adminEmail, Adgangskode);
        var adminHtml = await (await admin.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("href=\"organisationer\"", adminHtml);
    }
}
