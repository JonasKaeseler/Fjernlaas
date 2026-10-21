using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fjernlaas.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class LoginRegelTests(SqlServerFixture fixture)
{
    private const string Adgangskode = "Test-Pass1!";

    private ServiceProvider ByggServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication().AddIdentityCookies();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(fixture.ConnectionString));
        services.AddIdentityCore<ApplicationUser>(o => o.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
            .AddSignInManager<AppSignInManager>();
        services.AddScoped<OrganisationService>();
        return services.BuildServiceProvider();
    }

    private static async Task<ApplicationUser> OpretBrugerAsync(IServiceProvider sp, Guid? organisationId, bool aktiv = true)
    {
        var email = $"bruger-{Guid.NewGuid():N}@example.com";
        var bruger = new ApplicationUser
        {
            UserName = email,
            Email = email,
            Navn = "Testbruger",
            OrganisationId = organisationId,
            Aktiv = aktiv
        };
        var resultat = await sp.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(bruger, Adgangskode);
        Assert.True(resultat.Succeeded, string.Join("; ", resultat.Errors.Select(e => e.Description)));
        return bruger;
    }

    private static async Task<Organisation> OpretOrganisationAsync(IServiceProvider sp) =>
        (await sp.GetRequiredService<OrganisationService>().OpretAsync($"Butik {Guid.NewGuid():N}")).Organisation!;

    [Fact]
    public async Task Aktiv_bruger_i_aktiv_organisation_kan_logge_ind()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var org = await OpretOrganisationAsync(scope.ServiceProvider);
        var bruger = await OpretBrugerAsync(scope.ServiceProvider, org.Id);

        var resultat = await scope.ServiceProvider.GetRequiredService<AppSignInManager>()
            .CheckPasswordSignInAsync(bruger, Adgangskode, lockoutOnFailure: false);

        Assert.True(resultat.Succeeded);
    }

    [Fact]
    public async Task Platformsadmin_uden_organisation_kan_logge_ind()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var bruger = await OpretBrugerAsync(scope.ServiceProvider, organisationId: null);

        var resultat = await scope.ServiceProvider.GetRequiredService<AppSignInManager>()
            .CheckPasswordSignInAsync(bruger, Adgangskode, lockoutOnFailure: false);

        Assert.True(resultat.Succeeded);
    }

    [Fact]
    public async Task Forkert_adgangskode_afvises()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var bruger = await OpretBrugerAsync(scope.ServiceProvider, organisationId: null);

        var resultat = await scope.ServiceProvider.GetRequiredService<AppSignInManager>()
            .CheckPasswordSignInAsync(bruger, "Forkert-Pass9!", lockoutOnFailure: false);

        Assert.False(resultat.Succeeded);
    }

    [Fact]
    public async Task Deaktiveret_bruger_kan_ikke_logge_ind()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var org = await OpretOrganisationAsync(scope.ServiceProvider);
        var bruger = await OpretBrugerAsync(scope.ServiceProvider, org.Id, aktiv: false);

        var signIn = scope.ServiceProvider.GetRequiredService<AppSignInManager>();

        Assert.False(await signIn.CanSignInAsync(bruger));
        Assert.False((await signIn.CheckPasswordSignInAsync(bruger, Adgangskode, false)).Succeeded);
    }

    [Fact]
    public async Task Bruger_i_deaktiveret_organisation_kan_ikke_logge_ind_men_kan_igen_naar_organisationen_aktiveres()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var organisationer = scope.ServiceProvider.GetRequiredService<OrganisationService>();
        var org = await OpretOrganisationAsync(scope.ServiceProvider);
        var bruger = await OpretBrugerAsync(scope.ServiceProvider, org.Id);
        var signIn = scope.ServiceProvider.GetRequiredService<AppSignInManager>();

        await organisationer.SaetStatusAsync(org.Id, OrganisationStatus.Deaktiveret);
        Assert.False(await signIn.CanSignInAsync(bruger));

        await organisationer.SaetStatusAsync(org.Id, OrganisationStatus.Aktiv);
        Assert.True(await signIn.CanSignInAsync(bruger));
    }

    [Fact]
    public async Task Konto_laases_efter_gentagne_fejl()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var bruger = await OpretBrugerAsync(scope.ServiceProvider, organisationId: null);
        var signIn = scope.ServiceProvider.GetRequiredService<AppSignInManager>();

        Microsoft.AspNetCore.Identity.SignInResult resultat = default!;
        for (var i = 0; i < 6; i++)
        {
            resultat = await signIn.CheckPasswordSignInAsync(bruger, "Forkert-Pass9!", lockoutOnFailure: true);
        }

        Assert.True(resultat.IsLockedOut);
        Assert.False((await signIn.CheckPasswordSignInAsync(bruger, Adgangskode, lockoutOnFailure: true)).Succeeded);
    }

    [Fact]
    public async Task Claims_indeholder_organisation_navn_og_roller()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var org = await OpretOrganisationAsync(scope.ServiceProvider);
        var bruger = await OpretBrugerAsync(scope.ServiceProvider, org.Id);
        var roller = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roller.RoleExistsAsync(Roller.OrganisationAdmin))
            await roller.CreateAsync(new IdentityRole<Guid>(Roller.OrganisationAdmin) { Id = Guid.CreateVersion7() });
        await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .AddToRoleAsync(bruger, Roller.OrganisationAdmin);

        var principal = await scope.ServiceProvider
            .GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(bruger);

        Assert.Equal(org.Id.ToString(), principal.FindFirst(AppClaims.OrganisationId)?.Value);
        Assert.Equal("Testbruger", principal.FindFirst(AppClaims.Navn)?.Value);
        Assert.True(principal.IsInRole(Roller.OrganisationAdmin));
    }

    [Fact]
    public async Task Platformsadmin_har_ingen_organisation_claim()
    {
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();
        var bruger = await OpretBrugerAsync(scope.ServiceProvider, organisationId: null);

        var principal = await scope.ServiceProvider
            .GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(bruger);

        Assert.Null(principal.FindFirst(AppClaims.OrganisationId));
    }
}
