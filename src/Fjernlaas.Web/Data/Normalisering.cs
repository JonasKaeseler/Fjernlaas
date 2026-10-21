namespace Fjernlaas.Web.Data;

/// <summary>
/// Samler reglen for normaliserede nøgler. Unikke felter får en kolonne med den
/// normaliserede værdi, så unikhed ikke afhænger af databasens collation.
/// </summary>
public static class Normalisering
{
    public static string Normaliser(string? vaerdi) =>
        (vaerdi ?? string.Empty).Trim().ToUpperInvariant();
}
