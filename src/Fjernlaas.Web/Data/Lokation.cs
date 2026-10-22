namespace Fjernlaas.Web.Data;

public enum LokationStatus
{
    Aktiv = 0,
    Deaktiveret = 1
}

/// <summary>Et sted, hvor en organisation har ressourcer, fx en butik.</summary>
public class Lokation
{
    private string _navn = string.Empty;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganisationId { get; set; }

    public string Navn
    {
        get => _navn;
        set
        {
            _navn = (value ?? string.Empty).Trim();
            NavnNormaliseret = Normalisering.Normaliser(_navn);
        }
    }

    /// <summary>Normaliseret navn. Unikt inden for organisationen. Sættes automatisk via Navn.</summary>
    public string NavnNormaliseret { get; private set; } = string.Empty;

    public string? Adresse { get; set; }

    /// <summary>IANA-navn, fx Europe/Copenhagen.</summary>
    public string Tidszone { get; set; } = "Europe/Copenhagen";

    public LokationStatus Status { get; set; } = LokationStatus.Aktiv;

    /// <summary>UTC.</summary>
    public DateTime OprettetTid { get; set; } = DateTime.UtcNow;

    public ICollection<Ressource> Ressourcer { get; set; } = new List<Ressource>();
}
