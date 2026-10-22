using Fjernlaas.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Services;

public enum RessourceFejl
{
    Ingen = 0,
    IkkeTilladt,
    NavnUgyldigt,
    NavnFindesAllerede,
    LokationIkkeFundet,
    ForbindelseIkkeFundet,
    KoblingUgyldig,
    IkkeFundet
}

public sealed record RessourceResultat(RessourceFejl Fejl, Ressource? Ressource)
{
    public bool Ok => Fejl == RessourceFejl.Ingen;
}

public sealed record RessourceOversigt(
    Guid Id, Guid LokationId, string LokationNavn, LokationStatus LokationStatus,
    Guid OrganisationId, string Navn, RessourceType Type, RessourceStatus Status,
    Guid? ForbindelseId, long? TTLockLockId);

/// <summary>
/// Ressourcer (døre). Samme regler som LokationService: aktøren afgrænser, hvad der kan ses og ændres.
/// En ressource arver sin organisation fra lokationen.
/// </summary>
public class RessourceService(AppDbContext db)
{
    public const int MaxNavnLaengde = 200;

    public async Task<IReadOnlyList<RessourceOversigt>> OversigtAsync(Aktoer aktoer, Guid? lokationId = null, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) throw new UnauthorizedAccessException();
        Tenant.Saet(db, aktoer);

        var foresporgsel = db.Ressourcer.AsNoTracking().AsQueryable();
        if (lokationId is { } lok) foresporgsel = foresporgsel.Where(r => r.LokationId == lok);

        return await (
            from r in foresporgsel
            join l in db.Lokationer on r.LokationId equals l.Id
            orderby l.Navn, r.Navn
            select new RessourceOversigt(r.Id, l.Id, l.Navn, l.Status, r.OrganisationId, r.Navn, r.Type, r.Status, r.ForbindelseId, r.TTLockLockId))
            .ToListAsync(ct);
    }

    public async Task<RessourceResultat> OpretAsync(Aktoer aktoer, Guid lokationId, string? navn, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(RessourceFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var rent = (navn ?? string.Empty).Trim();
        if (rent.Length is 0 or > MaxNavnLaengde) return Fejl(RessourceFejl.NavnUgyldigt);

        var lokation = await db.Lokationer.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lokationId, ct);
        if (lokation is null) return Fejl(RessourceFejl.LokationIkkeFundet);

        var ressource = new Ressource { OrganisationId = lokation.OrganisationId, LokationId = lokation.Id, Navn = rent };

        if (await db.Ressourcer.AnyAsync(r => r.LokationId == lokationId && r.NavnNormaliseret == ressource.NavnNormaliseret, ct))
            return Fejl(RessourceFejl.NavnFindesAllerede);

        db.Ressourcer.Add(ressource);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ErDuplikat(ex))
        {
            db.Entry(ressource).State = EntityState.Detached;
            return Fejl(RessourceFejl.NavnFindesAllerede);
        }

        return new RessourceResultat(RessourceFejl.Ingen, ressource);
    }

    public async Task<RessourceResultat> OmdoebAsync(Aktoer aktoer, Guid id, string? navn, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(RessourceFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var rent = (navn ?? string.Empty).Trim();
        if (rent.Length is 0 or > MaxNavnLaengde) return Fejl(RessourceFejl.NavnUgyldigt);

        var ressource = await db.Ressourcer.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ressource is null) return Fejl(RessourceFejl.IkkeFundet);

        ressource.Navn = rent;

        if (await db.Ressourcer.AnyAsync(r => r.LokationId == ressource.LokationId
                                              && r.Id != id && r.NavnNormaliseret == ressource.NavnNormaliseret, ct))
        {
            db.Entry(ressource).State = EntityState.Detached;
            return Fejl(RessourceFejl.NavnFindesAllerede);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ErDuplikat(ex))
        {
            db.Entry(ressource).State = EntityState.Detached;
            return Fejl(RessourceFejl.NavnFindesAllerede);
        }

        return new RessourceResultat(RessourceFejl.Ingen, ressource);
    }

    /// <summary>Nødstop: Pause spærrer ressourcen, Aktiv åbner for den igen.</summary>
    public async Task<RessourceResultat> SaetStatusAsync(Aktoer aktoer, Guid id, RessourceStatus status, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(RessourceFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var ressource = await db.Ressourcer.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ressource is null) return Fejl(RessourceFejl.IkkeFundet);

        ressource.Status = status;
        await db.SaveChangesAsync(ct);
        return new RessourceResultat(RessourceFejl.Ingen, ressource);
    }

    /// <summary>
    /// Kobler ressourcen til en lås hos TTLock, eller fjerner koblingen (begge tomme).
    /// Forbindelsen skal høre til samme organisation som ressourcen.
    /// </summary>
    public async Task<RessourceResultat> KoblAsync(Aktoer aktoer, Guid id, Guid? forbindelseId, long? lockId, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(RessourceFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        // Enten begge eller ingen af dem.
        if ((forbindelseId is null) != (lockId is null) || lockId is <= 0) return Fejl(RessourceFejl.KoblingUgyldig);

        var ressource = await db.Ressourcer.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ressource is null) return Fejl(RessourceFejl.IkkeFundet);

        if (forbindelseId is { } f)
        {
            var forbindelse = await db.TTLockForbindelser.AsNoTracking().FirstOrDefaultAsync(x => x.Id == f, ct);
            if (forbindelse is null || forbindelse.OrganisationId != ressource.OrganisationId)
                return Fejl(RessourceFejl.ForbindelseIkkeFundet);
        }

        ressource.ForbindelseId = forbindelseId;
        ressource.TTLockLockId = lockId;
        await db.SaveChangesAsync(ct);
        return new RessourceResultat(RessourceFejl.Ingen, ressource);
    }

    private static RessourceResultat Fejl(RessourceFejl fejl) => new(fejl, null);

    private static bool ErDuplikat(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
