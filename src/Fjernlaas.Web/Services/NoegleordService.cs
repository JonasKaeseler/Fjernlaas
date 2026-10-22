using Fjernlaas.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Services;

public enum NoegleordFejl
{
    Ingen = 0,
    IkkeTilladt,
    OrdUgyldigt,
    OrdFindesAllerede,
    SmsNummerIkkeFundet,
    RessourceIkkeFundet,
    IkkeFundet
}

public sealed record NoegleordResultat(NoegleordFejl Fejl, Noegleord? Noegleord)
{
    public bool Ok => Fejl == NoegleordFejl.Ingen;
}

public sealed record NoegleordOversigt(
    Guid Id, Guid SmsNummerId, string SmsNummerNavn, string Ord, Guid RessourceId,
    string RessourceNavn, string LokationNavn, NoegleordStatus Status);

/// <summary>Nøgleord: det ord i en SMS, der åbner en bestemt ressource. Unikt pr. SMS-nummer.</summary>
public class NoegleordService(AppDbContext db)
{
    public const int MaxOrdLaengde = 100;

    public async Task<IReadOnlyList<NoegleordOversigt>> OversigtAsync(Aktoer aktoer, Guid? smsNummerId = null, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) throw new UnauthorizedAccessException();
        Tenant.Saet(db, aktoer);

        var foresporgsel = db.Noegleordene.AsNoTracking().AsQueryable();
        if (smsNummerId is { } nr) foresporgsel = foresporgsel.Where(k => k.SmsNummerId == nr);

        return await (
            from k in foresporgsel
            join n in db.SmsNumre on k.SmsNummerId equals n.Id
            join r in db.Ressourcer on k.RessourceId equals r.Id
            join l in db.Lokationer on r.LokationId equals l.Id
            orderby n.Navn, k.OrdNormaliseret
            select new NoegleordOversigt(k.Id, n.Id, n.Navn, k.Ord, r.Id, r.Navn, l.Navn, k.Status))
            .ToListAsync(ct);
    }

    public async Task<NoegleordResultat> OpretAsync(
        Aktoer aktoer, Guid smsNummerId, Guid ressourceId, string? ord, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(NoegleordFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        // Et nøgleord er ét ord, uden mellemrum og semikolon, så det kan findes som første ord i en SMS.
        var rent = (ord ?? string.Empty).Trim();
        if (rent.Length is 0 or > MaxOrdLaengde || rent.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == ';'))
            return Fejl(NoegleordFejl.OrdUgyldigt);

        var smsNummer = await db.SmsNumre.AsNoTracking().FirstOrDefaultAsync(n => n.Id == smsNummerId, ct);
        if (smsNummer is null) return Fejl(NoegleordFejl.SmsNummerIkkeFundet);

        var ressource = await db.Ressourcer.AsNoTracking().FirstOrDefaultAsync(r => r.Id == ressourceId, ct);
        if (ressource is null || ressource.OrganisationId != smsNummer.OrganisationId) return Fejl(NoegleordFejl.RessourceIkkeFundet);

        var noegleord = new Noegleord
        {
            OrganisationId = smsNummer.OrganisationId,
            SmsNummerId = smsNummer.Id,
            RessourceId = ressource.Id,
            Ord = rent
        };

        if (await db.Noegleordene.AnyAsync(k => k.SmsNummerId == smsNummerId && k.OrdNormaliseret == noegleord.OrdNormaliseret, ct))
            return Fejl(NoegleordFejl.OrdFindesAllerede);

        db.Noegleordene.Add(noegleord);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(noegleord).State = EntityState.Detached;
            return Fejl(NoegleordFejl.OrdFindesAllerede);
        }

        return new NoegleordResultat(NoegleordFejl.Ingen, noegleord);
    }

    public async Task<NoegleordResultat> SaetStatusAsync(Aktoer aktoer, Guid id, NoegleordStatus status, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(NoegleordFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var noegleord = await db.Noegleordene.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (noegleord is null) return Fejl(NoegleordFejl.IkkeFundet);

        noegleord.Status = status;
        await db.SaveChangesAsync(ct);
        return new NoegleordResultat(NoegleordFejl.Ingen, noegleord);
    }

    private static NoegleordResultat Fejl(NoegleordFejl fejl) => new(fejl, null);
}
