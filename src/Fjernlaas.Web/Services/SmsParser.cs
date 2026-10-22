using System.Text.RegularExpressions;

namespace Fjernlaas.Web.Services;

/// <summary>En indgående SMS som vores platform forstår den.</summary>
/// <param name="Afsender">Afsenderens nummer på formen +45xxxxxxxx.</param>
/// <param name="Tekst">Hele teksten, uden kontroltegn og med trimmede mellemrum (højst 500 tegn).</param>
/// <param name="Noegleord">Første ord i teksten, uden tegnsætning i enderne.</param>
public sealed record SmsBesked(string Afsender, string Tekst, string Noegleord);

/// <summary>
/// Læser InMobiles webhook-streng på formen "afsendernummer;tekst", fx "4512345678;Roshage\r".
/// Strengen splittes ved det første semikolon, så et semikolon i selve teksten ikke gør skade.
/// </summary>
public static partial class SmsParser
{
    public const int MaxTekstLaengde = 500;

    private static readonly char[] TegnsaetningIEnderne = ['.', ',', '!', '?', ':', ';', '"', '\''];

    public static SmsBesked? Parse(string? raa)
    {
        if (string.IsNullOrWhiteSpace(raa)) return null;

        var skille = raa.IndexOf(';');
        if (skille < 0) return null;

        var afsender = NormaliserNummer(raa[..skille]);
        if (afsender is null) return null;

        var tekst = Mellemrum().Replace(FjernKontroltegn(raa[(skille + 1)..]), " ").Trim();
        if (tekst.Length == 0) return null;
        if (tekst.Length > MaxTekstLaengde) tekst = tekst[..MaxTekstLaengde];

        var foersteOrd = tekst.Split(' ', 2)[0].Trim(TegnsaetningIEnderne);
        if (foersteOrd.Length == 0) return null;

        return new SmsBesked(afsender, tekst, foersteOrd);
    }

    /// <summary>Giver +landekode og tal, ellers null. Otte cifre regnes som et dansk nummer.</summary>
    public static string? NormaliserNummer(string? nummer)
    {
        if (string.IsNullOrWhiteSpace(nummer)) return null;

        var cifre = new string(nummer.Where(char.IsAsciiDigit).ToArray());
        if (cifre.StartsWith("00", StringComparison.Ordinal)) cifre = cifre[2..];
        if (cifre.Length == 8) cifre = "45" + cifre;

        return cifre.Length is >= 8 and <= 15 ? "+" + cifre : null;
    }

    private static string FjernKontroltegn(string tekst) =>
        new(tekst.Select(c => char.IsControl(c) ? ' ' : c).ToArray());

    [GeneratedRegex(@"\s+")]
    private static partial Regex Mellemrum();
}
