namespace Fjernlaas.Web.Data;

public enum RessourceType
{
    Doer = 0
}

public enum RessourceStatus
{
    Aktiv = 0,

    /// <summary>Nødstop: ressourcen kan ikke åbnes, før den sættes til aktiv igen.</summary>
    Pause = 1
}

/// <summary>Noget, der kan låses op, fx en dør. Hører til en lokation.</summary>
public class Ressource
{
    private string _navn = string.Empty;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Samme organisation som lokationen. Ligger her for at kunne filtrere og slå op uden join.</summary>
    public Guid OrganisationId { get; set; }

    public Guid LokationId { get; set; }

    public Lokation? Lokation { get; set; }

    public string Navn
    {
        get => _navn;
        set
        {
            _navn = (value ?? string.Empty).Trim();
            NavnNormaliseret = Normalisering.Normaliser(_navn);
        }
    }

    /// <summary>Normaliseret navn. Unikt inden for lokationen. Sættes automatisk via Navn.</summary>
    public string NavnNormaliseret { get; private set; } = string.Empty;

    public RessourceType Type { get; set; } = RessourceType.Doer;

    /// <summary>Organisationens TTLock-forbindelse, som låsen hører under. Tom, så længe ressourcen ikke er koblet til en lås.</summary>
    public Guid? ForbindelseId { get; set; }

    /// <summary>Låsens id hos TTLock.</summary>
    public long? TTLockLockId { get; set; }

    public RessourceStatus Status { get; set; } = RessourceStatus.Aktiv;

    /// <summary>UTC.</summary>
    public DateTime OprettetTid { get; set; } = DateTime.UtcNow;
}
