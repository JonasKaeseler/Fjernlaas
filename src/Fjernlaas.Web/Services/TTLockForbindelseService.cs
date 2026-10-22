using Fjernlaas.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fjernlaas.Web.Services;

public enum ForbindelseFejl
{
    Ingen = 0,
    IkkeTilladt,
    IkkeKonfigureret,
    NavnUgyldigt,
    BrugernavnUgyldigt,
    AdgangskodeMangler,
    OrganisationIkkeFundet,
    IkkeFundet,
    TTLockAfviste
}

public sealed record ForbindelseResultat(ForbindelseFejl Fejl, TTLockForbindelse? Forbindelse, string? Detalje = null)
{
    public bool Ok => Fejl == ForbindelseFejl.Ingen;
}

public sealed record ForbindelseOversigt(
    Guid Id, Guid OrganisationId, string OrganisationNavn, string Navn, string Brugernavn,
    ForbindelseStatus Status, string? SidsteFejl, DateTime? TokenUdloeberTid);

public sealed record LaaseResultat(ForbindelseFejl Fejl, IReadOnlyList<TTLockLaas> Laase, string? Detalje = null)
{
    public bool Ok => Fejl == ForbindelseFejl.Ingen;
}

/// <summary>Administration af organisationers TTLock-forbindelser.</summary>
public class TTLockForbindelseService(
    AppDbContext db,
    ITTLockKlient klient,
    TTLockTokenService tokens,
    IHemmelighedBeskytter beskytter,
    IOptions<TTLockOptions> options)
{
    public const int MaxNavnLaengde = 200;
    public const int MaxBrugernavnLaengde = 256;

    public async Task<IReadOnlyList<ForbindelseOversigt>> OversigtAsync(Aktoer aktoer, Guid? organisationId = null, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) throw new UnauthorizedAccessException();
        Tenant.Saet(db, aktoer);

        var foresporgsel = db.TTLockForbindelser.AsNoTracking().AsQueryable();
        if (organisationId is { } org) foresporgsel = foresporgsel.Where(f => f.OrganisationId == org);

        return await (
            from f in foresporgsel
            join o in db.Organisationer on f.OrganisationId equals o.Id
            orderby o.Navn, f.Navn
            select new ForbindelseOversigt(
                f.Id, f.OrganisationId, o.Navn, f.Navn, f.TTLockBrugernavn, f.Status, f.SidsteFejl, f.TokenUdloeberTid))
            .ToListAsync(ct);
    }

    /// <summary>Opretter forbindelsen, når TTLock har godkendt brugernavn og adgangskode. Adgangskoden gemmes kun som krypteret hash.</summary>
    public async Task<ForbindelseResultat> OpretAsync(
        Aktoer aktoer, Guid organisationId, string? navn, string? brugernavn, string? adgangskode, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(ForbindelseFejl.IkkeTilladt);
        if (!aktoer.ErPlatformAdmin && aktoer.OrganisationId != organisationId) return Fejl(ForbindelseFejl.IkkeTilladt);
        if (!options.Value.ErSat) return Fejl(ForbindelseFejl.IkkeKonfigureret);
        Tenant.Saet(db, aktoer);

        var rentNavn = (navn ?? string.Empty).Trim();
        if (rentNavn.Length is 0 or > MaxNavnLaengde) return Fejl(ForbindelseFejl.NavnUgyldigt);

        var rentBrugernavn = (brugernavn ?? string.Empty).Trim();
        if (rentBrugernavn.Length is 0 or > MaxBrugernavnLaengde) return Fejl(ForbindelseFejl.BrugernavnUgyldigt);

        if (string.IsNullOrEmpty(adgangskode)) return Fejl(ForbindelseFejl.AdgangskodeMangler);

        if (!await db.Organisationer.AnyAsync(o => o.Id == organisationId, ct)) return Fejl(ForbindelseFejl.OrganisationIkkeFundet);

        var hash = TTLockKlient.Md5Hex(adgangskode);
        var token = await klient.HentTokenAsync(rentBrugernavn, hash, ct);
        if (!token.Ok) return new ForbindelseResultat(ForbindelseFejl.TTLockAfviste, null, token.Fejl);

        var forbindelse = new TTLockForbindelse
        {
            OrganisationId = organisationId,
            Navn = rentNavn,
            TTLockBrugernavn = rentBrugernavn,
            AdgangskodeHashBeskyttet = beskytter.Beskyt(hash),
            AccessTokenBeskyttet = beskytter.Beskyt(token.Data!.AccessToken),
            RefreshTokenBeskyttet = beskytter.Beskyt(token.Data.RefreshToken),
            TokenUdloeberTid = token.Data.UdloeberUtc
        };

        db.TTLockForbindelser.Add(forbindelse);
        await db.SaveChangesAsync(ct);
        return new ForbindelseResultat(ForbindelseFejl.Ingen, forbindelse);
    }

    /// <summary>Henter låsene på kontoen og tester dermed forbindelsen.</summary>
    public async Task<LaaseResultat> LaaseAsync(Aktoer aktoer, Guid forbindelseId, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return new LaaseResultat(ForbindelseFejl.IkkeTilladt, []);
        if (!options.Value.ErSat) return new LaaseResultat(ForbindelseFejl.IkkeKonfigureret, []);
        Tenant.Saet(db, aktoer);

        var findes = await db.TTLockForbindelser.AnyAsync(f => f.Id == forbindelseId, ct);
        if (!findes) return new LaaseResultat(ForbindelseFejl.IkkeFundet, []);

        var token = await tokens.HentAccessTokenAsync(forbindelseId, false, ct);
        if (!token.Ok) return new LaaseResultat(ForbindelseFejl.TTLockAfviste, [], token.Fejl);

        var laase = await klient.ListLaaseAsync(token.Data!, ct);

        // Et afvist token midt i forløbet løses med ét nyt login.
        if (!laase.Ok && TTLockKoder.ErTokenFejl(laase.ErrCode))
        {
            token = await tokens.HentAccessTokenAsync(forbindelseId, true, ct);
            if (!token.Ok) return new LaaseResultat(ForbindelseFejl.TTLockAfviste, [], token.Fejl);
            laase = await klient.ListLaaseAsync(token.Data!, ct);
        }

        return laase.Ok
            ? new LaaseResultat(ForbindelseFejl.Ingen, laase.Data!)
            : new LaaseResultat(ForbindelseFejl.TTLockAfviste, [], laase.Fejl);
    }

    public async Task<ForbindelseResultat> SaetStatusAsync(Aktoer aktoer, Guid id, ForbindelseStatus status, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(ForbindelseFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var forbindelse = await db.TTLockForbindelser.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (forbindelse is null) return Fejl(ForbindelseFejl.IkkeFundet);

        forbindelse.Status = status;
        await db.SaveChangesAsync(ct);
        return new ForbindelseResultat(ForbindelseFejl.Ingen, forbindelse);
    }

    private static ForbindelseResultat Fejl(ForbindelseFejl fejl) => new(fejl, null);
}
