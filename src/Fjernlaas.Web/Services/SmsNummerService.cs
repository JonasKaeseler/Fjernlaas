using System.Security.Cryptography;
using System.Text;
using Fjernlaas.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Services;

public enum SmsNummerFejl
{
    Ingen = 0,
    IkkeTilladt,
    NavnUgyldigt,
    NummerUgyldigt,
    OrganisationIkkeFundet,
    IkkeFundet
}

/// <summary>Token er kun med lige efter oprettelse eller udskiftning. Derefter findes kun hashet.</summary>
public sealed record SmsNummerResultat(SmsNummerFejl Fejl, SmsNummer? SmsNummer, string? Token)
{
    public bool Ok => Fejl == SmsNummerFejl.Ingen;
}

public sealed record SmsNummerOversigt(
    Guid Id, Guid OrganisationId, string OrganisationNavn, string Navn, string? Nummer,
    string TokenPraefiks, SmsNummerStatus Status, int AntalNoegleord);

/// <summary>Virtuelle SMS-numre og deres hemmelige webhook-token.</summary>
public class SmsNummerService(AppDbContext db)
{
    public const int MaxNavnLaengde = 200;
    public const int PraefiksLaengde = 6;

    public static string NyToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public async Task<IReadOnlyList<SmsNummerOversigt>> OversigtAsync(Aktoer aktoer, Guid? organisationId = null, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) throw new UnauthorizedAccessException();
        Tenant.Saet(db, aktoer);

        var foresporgsel = db.SmsNumre.AsNoTracking().AsQueryable();
        if (organisationId is { } org) foresporgsel = foresporgsel.Where(n => n.OrganisationId == org);

        return await (
            from n in foresporgsel
            join o in db.Organisationer on n.OrganisationId equals o.Id
            orderby o.Navn, n.Navn
            select new SmsNummerOversigt(
                n.Id, n.OrganisationId, o.Navn, n.Navn, n.Nummer, n.TokenPraefiks, n.Status,
                db.Noegleordene.Count(k => k.SmsNummerId == n.Id)))
            .ToListAsync(ct);
    }

    public async Task<SmsNummerResultat> OpretAsync(
        Aktoer aktoer, Guid organisationId, string? navn, string? nummer, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(SmsNummerFejl.IkkeTilladt);
        if (!aktoer.ErPlatformAdmin && aktoer.OrganisationId != organisationId) return Fejl(SmsNummerFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var rentNavn = (navn ?? string.Empty).Trim();
        if (rentNavn.Length is 0 or > MaxNavnLaengde) return Fejl(SmsNummerFejl.NavnUgyldigt);

        string? visNummer = null;
        if (!string.IsNullOrWhiteSpace(nummer))
        {
            visNummer = SmsParser.NormaliserNummer(nummer);
            if (visNummer is null) return Fejl(SmsNummerFejl.NummerUgyldigt);
        }

        if (!await db.Organisationer.AnyAsync(o => o.Id == organisationId, ct)) return Fejl(SmsNummerFejl.OrganisationIkkeFundet);

        var token = NyToken();
        var smsNummer = new SmsNummer
        {
            OrganisationId = organisationId,
            Navn = rentNavn,
            Nummer = visNummer,
            TokenHash = HashToken(token),
            TokenPraefiks = token[..PraefiksLaengde]
        };

        db.SmsNumre.Add(smsNummer);
        await db.SaveChangesAsync(ct);
        return new SmsNummerResultat(SmsNummerFejl.Ingen, smsNummer, token);
    }

    /// <summary>Udskifter token. Den gamle virker ikke længere, så webhook-adressen i InMobile skal rettes.</summary>
    public async Task<SmsNummerResultat> NytTokenAsync(Aktoer aktoer, Guid id, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(SmsNummerFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var smsNummer = await db.SmsNumre.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (smsNummer is null) return Fejl(SmsNummerFejl.IkkeFundet);

        var token = NyToken();
        smsNummer.TokenHash = HashToken(token);
        smsNummer.TokenPraefiks = token[..PraefiksLaengde];
        await db.SaveChangesAsync(ct);
        return new SmsNummerResultat(SmsNummerFejl.Ingen, smsNummer, token);
    }

    public async Task<SmsNummerResultat> SaetStatusAsync(Aktoer aktoer, Guid id, SmsNummerStatus status, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return Fejl(SmsNummerFejl.IkkeTilladt);
        Tenant.Saet(db, aktoer);

        var smsNummer = await db.SmsNumre.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (smsNummer is null) return Fejl(SmsNummerFejl.IkkeFundet);

        smsNummer.Status = status;
        await db.SaveChangesAsync(ct);
        return new SmsNummerResultat(SmsNummerFejl.Ingen, smsNummer, null);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static SmsNummerResultat Fejl(SmsNummerFejl fejl) => new(fejl, null, null);
}
