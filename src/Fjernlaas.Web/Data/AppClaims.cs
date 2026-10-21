namespace Fjernlaas.Web.Data;

/// <summary>Egne claim-typer og autorisationspolitikker.</summary>
public static class AppClaims
{
    /// <summary>Id på brugerens organisation. Mangler for platformsadministratorer.</summary>
    public const string OrganisationId = "fjernlaas:organisation_id";

    public const string Navn = "fjernlaas:navn";
}

public static class Politikker
{
    /// <summary>Kun platformsadministratorer.</summary>
    public const string PlatformAdmin = "PlatformAdmin";
}
