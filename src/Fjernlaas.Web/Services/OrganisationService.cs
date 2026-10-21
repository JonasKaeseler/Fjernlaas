using Fjernlaas.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Services;

public enum OrganisationFejl
{
    Ingen = 0,
    NavnUgyldigt,
    NavnFindesAllerede,
    IkkeFundet
}

public sealed record OrganisationResultat(OrganisationFejl Fejl, Organisation? Organisation)
{
    public bool Ok => Fejl == OrganisationFejl.Ingen;
}

/// <summary>
/// Administration af organisationer (kun for platformsadministratorer).
/// Adgangskontrollen sidder i laget over servicen (UI/API), ikke her.
/// </summary>
public class OrganisationService(AppDbContext db)
{
    public const int MaxNavnLaengde = 200;

    public async Task<OrganisationResultat> OpretAsync(string? navn, CancellationToken ct = default)
    {
        var rent = (navn ?? string.Empty).Trim();
        if (!ErGyldigtNavn(rent)) return Fejl(OrganisationFejl.NavnUgyldigt);

        var normaliseret = Normalisering.Normaliser(rent);
        if (await db.Organisationer.AnyAsync(o => o.NavnNormaliseret == normaliseret, ct))
            return Fejl(OrganisationFejl.NavnFindesAllerede);

        var org = new Organisation { Navn = rent };
        db.Organisationer.Add(org);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ErDuplikat(ex))
        {
            // To oprettelser med samme navn på samme tid: det unikke indeks afgør.
            db.Entry(org).State = EntityState.Detached;
            return Fejl(OrganisationFejl.NavnFindesAllerede);
        }

        return new OrganisationResultat(OrganisationFejl.Ingen, org);
    }

    public async Task<IReadOnlyList<Organisation>> ListAsync(string? soeg = null, CancellationToken ct = default)
    {
        var foresporgsel = db.Organisationer.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(soeg))
        {
            var normaliseret = Normalisering.Normaliser(soeg);
            foresporgsel = foresporgsel.Where(o => o.NavnNormaliseret.Contains(normaliseret));
        }

        return await foresporgsel.OrderBy(o => o.Navn).ToListAsync(ct);
    }

    public Task<Organisation?> HentAsync(Guid id, CancellationToken ct = default) =>
        db.Organisationer.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);

    public async Task<OrganisationResultat> OmdoebAsync(Guid id, string? nytNavn, CancellationToken ct = default)
    {
        var rent = (nytNavn ?? string.Empty).Trim();
        if (!ErGyldigtNavn(rent)) return Fejl(OrganisationFejl.NavnUgyldigt);

        var org = await db.Organisationer.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return Fejl(OrganisationFejl.IkkeFundet);

        var normaliseret = Normalisering.Normaliser(rent);
        if (await db.Organisationer.AnyAsync(o => o.Id != id && o.NavnNormaliseret == normaliseret, ct))
            return Fejl(OrganisationFejl.NavnFindesAllerede);

        org.Navn = rent;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ErDuplikat(ex))
        {
            db.Entry(org).State = EntityState.Detached;
            return Fejl(OrganisationFejl.NavnFindesAllerede);
        }

        return new OrganisationResultat(OrganisationFejl.Ingen, org);
    }

    /// <summary>Deaktivér eller aktivér. Kan gentages uden effekt.</summary>
    public async Task<OrganisationResultat> SaetStatusAsync(Guid id, OrganisationStatus status, CancellationToken ct = default)
    {
        var org = await db.Organisationer.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return Fejl(OrganisationFejl.IkkeFundet);

        if (org.Status != status)
        {
            org.Status = status;
            await db.SaveChangesAsync(ct);
        }

        return new OrganisationResultat(OrganisationFejl.Ingen, org);
    }

    private static bool ErGyldigtNavn(string rent) => rent.Length is > 0 and <= MaxNavnLaengde;

    private static OrganisationResultat Fejl(OrganisationFejl fejl) => new(fejl, null);

    // 2601 og 2627: brud på unikt indeks / unik nøgle.
    private static bool ErDuplikat(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
