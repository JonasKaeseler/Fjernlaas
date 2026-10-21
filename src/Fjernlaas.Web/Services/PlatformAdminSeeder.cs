using Fjernlaas.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Fjernlaas.Web.Services;

/// <summary>
/// Sikrer at rollerne findes, og at der findes en platformsadmin (bruger uden organisation).
/// Kan køres flere gange: en eksisterende bruger bliver aldrig ændret, og adgangskoden røres ikke.
/// </summary>
public class PlatformAdminSeeder(
    UserManager<ApplicationUser> brugere,
    RoleManager<IdentityRole<Guid>> roller,
    ILogger<PlatformAdminSeeder> log)
{
    public async Task SikrAsync(string email, string navn, string adgangskode)
    {
        foreach (var rolle in Roller.Alle)
        {
            if (!await roller.RoleExistsAsync(rolle))
            {
                Tjek(await roller.CreateAsync(new IdentityRole<Guid>(rolle) { Id = Guid.CreateVersion7() }));
            }
        }

        var bruger = await brugere.FindByEmailAsync(email);
        if (bruger is null)
        {
            bruger = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                Navn = navn,
                OrganisationId = null
            };
            Tjek(await brugere.CreateAsync(bruger, adgangskode));
            log.LogInformation("Platformsadmin {Email} oprettet", email);
        }
        else if (bruger.OrganisationId is not null)
        {
            throw new InvalidOperationException(
                $"Brugeren {email} findes allerede og hører til en organisation, så den kan ikke være platformsadmin.");
        }

        if (!await brugere.IsInRoleAsync(bruger, Roller.PlatformAdmin))
        {
            Tjek(await brugere.AddToRoleAsync(bruger, Roller.PlatformAdmin));
        }
    }

    private static void Tjek(IdentityResult resultat)
    {
        if (!resultat.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", resultat.Errors.Select(e => e.Description)));
        }
    }
}
