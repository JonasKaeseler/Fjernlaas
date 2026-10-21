namespace Fjernlaas.Web.Data;

/// <summary>Navne på roller i platformen.</summary>
public static class Roller
{
    /// <summary>Jonas og Lukas. Ingen organisation. Opretter og vedligeholder organisationer og brugere.</summary>
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>Administrator i én organisation.</summary>
    public const string OrganisationAdmin = "OrganisationAdmin";

    /// <summary>Almindelig bruger i én organisation.</summary>
    public const string Bruger = "Bruger";

    public static readonly string[] Alle = [PlatformAdmin, OrganisationAdmin, Bruger];
}
