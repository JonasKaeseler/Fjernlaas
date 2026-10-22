using Fjernlaas.Web.Data;

namespace Fjernlaas.Web.Services;

/// <summary>Fortæller databasekonteksten, hvem der spørger, så lokationer og ressourcer afgrænses til rette organisation.</summary>
public static class Tenant
{
    public static void Saet(AppDbContext db, Aktoer aktoer) =>
        db.SaetTenant(aktoer.ErPlatformAdmin, aktoer.OrganisationId);

    /// <summary>Må aktøren administrere lokationer og ressourcer? Platformsadmin eller organisationsadmin.</summary>
    public static bool KanAdministrere(Aktoer aktoer) =>
        aktoer.ErPlatformAdmin || (aktoer.ErOrganisationAdmin && aktoer.OrganisationId is not null);
}
