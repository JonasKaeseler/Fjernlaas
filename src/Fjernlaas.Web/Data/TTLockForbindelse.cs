namespace Fjernlaas.Web.Data;

public enum ForbindelseStatus
{
    Aktiv = 0,
    Deaktiveret = 1
}

/// <summary>
/// En organisations forbindelse til sin TTLock-konto. Adgangskoden (som MD5-hash, som TTLock kræver) og
/// tokens gemmes kun krypteret. Appens clientId og clientSecret ligger i konfigurationen, ikke her.
/// </summary>
public class TTLockForbindelse
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganisationId { get; set; }

    /// <summary>Visningsnavn, fx "Roshage TTLock-konto".</summary>
    public string Navn { get; set; } = string.Empty;

    /// <summary>TTLock-appens brugernavn (ikke udviklerkontoen).</summary>
    public string TTLockBrugernavn { get; set; } = string.Empty;

    /// <summary>Krypteret MD5-hash af TTLock-adgangskoden.</summary>
    public string AdgangskodeHashBeskyttet { get; set; } = string.Empty;

    public string? AccessTokenBeskyttet { get; set; }

    public string? RefreshTokenBeskyttet { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? TokenUdloeberTid { get; set; }

    public ForbindelseStatus Status { get; set; } = ForbindelseStatus.Aktiv;

    /// <summary>Seneste fejl fra TTLock, vist i admin-UI'et. Tom, når alt virker.</summary>
    public string? SidsteFejl { get; set; }

    /// <summary>UTC.</summary>
    public DateTime OprettetTid { get; set; } = DateTime.UtcNow;
}
