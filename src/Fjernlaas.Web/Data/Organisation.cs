namespace Fjernlaas.Web.Data;

public enum OrganisationStatus
{
    Aktiv = 0,
    Deaktiveret = 1
}

public class Organisation
{
    private string _navn = string.Empty;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Navn
    {
        get => _navn;
        set
        {
            _navn = (value ?? string.Empty).Trim();
            NavnNormaliseret = Normalisering.Normaliser(_navn);
        }
    }

    /// <summary>Normaliseret navn med unikt indeks. Sættes automatisk via Navn.</summary>
    public string NavnNormaliseret { get; private set; } = string.Empty;

    public OrganisationStatus Status { get; set; } = OrganisationStatus.Aktiv;

    /// <summary>UTC.</summary>
    public DateTime OprettetTid { get; set; } = DateTime.UtcNow;

    public ICollection<ApplicationUser> Brugere { get; set; } = new List<ApplicationUser>();
}
