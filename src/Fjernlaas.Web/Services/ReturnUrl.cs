namespace Fjernlaas.Web.Services;

/// <summary>Accepterer kun lokale stier efter login, så login ikke kan bruges til at sende folk til andre sider.</summary>
public static class ReturnUrl
{
    public static string Sikker(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "/";
        if (url[0] != '/') return "/";
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\')) return "/";
        if (url.Any(char.IsControl)) return "/";
        return url;
    }
}
