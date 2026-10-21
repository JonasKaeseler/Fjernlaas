using System.Security.Claims;
using Fjernlaas.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fjernlaas.Web.Services;

/// <summary>
/// Udvider Identitys login-regler: en deaktiveret bruger, eller en bruger i en deaktiveret
/// organisation, kan hverken logge ind eller beholde en eksisterende session
/// (sessionen tjekkes igen, når Identitys sikkerhedsstempel valideres).
/// </summary>
public class AppSignInManager(
    UserManager<ApplicationUser> brugere,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> options,
    ILogger<SignInManager<ApplicationUser>> log,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    AppDbContext db)
    : SignInManager<ApplicationUser>(brugere, contextAccessor, claimsFactory, options, log, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        if (!await base.CanSignInAsync(user)) return false;
        if (!user.Aktiv) return false;

        if (user.OrganisationId is { } organisationId)
        {
            var status = await db.Organisationer
                .Where(o => o.Id == organisationId)
                .Select(o => (OrganisationStatus?)o.Status)
                .SingleOrDefaultAsync();

            if (status != OrganisationStatus.Aktiv) return false;
        }

        return true;
    }

    public override async Task<ApplicationUser?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        var bruger = await base.ValidateSecurityStampAsync(principal);
        if (bruger is null) return null;

        return await CanSignInAsync(bruger) ? bruger : null;
    }
}
