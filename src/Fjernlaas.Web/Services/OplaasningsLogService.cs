using Fjernlaas.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Services;

public sealed record LogFilter(Udfald? Udfald = null, Guid? SmsNummerId = null, DateTime? FraUtc = null, DateTime? TilUtc = null);

public sealed record LogRaekke(
    Guid Id, DateTime TidspunktUtc, string OrganisationNavn, string SmsNummerNavn, string Afsender,
    string Tekst, string? Noegleord, string? RessourceNavn, string? LokationNavn,
    Udfald Udfald, int? TTLockErrcode, string? Fejltekst);

public sealed record LogSide(IReadOnlyList<LogRaekke> Raekker, int Side, int SideStoerrelse, int Total)
{
    public int AntalSider => Math.Max(1, (int)Math.Ceiling(Total / (double)SideStoerrelse));
}

/// <summary>Læser oplåsningsloggen, afgrænset til aktørens organisation. Nyeste først.</summary>
public class OplaasningsLogService(AppDbContext db)
{
    public const int StandardSideStoerrelse = 50;
    public const int MaxSideStoerrelse = 200;

    public async Task<LogSide> HentAsync(
        Aktoer aktoer, LogFilter? filter = null, int side = 1, int sideStoerrelse = StandardSideStoerrelse, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) throw new UnauthorizedAccessException();
        Tenant.Saet(db, aktoer);

        filter ??= new LogFilter();
        side = Math.Max(1, side);
        sideStoerrelse = Math.Clamp(sideStoerrelse, 1, MaxSideStoerrelse);

        var f = db.Forsoeg.AsNoTracking().AsQueryable();
        if (filter.Udfald is { } u) f = f.Where(x => x.Udfald == u);
        if (filter.SmsNummerId is { } n) f = f.Where(x => x.SmsNummerId == n);
        if (filter.FraUtc is { } fra) f = f.Where(x => x.TidspunktUtc >= fra);
        if (filter.TilUtc is { } til) f = f.Where(x => x.TidspunktUtc < til);

        var total = await f.CountAsync(ct);

        var raekker = await (
            from x in f
            join o in db.Organisationer on x.OrganisationId equals o.Id
            join s in db.SmsNumre on x.SmsNummerId equals s.Id
            join r in db.Ressourcer on x.RessourceId equals r.Id into rr
            from r in rr.DefaultIfEmpty()
            orderby x.TidspunktUtc descending, x.Id descending
            select new LogRaekke(
                x.Id, x.TidspunktUtc, o.Navn, s.Navn, x.Afsender, x.Tekst, x.Noegleord,
                r == null ? null : r.Navn, r == null ? null : r.Lokation!.Navn,
                x.Udfald, x.TTLockErrcode, x.Fejltekst))
            .Skip((side - 1) * sideStoerrelse).Take(sideStoerrelse)
            .ToListAsync(ct);

        return new LogSide(raekker, side, sideStoerrelse, total);
    }

    public static string UdfaldTekst(Udfald u) => u switch
    {
        Udfald.Aabnet => "Åbnet",
        Udfald.AfvistUgyldigtFormat => "Afvist: ugyldigt format",
        Udfald.AfvistNummerDeaktiveret => "Afvist: SMS-nummer deaktiveret",
        Udfald.AfvistOrganisationDeaktiveret => "Afvist: organisation deaktiveret",
        Udfald.AfvistDublet => "Afvist: dublet",
        Udfald.AfvistForMangeForsoeg => "Afvist: for mange forsøg",
        Udfald.AfvistUkendtOrd => "Afvist: ukendt nøgleord",
        Udfald.AfvistLokationDeaktiveret => "Afvist: lokation deaktiveret",
        Udfald.AfvistRessourcePause => "Afvist: ressource på pause",
        Udfald.AfvistIkkeKonfigureret => "Afvist: lås ikke koblet",
        Udfald.AfvistForbindelseDeaktiveret => "Afvist: forbindelse deaktiveret",
        Udfald.TTLockFejl => "Fejl fra TTLock",
        _ => u.ToString()
    };
}
