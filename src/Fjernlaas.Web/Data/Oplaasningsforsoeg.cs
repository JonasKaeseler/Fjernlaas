namespace Fjernlaas.Web.Data;

public enum Udfald
{
    Aabnet = 0,
    AfvistUgyldigtFormat,
    AfvistNummerDeaktiveret,
    AfvistOrganisationDeaktiveret,
    AfvistDublet,
    AfvistForMangeForsoeg,
    AfvistUkendtOrd,
    AfvistLokationDeaktiveret,
    AfvistRessourcePause,
    AfvistIkkeKonfigureret,
    AfvistForbindelseDeaktiveret,
    TTLockFejl
}

/// <summary>Ét forsøg på at åbne en dør via SMS, også de afviste. Grundlag for oplåsningsloggen og for grænsen pr. afsender.</summary>
public class Oplaasningsforsoeg
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>UTC.</summary>
    public DateTime TidspunktUtc { get; set; }

    public Guid OrganisationId { get; set; }

    public Guid SmsNummerId { get; set; }

    public Guid? RessourceId { get; set; }

    /// <summary>Afsenderens nummer på formen +45xxxxxxxx, eller "ukendt", hvis beskeden ikke kunne læses.</summary>
    public string Afsender { get; set; } = string.Empty;

    /// <summary>Teksten i SMS'en (højst 500 tegn). Ved ugyldigt format gemmes den rå streng.</summary>
    public string Tekst { get; set; } = string.Empty;

    public string? Noegleord { get; set; }

    public Udfald Udfald { get; set; }

    public int? TTLockErrcode { get; set; }

    public string? Fejltekst { get; set; }
}
