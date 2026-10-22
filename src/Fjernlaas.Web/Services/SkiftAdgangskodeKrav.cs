namespace Fjernlaas.Web.Services;

/// <summary>
/// En bruger med midlertidig adgangskode må kun se siden, hvor koden skiftes, logge ud og hente
/// filer (typografi, scripts). Alt andet sendes til skift-siden.
/// </summary>
public static class SkiftAdgangskodeKrav
{
    public const string Side = "/skift-adgangskode";

    public static bool Tilladt(PathString sti)
    {
        var vaerdi = sti.Value ?? "/";

        if (vaerdi.Equals(Side, StringComparison.OrdinalIgnoreCase)) return true;
        if (vaerdi.Equals("/logout", StringComparison.OrdinalIgnoreCase)) return true;
        if (vaerdi.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)) return true;
        if (vaerdi.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)) return true;

        // Filer med endelse (app.css, favicon, ...).
        return Path.HasExtension(vaerdi);
    }
}
