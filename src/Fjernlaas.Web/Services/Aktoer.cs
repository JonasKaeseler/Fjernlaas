using System.Security.Claims;
using Fjernlaas.Web.Data;

namespace Fjernlaas.Web.Services;

/// <summary>
/// Hvem der udfører en handling. Services får altid aktøren med og afgør selv, hvad han må se og ændre.
/// Det er det, der holder organisationerne adskilt, og det kan testes uden UI.
/// </summary>
public sealed record Aktoer(Guid? BrugerId, Guid? OrganisationId, bool ErPlatformAdmin, bool ErOrganisationAdmin)
{
    public static Aktoer Fra(ClaimsPrincipal bruger)
    {
        Guid? id = Guid.TryParse(bruger.FindFirstValue(ClaimTypes.NameIdentifier), out var b) ? b : null;
        Guid? organisation = Guid.TryParse(bruger.FindFirstValue(AppClaims.OrganisationId), out var o) ? o : null;

        return new Aktoer(id, organisation, bruger.IsInRole(Roller.PlatformAdmin), bruger.IsInRole(Roller.OrganisationAdmin));
    }
}
