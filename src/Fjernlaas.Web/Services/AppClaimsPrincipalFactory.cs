using System.Security.Claims;
using Fjernlaas.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Fjernlaas.Web.Services;

/// <summary>Lægger organisation og visningsnavn på brugerens claims, så de kan bruges uden databasekald.</summary>
public class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> brugere,
    RoleManager<IdentityRole<Guid>> roller,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(brugere, roller, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser bruger)
    {
        var identitet = await base.GenerateClaimsAsync(bruger);

        identitet.AddClaim(new Claim(AppClaims.Navn, bruger.Navn));
        if (bruger.OrganisationId is { } organisationId)
        {
            identitet.AddClaim(new Claim(AppClaims.OrganisationId, organisationId.ToString()));
        }

        return identitet;
    }
}
