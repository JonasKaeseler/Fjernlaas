using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fjernlaas.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class PlatformAdminSeederTests(SqlServerFixture fixture)
{
    private const string Adgangskode = "Test-Pass1!";

    private static string NyEmail() => $"admin-{Guid.NewGuid():N}@example.com";

    private ServiceProvider ByggServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(fixture.ConnectionString));
        services.AddIdentityCore<ApplicationUser>(o => o.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<PlatformAdminSeeder>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Opretter_roller_og_platformsadmin_uden_organisation()
    {
        var email = NyEmail();
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>()
            .SikrAsync(email, "Admin", Adgangskode);

        var brugere = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var bruger = await brugere.FindByEmailAsync(email);
        Assert.NotNull(bruger);
        Assert.Null(bruger!.OrganisationId);
        Assert.True(await brugere.IsInRoleAsync(bruger, Roller.PlatformAdmin));

        var roller = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var rolle in Roller.Alle)
            Assert.True(await roller.RoleExistsAsync(rolle), $"Rollen {rolle} mangler");
    }

    [Fact]
    public async Task Kan_koeres_igen_uden_at_aendre_eksisterende_bruger()
    {
        var email = NyEmail();
        await using var provider = ByggServices();

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>()
                .SikrAsync(email, "Admin", Adgangskode);
        }

        string? hashFoer;
        using (var scope = provider.CreateScope())
        {
            hashFoer = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByEmailAsync(email))!.PasswordHash;
        }

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>()
                .SikrAsync(email, "Andet navn", "Anden-Pass2!");
        }

        using (var scope = provider.CreateScope())
        {
            var brugere = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var bruger = (await brugere.FindByEmailAsync(email))!;
            Assert.Equal(hashFoer, bruger.PasswordHash);
            Assert.Equal("Admin", bruger.Navn);
            Assert.Single(await brugere.GetUsersInRoleAsync(Roller.PlatformAdmin), u => u.Id == bruger.Id);
        }
    }

    [Fact]
    public async Task Afviser_eksisterende_bruger_der_hoerer_til_en_organisation()
    {
        var email = NyEmail();
        await using var provider = ByggServices();
        using var scope = provider.CreateScope();

        var org = (await new OrganisationService(scope.ServiceProvider.GetRequiredService<AppDbContext>())
            .OpretAsync($"Butik {Guid.NewGuid():N}")).Organisation!;
        var brugere = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var oprettet = await brugere.CreateAsync(new ApplicationUser
        {
            UserName = email, Email = email, Navn = "Butiksbruger", OrganisationId = org.Id
        }, Adgangskode);
        Assert.True(oprettet.Succeeded);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>()
                .SikrAsync(email, "Admin", Adgangskode));
    }
}
