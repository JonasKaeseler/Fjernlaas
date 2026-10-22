using System.Collections.Concurrent;
using System.Security.Cryptography;
using Fjernlaas.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Services;

/// <summary>
/// Én lås pr. forbindelse, så to samtidige oplåsninger ikke fornyer tokenet samtidig (fornyelse giver et nyt
/// refresh-token og gør det gamle ugyldigt). Gælder én instans af appen, hvilket er nok i beta.
/// </summary>
public sealed class TokenLaase
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _laase = new();

    public async Task<IDisposable> TagAsync(Guid forbindelseId, CancellationToken ct)
    {
        var semafor = _laase.GetOrAdd(forbindelseId, _ => new SemaphoreSlim(1, 1));
        await semafor.WaitAsync(ct);
        return new Frigiver(semafor);
    }

    private sealed class Frigiver(SemaphoreSlim semafor) : IDisposable
    {
        private int _frigivet;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _frigivet, 1) == 0) semafor.Release();
        }
    }
}

/// <summary>
/// Giver et gyldigt access token til en forbindelse. Tokenet gemmes og fornyes først, når der er kort tid tilbage
/// (TTLock-tokens holder i uger). Mislykkes fornyelsen, logger vi ind igen med de gemte oplysninger.
/// Kalderen har allerede sat tenant på databasekonteksten.
/// </summary>
public class TTLockTokenService(
    AppDbContext db,
    ITTLockKlient klient,
    IHemmelighedBeskytter beskytter,
    TokenLaase laase,
    TimeProvider tid)
{
    /// <summary>Fornyer, når der er mindre end så lang tid til tokenet udløber.</summary>
    public static readonly TimeSpan FornyFoerUdloeb = TimeSpan.FromDays(3);

    public async Task<TTLockSvar<string>> HentAccessTokenAsync(Guid forbindelseId, bool tvingNyt = false, CancellationToken ct = default)
    {
        using var _ = await laase.TagAsync(forbindelseId, ct);

        // Læs frisk inde i låsen, så en samtidig fornyelse er med.
        var f = await db.TTLockForbindelser.FirstOrDefaultAsync(x => x.Id == forbindelseId, ct);
        if (f is null) return TTLockSvar<string>.Fejlet(TTLockKoder.Teknisk, "Forbindelsen findes ikke.");
        await db.Entry(f).ReloadAsync(ct);

        var nu = tid.GetUtcNow().UtcDateTime;

        try
        {
            if (!tvingNyt && f.AccessTokenBeskyttet is not null && f.TokenUdloeberTid is { } udloeber && udloeber - nu > FornyFoerUdloeb)
                return TTLockSvar<string>.Succes(beskytter.FjernBeskyttelse(f.AccessTokenBeskyttet));

            if (f.RefreshTokenBeskyttet is not null)
            {
                var fornyet = await klient.FornyTokenAsync(beskytter.FjernBeskyttelse(f.RefreshTokenBeskyttet), ct);
                if (fornyet.Ok) return await GemAsync(f, fornyet.Data!, ct);
            }

            var login = await klient.HentTokenAsync(f.TTLockBrugernavn, beskytter.FjernBeskyttelse(f.AdgangskodeHashBeskyttet), ct);
            if (login.Ok) return await GemAsync(f, login.Data!, ct);

            f.SidsteFejl = Kort(login.Fejl);
            await db.SaveChangesAsync(ct);
            return TTLockSvar<string>.FejletMedTekst(login.ErrCode, login.Fejl);
        }
        catch (CryptographicException)
        {
            f.SidsteFejl = TTLockKoder.Tekst(TTLockKoder.OplysningerUlaeselige, null);
            await db.SaveChangesAsync(ct);
            return TTLockSvar<string>.Fejlet(TTLockKoder.OplysningerUlaeselige);
        }
    }

    private async Task<TTLockSvar<string>> GemAsync(TTLockForbindelse f, TTLockToken token, CancellationToken ct)
    {
        f.AccessTokenBeskyttet = beskytter.Beskyt(token.AccessToken);
        f.RefreshTokenBeskyttet = beskytter.Beskyt(token.RefreshToken);
        f.TokenUdloeberTid = token.UdloeberUtc;
        f.SidsteFejl = null;
        await db.SaveChangesAsync(ct);
        return TTLockSvar<string>.Succes(token.AccessToken);
    }

    private static string? Kort(string? tekst) => tekst is { Length: > 500 } ? tekst[..500] : tekst;
}
