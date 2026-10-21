using Microsoft.AspNetCore.Identity;

namespace Fjernlaas.Web.Data;

/// <summary>
/// Bruger i platformen. OrganisationId er null for platformsadministratorer
/// og udfyldt for alle brugere, der hører til en organisation.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid? OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public string Navn { get; set; } = string.Empty;

    /// <summary>Falsk, når brugeren er deaktiveret af en administrator.</summary>
    public bool Aktiv { get; set; } = true;

    /// <summary>UTC.</summary>
    public DateTime OprettetTid { get; set; } = DateTime.UtcNow;

    /// <summary>UTC.</summary>
    public DateTime? SidstLoggetInd { get; set; }
}
