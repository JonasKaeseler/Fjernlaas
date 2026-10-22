using System.Text;

namespace Fjernlaas.Web.Services;

/// <summary>Læser en indgående SMS fra InMobiles webhook-kald.</summary>
public static class SmsEndpoint
{
    public const int MaxTegn = 4096;

    /// <summary>
    /// InMobile sender teksten "afsender;tekst". Den læses som rå body ved POST, som feltet "value" i en formular,
    /// eller som parameteren "value" ved GET. Mere end 4096 tegn afvises ved at blive skåret af.
    /// </summary>
    public static async Task<string?> LaesBeskedAsync(HttpRequest request, CancellationToken ct)
    {
        if (HttpMethods.IsGet(request.Method)) return request.Query["value"].FirstOrDefault();

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            return form["value"].FirstOrDefault();
        }

        using var laeser = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var buffer = new char[MaxTegn];
        var antal = await laeser.ReadBlockAsync(buffer.AsMemory(), ct);
        return new string(buffer, 0, antal);
    }
}
