using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Fjernlaas.Web.Services;

public sealed class TTLockOptions
{
    /// <summary>Appens id hos TTLock. Hører til os og er ens for alle organisationer.</summary>
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Den europæiske endpoint. Dokumentationen nævner api.sciener.com.</summary>
    public string BaseUrl { get; set; } = "https://euapi.ttlock.com";

    public bool ErSat => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Fejlkoder fra TTLocks API. Succes er 0.</summary>
public static class TTLockKoder
{
    public const int Ok = 0;
    public const int UgyldigtToken = 10003;
    public const int TokenUgyldigt = 10004;
    public const int ForkertLogin = 10007;
    public const int UgyldigtRefreshToken = 10011;
    public const int ForMangeKald = 30006;
    public const int TidsstempelForskel = 80000;
    public const int IngenGateway = -2012;
    public const int IkkeUnderstoettet = -4043;

    /// <summary>Intern kode: ingen forbindelse, timeout eller et svar, vi ikke kunne læse.</summary>
    public const int Teknisk = -1000;

    /// <summary>Intern kode: de gemte oplysninger kan ikke læses.</summary>
    public const int OplysningerUlaeselige = -1001;

    /// <summary>Fejl, hvor et nyt token kan løse det.</summary>
    public static bool ErTokenFejl(int kode) => kode is UgyldigtToken or TokenUgyldigt;

    public static string Tekst(int kode, string? besked) => kode switch
    {
        ForkertLogin => "TTLock afviste brugernavn eller adgangskode.",
        UgyldigtRefreshToken => "TTLock afviste den gemte fornyelse. Forbindelsen logger ind igen.",
        UgyldigtToken or TokenUgyldigt => "TTLock afviste adgangstoken.",
        ForMangeKald => "TTLock har begrænset antallet af kald. Prøv igen om lidt.",
        TidsstempelForskel => "Serverens ur afviger for meget fra TTLocks tid.",
        IngenGateway => "Låsen har ingen gateway i nærheden.",
        IkkeUnderstoettet => "Låsen understøtter ikke fjernoplåsning. Slå fjernoplåsning til i TTLock-appens indstillinger for låsen.",
        Teknisk => string.IsNullOrWhiteSpace(besked) ? "Kunne ikke få kontakt til TTLock." : besked,
        OplysningerUlaeselige => "De gemte oplysninger kan ikke læses. Indtast adgangskoden til TTLock igen.",
        _ => string.IsNullOrWhiteSpace(besked) ? $"TTLock svarede med fejlkode {kode}." : $"TTLock svarede med fejlkode {kode}: {besked}"
    };
}

public sealed record TTLockSvar<T>(bool Ok, int ErrCode, string? Fejl, T? Data)
{
    public static TTLockSvar<T> Succes(T data) => new(true, TTLockKoder.Ok, null, data);

    public static TTLockSvar<T> Fejlet(int kode, string? besked = null) => new(false, kode, TTLockKoder.Tekst(kode, besked), default);

    /// <summary>Giver en fejl videre med den tekst, den allerede har.</summary>
    public static TTLockSvar<T> FejletMedTekst(int kode, string? tekst) => new(false, kode, tekst, default);
}

public sealed record TTLockToken(string AccessToken, string RefreshToken, DateTime UdloeberUtc);

public sealed record TTLockLaas(long LockId, string Navn, string? Alias, int? Batteri, bool HarGateway);

public interface ITTLockKlient
{
    Task<TTLockSvar<TTLockToken>> HentTokenAsync(string brugernavn, string adgangskodeHash, CancellationToken ct = default);

    Task<TTLockSvar<TTLockToken>> FornyTokenAsync(string refreshToken, CancellationToken ct = default);

    Task<TTLockSvar<IReadOnlyList<TTLockLaas>>> ListLaaseAsync(string accessToken, CancellationToken ct = default);

    Task<TTLockSvar<bool>> AabnAsync(string accessToken, long lockId, CancellationToken ct = default);
}

/// <summary>
/// Kalder TTLocks Cloud API V3 (POST, form-urlencoded, JSON tilbage). Fejl ligger i errcode, ikke i HTTP-status,
/// så alle svar læses for errcode. Hemmeligheder skrives aldrig i fejltekster.
/// </summary>
public sealed class TTLockKlient(HttpClient http, IOptions<TTLockOptions> options, TimeProvider tid) : ITTLockKlient
{
    private const int SideStoerrelse = 100;
    private const int MaxSider = 20;

    private TTLockOptions Opt => options.Value;

