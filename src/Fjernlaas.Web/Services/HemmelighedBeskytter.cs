using Microsoft.AspNetCore.DataProtection;

namespace Fjernlaas.Web.Services;

/// <summary>Krypterer hemmeligheder (TTLock-adgangskodens hash og tokens), før de gemmes i databasen.</summary>
public interface IHemmelighedBeskytter
{
    string Beskyt(string klartekst);

    /// <summary>Kaster CryptographicException, hvis teksten ikke kan læses (fx fordi nøglerne er skiftet).</summary>
    string FjernBeskyttelse(string beskyttet);
}

/// <summary>
/// Bruger ASP.NET Core Data Protection. Nøglerne ligger som standard i brugerprofilen på udviklingsmaskinen.
/// I drift skal nøglerne gemmes et sikkert sted (FND-6/FND-7), ellers kan gemte hemmeligheder ikke læses efter en ny udrulning.
/// </summary>
public sealed class DataProtectionBeskytter(IDataProtectionProvider provider) : IHemmelighedBeskytter
{
    private readonly IDataProtector _beskytter = provider.CreateProtector("Fjernlaas.TTLock.v1");

    public string Beskyt(string klartekst) => _beskytter.Protect(klartekst);

    public string FjernBeskyttelse(string beskyttet) => _beskytter.Unprotect(beskyttet);
}
