using System.Security.Cryptography;

namespace Fjernlaas.Web.Services;

/// <summary>Skaber midlertidige adgangskoder, der opfylder kravene og er til at læse op og skrive af.</summary>
public static class Adgangskoder
{
    private const string Store = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Smaa = "abcdefghijkmnpqrstuvwxyz";
    private const string Tal = "23456789";
    private const string Tegn = "!#%+-=?";

    public static string Ny(int laengde = 14)
    {
        if (laengde < 10) throw new ArgumentOutOfRangeException(nameof(laengde), "Mindst 10 tegn.");

        var alle = Store + Smaa + Tal + Tegn;
        var tegn = new List<char>
        {
            Vaelg(Store), Vaelg(Smaa), Vaelg(Tal), Vaelg(Tegn)
        };
        while (tegn.Count < laengde) tegn.Add(Vaelg(alle));

        // Bland, så de fire påkrævede tegn ikke altid står først.
        for (var i = tegn.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (tegn[i], tegn[j]) = (tegn[j], tegn[i]);
        }

        return new string(tegn.ToArray());
    }

    private static char Vaelg(string saet) => saet[RandomNumberGenerator.GetInt32(saet.Length)];
}