    /// <summary>TTLock kræver adgangskoden som 32 små hex-tegn, MD5.</summary>
    public static string Md5Hex(string adgangskode)
    {
#pragma warning disable CA5351 // MD5 er et krav fra TTLocks API, ikke vores eget valg.
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(adgangskode))).ToLowerInvariant();
#pragma warning restore CA5351
    }

    public async Task<TTLockSvar<TTLockToken>> HentTokenAsync(string brugernavn, string adgangskodeHash, CancellationToken ct = default)
    {
        var svar = await PostAsync("oauth2/token", new Dictionary<string, string>
        {
            ["clientId"] = Opt.ClientId,
            ["clientSecret"] = Opt.ClientSecret,
            ["username"] = brugernavn,
            ["password"] = adgangskodeHash
        }, ct);

        return TilToken(svar);
    }

    public async Task<TTLockSvar<TTLockToken>> FornyTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var svar = await PostAsync("oauth2/token", new Dictionary<string, string>
        {
            ["clientId"] = Opt.ClientId,
            ["clientSecret"] = Opt.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, ct);

        return TilToken(svar);
    }

    public async Task<TTLockSvar<IReadOnlyList<TTLockLaas>>> ListLaaseAsync(string accessToken, CancellationToken ct = default)
    {
        var alle = new List<TTLockLaas>();

        for (var side = 1; side <= MaxSider; side++)
        {
            var svar = await PostAsync("v3/lock/list", new Dictionary<string, string>
            {
                ["clientId"] = Opt.ClientId,
                ["accessToken"] = accessToken,
                ["pageNo"] = side.ToString(),
                ["pageSize"] = SideStoerrelse.ToString(),
                ["date"] = Nu()
            }, ct);

            if (!svar.Ok) return TTLockSvar<IReadOnlyList<TTLockLaas>>.FejletMedTekst(svar.ErrCode, svar.Fejl);

            if (!svar.Data.TryGetProperty("list", out var liste) || liste.ValueKind != JsonValueKind.Array)
                return TTLockSvar<IReadOnlyList<TTLockLaas>>.Fejlet(TTLockKoder.Teknisk, "Uventet svar fra TTLock.");

            var antal = 0;
            foreach (var l in liste.EnumerateArray())
            {
                antal++;
                if (!l.TryGetProperty("lockId", out var id) || !id.TryGetInt64(out var lockId)) continue;

                alle.Add(new TTLockLaas(
                    lockId,
                    Tekst(l, "lockName") ?? lockId.ToString(),
                    Tekst(l, "lockAlias"),
                    l.TryGetProperty("electricQuantity", out var b) && b.TryGetInt32(out var batteri) ? batteri : null,
                    l.TryGetProperty("hasGateway", out var g) && g.TryGetInt32(out var gw) && gw == 1));
            }

            if (antal < SideStoerrelse) break;
        }

        return TTLockSvar<IReadOnlyList<TTLockLaas>>.Succes(alle);
    }

    public async Task<TTLockSvar<bool>> AabnAsync(string accessToken, long lockId, CancellationToken ct = default)
    {
        var svar = await PostAsync("v3/lock/unlock", new Dictionary<string, string>
        {
            ["clientId"] = Opt.ClientId,
            ["accessToken"] = accessToken,
            ["lockId"] = lockId.ToString(),
            ["date"] = Nu()
        }, ct, kraeverErrcode: true);

        return svar.Ok ? TTLockSvar<bool>.Succes(true) : TTLockSvar<bool>.FejletMedTekst(svar.ErrCode, svar.Fejl);
    }

    private string Nu() => tid.GetUtcNow().ToUnixTimeMilliseconds().ToString();

    private static string? Tekst(JsonElement e, string navn) =>
        e.TryGetProperty(navn, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private TTLockSvar<TTLockToken> TilToken(TTLockSvar<JsonElement> svar)
    {
        if (!svar.Ok) return TTLockSvar<TTLockToken>.FejletMedTekst(svar.ErrCode, svar.Fejl);

        var d = svar.Data;
        var access = Tekst(d, "access_token");
        var refresh = Tekst(d, "refresh_token");
        if (access is null || refresh is null || !d.TryGetProperty("expires_in", out var e) || !e.TryGetInt64(out var sekunder))
            return TTLockSvar<TTLockToken>.Fejlet(TTLockKoder.Teknisk, "Uventet svar fra TTLock.");

        var udloeber = tid.GetUtcNow().UtcDateTime.AddSeconds(sekunder);
        return TTLockSvar<TTLockToken>.Succes(new TTLockToken(access, refresh, udloeber));
    }

    private async Task<TTLockSvar<JsonElement>> PostAsync(
        string sti, Dictionary<string, string> felter, CancellationToken ct, bool kraeverErrcode = false)
    {
        try
        {
            var url = new Uri(new Uri(Opt.BaseUrl.TrimEnd('/') + "/"), sti);
            using var indhold = new FormUrlEncodedContent(felter);
            using var svar = await http.PostAsync(url, indhold, ct);

            if (!svar.IsSuccessStatusCode)
                return TTLockSvar<JsonElement>.Fejlet(TTLockKoder.Teknisk, $"TTLock svarede med HTTP {(int)svar.StatusCode}.");

            var tekst = await svar.Content.ReadAsStringAsync(ct);
            using var dokument = JsonDocument.Parse(tekst);
            var rod = dokument.RootElement.Clone();

            if (rod.ValueKind != JsonValueKind.Object)
                return TTLockSvar<JsonElement>.Fejlet(TTLockKoder.Teknisk, "Uventet svar fra TTLock.");

            if (rod.TryGetProperty("errcode", out var kode) && kode.TryGetInt32(out var k))
            {
                if (k != TTLockKoder.Ok) return TTLockSvar<JsonElement>.Fejlet(k, Tekst(rod, "errmsg"));
            }
            else if (kraeverErrcode)
            {
                return TTLockSvar<JsonElement>.Fejlet(TTLockKoder.Teknisk, "Uventet svar fra TTLock.");
            }

            return TTLockSvar<JsonElement>.Succes(rod);
        }
        catch (HttpRequestException)
        {
            return TTLockSvar<JsonElement>.Fejlet(TTLockKoder.Teknisk, "Kunne ikke få kontakt til TTLock.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return TTLockSvar<JsonElement>.Fejlet(TTLockKoder.Teknisk, "TTLock svarede ikke i tide.");
        }
        catch (JsonException)
        {
            return TTLockSvar<JsonElement>.Fejlet(TTLockKoder.Teknisk, "Uventet svar fra TTLock.");
        }
    }
}
