using Fjernlaas.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Services;

public enum LokationFejl
{
    Ingen = 0,
    IkkeTilladt,
    NavnUgyldigt,
    NavnFindesAllerede,
    TidszoneUgyldig,
    AdresseUgyldig,
    OrganisationIkkeFundet,
    IkkeFundet
}

public sealed record LokationResultat(LokationFejl Fejl, Lokation? Lokation)
{
    public bool Ok => Fejl == LokationFejl.Ingen;
}

public sealed record LokationOversigt(
    Guid Id, Guid OrganisationId, string OrganisationNavn, string Navn, string? Adresse,
    string Tidszone, LokationStatus Status, int AntalRessourcer);

/// <summary>
/// Lokationer. Alle kald får en aktør med, og konteksten afgrænses til aktørens organisation
/// (platformsadmin ser alle). Slår man en lokation op i en anden organisation, er svaret "ikke fundet".
/// </summary>
public class LokationService(AppDbContext db)
{
    public const int MaxNavnLaengde = 200;
    public const int MaxAdresseLaengde = 300;

    /// <summary>De tidszoner, man kan vælge i beta. Listen er bevidst kort og fast, så vi ikke afhænger af tidszonedata på serveren.</summary>
    public static readonly IReadOnlyList<string> Tidszoner =
    [
        "Europe/Copenhagen", "Europe/Oslo", "Europe/Stockholm", "Europe/Berlin", "Europe/London",
        "Atlantic/Faroe", "America/Godthab", "UTC"
    ];

    public async Task<IReadOnlyList<LokationOversigt>> OversigtAsync(Aktoer aktoer, Guid? organisationId = null, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) throw new UnauthorizedAccessException();
        Tenant.Saet(db, aktoer);

        var foresporgsel = db.Lokationer.AsNoTracking().AsQueryable();
        if (organisationId is { } org) foresporgsel = foresporgsel.Where(l => l.OrganisationId == org);

        return await (
            from l in foresporgsel
            join o in db.Organisationer on l.OrganisationId equals o.Id
            orderby o.Navn, l.Navn
            select new LokationOversigt(
                l.Id, l.OrganisationId, o.Navn, l.Navn, l.Adresse, l.Tidszone, l.Status,
                l.Ressourcer.Count()))
            .ToListAsync(ct);
    }

    public async Task<Lokation?> HentAsync(Aktoer aktoer, Guid id, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) throw new UnauthorizedAccessException();
        Tenant.Saet(db, aktoer);
        return await db.Lokationer.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task<LokationResultat> OpretAsync(
        Aktoer aktoer, Guid organisationId, string? navn, string? adresse, string? tidszone, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(LokationFejl.IkkeTilladt);
        // En organisationsadmin må kun oprette i sin egen organisation.
        if (!aktoer.ErPlatformAdmin && aktoer.OrganisationId != organisationId) return Fejl(LokationFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var validering = Valider(navn, adresse, tidszone);
        if (validering != LokationFejl.Ingen) return Fejl(validering);

        var organisationFindes = await db.Organisationer.AnyAsync(o => o.Id == organisationId, ct);
        if (!organisationFindes) return Fejl(LokationFejl.OrganisationIkkeFundet);

        var lokation = new Lokation
        {
            OrganisationId = organisationId,
            Navn = navn!,
            Adresse = RentEllerNull(adresse),
            Tidszone = tidszone!.Trim()
        };

        if (await db.Lokationer.AnyAsync(l => l.OrganisationId == organisationId && l.NavnNormaliseret == lokation.NavnNormaliseret, ct))
            return Fejl(LokationFejl.NavnFindesAllerede);

        db.Lokationer.Add(lokation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ErDuplikat(ex))
        {
            db.Entry(lokation).State = EntityState.Detached;
            return Fejl(LokationFejl.NavnFindesAllerede);
        }

        return new LokationResultat(LokationFejl.Ingen, lokation);
    }

    public async Task<LokationResultat> RedigerAsync(
        Aktoer aktoer, Guid id, string? navn, string? adresse, string? tidszone, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(LokationFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var validering = Valider(navn, adresse, tidszone);
        if (validering != LokationFejl.Ingen) return Fejl(validering);

        var lokation = await db.Lokationer.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lokation is null) return Fejl(LokationFejl.IkkeFundet);

        lokation.Navn = navn!;
        lokation.Adresse = RentEllerNull(adresse);
        lokation.Tidszone = tidszone!.Trim();

        if (await db.Lokationer.AnyAsync(l => l.OrganisationId == lokation.OrganisationId
                                              && l.Id != id && l.NavnNormaliseret == lokation.NavnNormaliseret, ct))
        {
            db.Entry(lokation).State = EntityState.Detached;
            return Fejl(LokationFejl.NavnFindesAllerede);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ErDuplikat(ex))
        {
            db.Entry(lokation).State = EntityState.Detached;
            return Fejl(LokationFejl.NavnFindesAllerede);
        }

        return new LokationResultat(LokationFejl.Ingen, lokation);
    }

    /// <summary>Deaktiverer eller genaktiverer en lokation. Ressourcerne ændres ikke, men kan ikke åbnes, mens lokationen er deaktiveret.</summary>
    public async Task<LokationResultat> SaetStatusAsync(Aktoer aktoer, Guid id, LokationStatus status, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(LokationFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var lokation = await db.Lokationer.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lokation is null) return Fejl(LokationFejl.IkkeFundet);

        lokation.Status = status;
        await db.SaveChangesAsync(ct);
        return new LokationResultat(LokationFejl.Ingen, lokation);
    }

    private static LokationFejl Valider(string? navn, string? adresse, string? tidszone)
    {
        var rentNavn = (navn ?? string.Empty).Trim();
        if (rentNavn.Length is 0 or > MaxNavnLaengde) return LokationFejl.NavnUgyldigt;
        if ((adresse ?? string.Empty).Trim().Length > MaxAdresseLaengde) return LokationFejl.AdresseUgyldig;
        if (tidszone is null || !Tidszoner.Contains(tidszone.Trim())) return LokationFejl.TidszoneUgyldig;
        return LokationFejl.Ingen;
    }

    private static string? RentEllerNull(string? tekst)
    {
        var rent = (tekst ?? string.Empty).Trim();
        return rent.Length == 0 ? null : rent;
    }

    private static LokationResultat Fejl(LokationFejl fejl) => new(fejl, null);

    private static bool ErDuplikat(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
