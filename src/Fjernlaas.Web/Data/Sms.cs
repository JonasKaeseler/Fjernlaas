namespace Fjernlaas.Web.Data;

public enum SmsNummerStatus
{
    Aktiv = 0,
    Deaktiveret = 1
}

/// <summary>
/// Et virtuelt nummer, InMobile sender indgående SMS'er fra. Webhook-adressen indeholder en hemmelig token,
/// og kun et hash af den gemmes (token vises kun, når den oprettes).
/// </summary>
public class SmsNummer
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganisationId { get; set; }

    /// <summary>Visningsnavn, fx "Roshage-nummer".</summary>
    public string Navn { get; set; } = string.Empty;

    /// <summary>Selve telefonnummeret, kun til visning.</summary>
    public string? Nummer { get; set; }

    /// <summary>SHA-256 af token, som små hex-tegn. Unikt.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>De første tegn af token, så man kan kende den fra andre i admin-UI'et.</summary>
    public string TokenPraefiks { get; set; } = string.Empty;

    public SmsNummerStatus Status { get; set; } = SmsNummerStatus.Aktiv;

    /// <summary>UTC.</summary>
    public DateTime OprettetTid { get; set; } = DateTime.UtcNow;
}

public enum NoegleordStatus
{
    Aktiv = 0,
    Deaktiveret = 1
}

/// <summary>Et ord i SMS'en, der åbner en bestemt ressource. Unikt pr. SMS-nummer.</summary>
public class Noegleord
{
    private string _ord = string.Empty;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganisationId { get; set; }

    public Guid SmsNummerId { get; set; }

    public Guid RessourceId { get; set; }

    public string Ord
    {
        get => _ord;
        set
        {
            _ord = (value ?? string.Empty).Trim();
            OrdNormaliseret = Normalisering.Normaliser(_ord);
        }
    }

    public string OrdNormaliseret { get; private set; } = string.Empty;

    public NoegleordStatus Status { get; set; } = NoegleordStatus.Aktiv;

    /// <summary>UTC.</summary>
    public DateTime OprettetTid { get; set; } = DateTime.UtcNow;
}
