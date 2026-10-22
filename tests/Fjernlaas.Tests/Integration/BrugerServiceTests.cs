using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fjernlaas.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class BrugerServiceTests(SqlServerFixture fixture)
{
    private const string Kode = "Midlertidig-1!x";

    private static readonly Aktoer Platform = new(Guid.CreateVersion7(), null, true, false);

    private ServiceProvider ByggServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection(); // kræves af Identitys token-udbydere (web-appen registrerer den selv)
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(fixture.ConnectionString));
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<OrganisationService>();
        services.AddScoped<BrugerService>();
        return services.BuildServiceProvider();
    }

    private static string NyEmail() => $"bruger-{Guid.NewGuid():N}@example.com";

    private static async Task<Guid> NyOrganisationAsync(IServiceProvider sp) =>
        (await sp.GetRequiredService<OrganisationService>().OpretAsync($"Butik {Guid.NewGuid():N}")).Organisation!.Id;

    private static async Task<ApplicationUser> OpretAsync(
        IServiceProvider sp, Guid organisationId, string rolle = Roller.Bruger, string? email = null)
    {
        var resultat = await sp.GetRequiredService<BrugerService>()
            .OpretAsync(Platform, organisationId, "Testbruger", email ?? NyEmail(), rolle, Kode);
        Assert.True(resultat.Ok, $"{resultat.Fejl}: {string.Join("; ", resultat.Detaljer)}");
        return resultat.Bruger!;
    }

    [Fact]
    public async Task Opret_goer_brugeren_klar_til_foerste_login()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var organisationId = await NyOrganisationAsync(scope.ServiceProvider);
        var email = NyEmail();

        var bruger = await OpretAsync(scope.ServiceProvider, organisationId, Roller.OrganisationAdmin, $"  {email}  ");

        var brugere = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var gemt = (await brugere.FindByEmailAsync(email))!;
        Assert.Equal(email, gemt.Email);
        Assert.Equal(organisationId, gemt.OrganisationId);
        Assert.True(gemt.SkalSkifteAdgangskode);
        Assert.True(gemt.Aktiv);
        Assert.True(await brugere.CheckPasswordAsync(gemt, Kode));
        Assert.True(await brugere.IsInRoleAsync(gemt, Roller.OrganisationAdmin));
        Assert.Equal(bruger.Id, gemt.Id);
    }

    [Fact]
    public async Task Opret_afviser_email_der_findes_uanset_store_og_smaa_bogstaver()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var organisationId = await NyOrganisationAsync(scope.ServiceProvider);
        var email = NyEmail();
        await OpretAsync(scope.ServiceProvider, organisationId, email: email);

        var resultat = await scope.ServiceProvider.GetRequiredService<BrugerService>()
            .OpretAsync(Platform, organisationId, "Anden", email.ToUpperInvariant(), Roller.Bruger, Kode);

        Assert.Equal(BrugerFejl.EmailFindesAllerede, resultat.Fejl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ikke-en-email")]
    [InlineData("a@")]
    public async Task Opret_afviser_ugyldig_email(string email)
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var organisationId = await NyOrganisationAsync(scope.ServiceProvider);

        var resultat = await scope.ServiceProvider.GetRequiredService<BrugerService>()
            .OpretAsync(Platform, organisationId, "Navn", email, Roller.Bruger, Kode);

        Assert.Equal(BrugerFejl.EmailUgyldig, resultat.Fejl);
    }

    [Fact]
    public async Task Opret_afviser_svag_adgangskode_med_forklaring()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var organisationId = await NyOrganisationAsync(scope.ServiceProvider);

        var resultat = await scope.ServiceProvider.GetRequiredService<BrugerService>()
            .OpretAsync(Platform, organisationId, "Navn", NyEmail(), Roller.Bruger, "kort");

        Assert.Equal(BrugerFejl.AdgangskodeAfvist, resultat.Fejl);
        Assert.NotEmpty(resultat.Detaljer);
    }

    [Fact]
    public async Task Opret_afviser_ugyldigt_navn_ukendt_organisation_og_ugyldig_rolle()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrugerService>();
        var organisationId = await NyOrganisationAsync(scope.ServiceProvider);

        Assert.Equal(BrugerFejl.NavnUgyldigt,
            (await service.OpretAsync(Platform, organisationId, "  ", NyEmail(), Roller.Bruger, Kode)).Fejl);
        Assert.Equal(BrugerFejl.OrganisationIkkeFundet,
            (await service.OpretAsync(Platform, Guid.CreateVersion7(), "Navn", NyEmail(), Roller.Bruger, Kode)).Fejl);
        Assert.Equal(BrugerFejl.RolleUgyldig,
            (await service.OpretAsync(Platform, organisationId, "Navn", NyEmail(), Roller.PlatformAdmin, Kode)).Fejl);
    }

    [Fact]
    public async Task Kun_platformsadmin_kan_oprette_aendre_og_nulstille()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrugerService>();
        var organisationId = await NyOrganisationAsync(scope.ServiceProvider);
        var bruger = await OpretAsync(scope.ServiceProvider, organisationId);
        var orgAdmin = new Aktoer(Guid.CreateVersion7(), organisationId, false, true);
        var almindelig = new Aktoer(Guid.CreateVersion7(), organisationId, false, false);

        foreach (var aktoer in new[] { orgAdmin, almindelig })
        {
            Assert.Equal(BrugerFejl.IkkeTilladt,
                (await service.OpretAsync(aktoer, organisationId, "Navn", NyEmail(), Roller.Bruger, Kode)).Fejl);
            Assert.Equal(BrugerFejl.IkkeTilladt, (await service.SaetAktivAsync(aktoer, bruger.Id, false)).Fejl);
            Assert.Equal(BrugerFejl.IkkeTilladt, (await service.NulstilAdgangskodeAsync(aktoer, bruger.Id, Kode)).Fejl);
        }

        var gemt = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(bruger.Id.ToString());
        Assert.True(gemt!.Aktiv);
    }

    [Fact]
    public async Task Organisationsadmin_ser_kun_egen_organisations_brugere()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrugerService>();
        var orgA = await NyOrganisationAsync(scope.ServiceProvider);
        var orgB = await NyOrganisationAsync(scope.ServiceProvider);
        var emailA = NyEmail();
        var emailB = NyEmail();
        await OpretAsync(scope.ServiceProvider, orgA, email: emailA);
        await OpretAsync(scope.ServiceProvider, orgB, email: emailB);
        var adminA = new Aktoer(Guid.CreateVersion7(), orgA, false, true);

        var liste = await service.OversigtAsync(adminA);

        Assert.Contains(liste, b => b.Email == emailA);
        Assert.DoesNotContain(liste, b => b.Email == emailB);
        Assert.All(liste, b => Assert.Equal(orgA, b.OrganisationId));
    }

    [Fact]
    public async Task Organisationsadmin_kan_ikke_bede_om_en_anden_organisations_brugere()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrugerService>();
        var orgA = await NyOrganisationAsync(scope.ServiceProvider);
        var orgB = await NyOrganisationAsync(scope.ServiceProvider);
        var adminA = new Aktoer(Guid.CreateVersion7(), orgA, false, true);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.OversigtAsync(adminA, orgB));
    }

    [Fact]
    public async Task Almindelig_bruger_og_bruger_uden_organisation_ser_ingen_liste()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrugerService>();
        var org = await NyOrganisationAsync(scope.ServiceProvider);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.OversigtAsync(new Aktoer(Guid.CreateVersion7(), org, false, false)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.OversigtAsync(new Aktoer(Guid.CreateVersion7(), null, false, true)));
    }

    [Fact]
    public async Task Platformsadmin_ser_alle_og_kan_afgraense_til_en_organisation()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrugerService>();
        var orgA = await NyOrganisationAsync(scope.ServiceProvider);
        var orgB = await NyOrganisationAsync(scope.ServiceProvider);
        var emailA = NyEmail();
        var emailB = NyEmail();
        await OpretAsync(scope.ServiceProvider, orgA, Roller.OrganisationAdmin, emailA);
        await OpretAsync(scope.ServiceProvider, orgB, email: emailB);

        var alle = await service.OversigtAsync(Platform);
        var kunA = await service.OversigtAsync(Platform, orgA);

        Assert.Contains(alle, b => b.Email == emailA);
        Assert.Contains(alle, b => b.Email == emailB);
        Assert.Single(kunA);
        Assert.Equal(emailA, kunA[0].Email);
        Assert.Equal(Roller.OrganisationAdmin, kunA[0].Rolle);
        Assert.NotNull(kunA[0].OrganisationNavn);
    }

    [Fact]
    public async Task Deaktiver_og_aktiver_aendrer_status_og_ugyldiggoer_sessioner()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrugerService>();
        var brugere = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var bruger = await OpretAsync(scope.ServiceProvider, await NyOrganisationAsync(scope.ServiceProvider));
        var stempelFoer = (await brugere.FindByIdAsync(bruger.Id.ToString()))!.SecurityStamp;

        Assert.True((await service.SaetAktivAsync(Platform, bruger.Id, false)).Ok);
        var deaktiveret = (await brugere.FindByIdAsync(bruger.Id.ToString()))!;
        Assert.False(deaktiveret.Aktiv);
        Assert.NotEqual(stempelFoer, deaktiveret.SecurityStamp);

        Assert.True((await service.SaetAktivAsync(Platform, bruger.Id, true)).Ok);
        Assert.True((await brugere.FindByIdAsync(bruger.Id.ToString()))!.Aktiv);
    }

    [Fact]
    public async Task Platformsadmin_kan_ikke_deaktivere_sig_selv_og_ukendt_bruger_giver_ikke_fundet()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrugerService>();
        var bruger = await OpretAsync(scope.ServiceProvider, await NyOrganisationAsync(scope.ServiceProvider));
        var sig = new Aktoer(bruger.Id, null, true, false);

        Assert.Equal(BrugerFejl.KanIkkeAendreSig, (await service.SaetAktivAsync(sig, bruger.Id, false)).Fejl);
        Assert.Equal(BrugerFejl.IkkeFundet, (await service.SaetAktivAsync(Platform, Guid.CreateVersion7(), false)).Fejl);
        Assert.Equal(BrugerFejl.IkkeFundet, (await service.NulstilAdgangskodeAsync(Platform, Guid.CreateVersion7(), Kode)).Fejl);
    }

    [Fact]
    public async Task Nulstil_saetter_ny_kode_kraever_skift_og_laaser_op()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrugerService>();
        var brugere = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var bruger = await OpretAsync(scope.ServiceProvider, await NyOrganisationAsync(scope.ServiceProvider));

        // Brugeren har valgt sin egen kode og er låst efter for mange forsøg.
        var gemt = (await brugere.FindByIdAsync(bruger.Id.ToString()))!;
        await brugere.ChangePasswordAsync(gemt, Kode, "Egen-Kode-2026!");
        gemt.SkalSkifteAdgangskode = false;
        await brugere.UpdateAsync(gemt);
        await brugere.SetLockoutEndDateAsync(gemt, DateTimeOffset.UtcNow.AddMinutes(30));

        const string ny = "Ny-Midlertidig-3#";
        var resultat = await service.NulstilAdgangskodeAsync(Platform, bruger.Id, ny);

        Assert.True(resultat.Ok);
        var efter = (await brugere.FindByIdAsync(bruger.Id.ToString()))!;
        Assert.True(await brugere.CheckPasswordAsync(efter, ny));
        Assert.False(await brugere.CheckPasswordAsync(efter, "Egen-Kode-2026!"));
        Assert.True(efter.SkalSkifteAdgangskode);
        Assert.False(await brugere.IsLockedOutAsync(efter));
    }

    [Fact]
    public async Task Nulstil_afviser_svag_kode_uden_at_aendre_noget()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrugerService>();
        var brugere = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var bruger = await OpretAsync(scope.ServiceProvider, await NyOrganisationAsync(scope.ServiceProvider));

        var resultat = await service.NulstilAdgangskodeAsync(Platform, bruger.Id, "svag");

        Assert.Equal(BrugerFejl.AdgangskodeAfvist, resultat.Fejl);
        Assert.True(await brugere.CheckPasswordAsync((await brugere.FindByIdAsync(bruger.Id.ToString()))!, Kode));
    }
}
