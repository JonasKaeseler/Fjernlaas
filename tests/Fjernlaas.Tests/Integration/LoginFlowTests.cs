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

    private static readonly Aktoer Platform = new(Guid.CreateVersion7(), null, true, false);
    private const string Midlertidig = "Midlertidig-1!x";

    private static async Task<(Guid OrganisationId, string Email)> OpretViaServiceAsync(
        AppFactory factory, Guid? organisationId, string rolle, bool klarTilBrug)
    {
        using var scope = factory.Services.CreateScope();
        var orgId = organisationId ?? (await scope.ServiceProvider.GetRequiredService<OrganisationService>()
            .OpretAsync($"Butik {Guid.NewGuid():N}")).Organisation!.Id;
        var email = $"bruger-{Guid.NewGuid():N}@example.com";

        var resultat = await scope.ServiceProvider.GetRequiredService<BrugerService>()
            .OpretAsync(Platform, orgId, "Fie Testesen", email, rolle, Midlertidig);
        Assert.True(resultat.Ok, resultat.Fejl.ToString());

        if (klarTilBrug)
        {
            var brugere = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var bruger = (await brugere.FindByEmailAsync(email))!;
            bruger.SkalSkifteAdgangskode = false;
            await brugere.UpdateAsync(bruger);
        }

        return (orgId, email);
    }

    private static async Task<HttpResponseMessage> PostSkiftAsync(HttpClient klient, string nuvaerende, string ny, string gentag)
    {
        var side = await klient.GetAsync("/skift-adgangskode");
        Assert.Equal(HttpStatusCode.OK, side.StatusCode);
        var felter = LaesFormfelter(await side.Content.ReadAsStringAsync());

        felter[felter.Keys.Single(k => k.EndsWith(".NuvaerendeAdgangskode"))] = nuvaerende;
        felter[felter.Keys.Single(k => k.EndsWith(".NyAdgangskode"))] = ny;
        felter[felter.Keys.Single(k => k.EndsWith(".GentagAdgangskode"))] = gentag;

        return await klient.PostAsync("/skift-adgangskode", new FormUrlEncodedContent(felter));
    }

    [Fact]
    public async Task Midlertidig_adgangskode_skal_skiftes_foer_brugeren_kommer_videre()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var (_, email) = await OpretViaServiceAsync(factory, null, Roller.Bruger, klarTilBrug: false);
        var klient = NyKlient(factory);

        await PostLoginAsync(klient, email, Midlertidig);

        // Alt andet end skift-siden sender videre dertil.
        var forside = await klient.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, forside.StatusCode);
        Assert.Contains("/skift-adgangskode", forside.Headers.Location!.ToString());

        // For svag ny kode afvises med en forklaring, og brugeren er stadig spærret.
        var svag = await PostSkiftAsync(klient, Midlertidig, "kort", "kort");
        Assert.Equal(HttpStatusCode.OK, svag.StatusCode);
        Assert.Contains("mindst 10 tegn", await svag.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await klient.GetAsync("/")).StatusCode);

        // To forskellige nye koder afvises.
        var forskellig = await PostSkiftAsync(klient, Midlertidig, "Egen-Kode-2026!", "Anden-Kode-2026!");
        Assert.Contains("ikke ens", await forskellig.Content.ReadAsStringAsync());

        // Rigtig skift giver adgang.
        var skift = await PostSkiftAsync(klient, Midlertidig, "Egen-Kode-2026!", "Egen-Kode-2026!");
        Assert.True(skift.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        Assert.Equal(HttpStatusCode.OK, (await klient.GetAsync("/")).StatusCode);

        // Bagefter virker kun den nye kode.
        var ny = NyKlient(factory);
        var medGammel = await PostLoginAsync(ny, email, Midlertidig);
        Assert.Equal(HttpStatusCode.OK, medGammel.StatusCode);
        Assert.Contains("Login mislykkedes", await medGammel.Content.ReadAsStringAsync());

        var medNy = await PostLoginAsync(ny, email, "Egen-Kode-2026!");
        Assert.True(medNy.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        Assert.Equal(HttpStatusCode.OK, (await ny.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Organisationsadmin_ser_kun_egne_brugere_paa_brugersiden_og_ingen_knapper()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var (orgA, adminEmail) = await OpretViaServiceAsync(factory, null, Roller.OrganisationAdmin, klarTilBrug: true);
        var (_, kollegaA) = await OpretViaServiceAsync(factory, orgA, Roller.Bruger, klarTilBrug: true);
        var (_, fremmed) = await OpretViaServiceAsync(factory, null, Roller.Bruger, klarTilBrug: true);

        var klient = NyKlient(factory);
        await PostLoginAsync(klient, adminEmail, Midlertidig);

        var svar = await klient.GetAsync("/brugere");

        Assert.Equal(HttpStatusCode.OK, svar.StatusCode);
        var html = await svar.Content.ReadAsStringAsync();
        Assert.Contains(kollegaA, html);
        Assert.DoesNotContain(fremmed, html);
        Assert.DoesNotContain("Tilføj bruger", html);
        Assert.DoesNotContain("Nulstil kode", html);

        // Organisationsadmin har ikke adgang til organisationssiden.
        Assert.NotEqual(HttpStatusCode.OK, (await klient.GetAsync("/organisationer")).StatusCode);
    }

    [Fact]
    public async Task Almindelig_bruger_har_ikke_adgang_til_brugersiden()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var (_, email) = await OpretViaServiceAsync(factory, null, Roller.Bruger, klarTilBrug: true);
        var klient = NyKlient(factory);
        await PostLoginAsync(klient, email, Midlertidig);

        Assert.NotEqual(HttpStatusCode.OK, (await klient.GetAsync("/brugere")).StatusCode);
    }

    [Fact]
    public async Task Platformsadmin_ser_alle_brugere_og_har_knapperne()
    {
        using var factory = new AppFactory(fixture.ConnectionString);
        var (_, emailA) = await OpretViaServiceAsync(factory, null, Roller.Bruger, klarTilBrug: true);
        var (_, emailB) = await OpretViaServiceAsync(factory, null, Roller.Bruger, klarTilBrug: true);
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.com";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>()
                .SikrAsync(adminEmail, "Platformsadmin", Adgangskode);
        }

        var klient = NyKlient(factory);
        await PostLoginAsync(klient, adminEmail, Adgangskode);

        var html = await (await klient.GetAsync("/brugere")).Content.ReadAsStringAsync();

        Assert.Contains(emailA, html);
        Assert.Contains(emailB, html);
        Assert.Contains("Tilføj bruger", html);
    }
}
