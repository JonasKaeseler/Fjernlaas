using Fjernlaas.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Services;

/// <param name="TokenKendt">Falsk, hvis webhook-adressen ikke svarer til noget SMS-nummer (så svarer endpointet 404).</param>
/// <param name="Udfald">Hvad der skete. Tom, når token er ukendt.</param>
public sealed record SmsHaandtering(bool TokenKendt, Udfald? Udfald);

/// <param name="Udfald">Samme udfald som ved en SMS, så fejl kan forklares på samme måde.</param>
public sealed record TestResultat(bool IkkeTilladt, bool IkkeFundet, Udfald? Udfald, int? ErrCode, string? Fejltekst)
{
    public bool Aabnet => Udfald == Data.Udfald.Aabnet;
}

/// <summary>
/// Hele forløbet fra en indgående SMS til en åbnet dør. Kører som system, så tenant sættes til "alle":
/// organisationen findes ud fra webhook-adressen og ikke ud fra en bruger. Alle forsøg logges, også de afviste,
/// og kald fra InMobile får altid svar (ingen undtagelser ud), så de ikke gentages unødigt.
/// </summary>
public class OplaasningService(
    AppDbContext db,
    ITTLockKlient klient,
    TTLockTokenService tokens,
    TimeProvider tid,
    ILogger<OplaasningService> log)
{
    /// <summary>Flere forsøg end dette fra samme afsender inden for et minut afvises.</summary>
    public const int MaxForsoegPrMinut = 5;

    /// <summary>Samme tekst fra samme afsender inden for så få sekunder efter en åbning regnes for et gensendt kald.</summary>
    public const int DubletVinduSekunder = 10;

    public async Task<SmsHaandtering> HaandterAsync(string? token, string? raaBesked, CancellationToken ct = default)
    {
        db.SaetTenant(true, null);

        if (string.IsNullOrWhiteSpace(token)) return new SmsHaandtering(false, null);

        var hash = SmsNummerService.HashToken(token);
        var nummer = await db.SmsNumre.FirstOrDefaultAsync(n => n.TokenHash == hash, ct);
        if (nummer is null) return new SmsHaandtering(false, null);

        var nu = tid.GetUtcNow().UtcDateTime;
        var besked = SmsParser.Parse(raaBesked);

        async Task<SmsHaandtering> Afslut(
            Udfald udfald, Guid? ressourceId = null, string? noegleordTekst = null, int? errcode = null, string? fejltekst = null)
        {
            db.Forsoeg.Add(new Oplaasningsforsoeg
            {
                TidspunktUtc = nu,
                OrganisationId = nummer.OrganisationId,
                SmsNummerId = nummer.Id,
                RessourceId = ressourceId,
                Afsender = besked?.Afsender ?? "ukendt",
                Tekst = besked?.Tekst ?? Afkort(raaBesked),
                Noegleord = noegleordTekst ?? besked?.Noegleord,
                Udfald = udfald,
                TTLockErrcode = errcode,
                Fejltekst = string.IsNullOrEmpty(fejltekst) ? null : Afkort(fejltekst)
            });
            await db.SaveChangesAsync(ct);
            return new SmsHaandtering(true, udfald);
        }

        var organisation = await db.Organisationer.AsNoTracking().FirstOrDefaultAsync(o => o.Id == nummer.OrganisationId, ct);
        if (organisation is null || organisation.Status != OrganisationStatus.Aktiv) return await Afslut(Udfald.AfvistOrganisationDeaktiveret);
        if (nummer.Status != SmsNummerStatus.Aktiv) return await Afslut(Udfald.AfvistNummerDeaktiveret);
        if (besked is null) return await Afslut(Udfald.AfvistUgyldigtFormat);

        var dubletGraense = nu.AddSeconds(-DubletVinduSekunder);
        var dublet = await db.Forsoeg.AnyAsync(f =>
            f.SmsNummerId == nummer.Id && f.Afsender == besked.Afsender && f.Tekst == besked.Tekst
            && f.Udfald == Udfald.Aabnet && f.TidspunktUtc >= dubletGraense, ct);
        if (dublet) return await Afslut(Udfald.AfvistDublet);

        var minutGraense = nu.AddMinutes(-1);
        var seneste = await db.Forsoeg.CountAsync(f =>
            f.SmsNummerId == nummer.Id && f.Afsender == besked.Afsender && f.TidspunktUtc >= minutGraense, ct);
        if (seneste >= MaxForsoegPrMinut) return await Afslut(Udfald.AfvistForMangeForsoeg);

        var normaliseret = Normalisering.Normaliser(besked.Noegleord);
        var noegleord = await db.Noegleordene.FirstOrDefaultAsync(k =>
            k.SmsNummerId == nummer.Id && k.OrdNormaliseret == normaliseret && k.Status == NoegleordStatus.Aktiv, ct);
        if (noegleord is null) return await Afslut(Udfald.AfvistUkendtOrd);

        var ressource = await db.Ressourcer.Include(r => r.Lokation).FirstOrDefaultAsync(r => r.Id == noegleord.RessourceId, ct);
        if (ressource is null) return await Afslut(Udfald.AfvistUkendtOrd);

        if (ressource.Lokation is null || ressource.Lokation.Status != LokationStatus.Aktiv)
            return await Afslut(Udfald.AfvistLokationDeaktiveret, ressource.Id);
        if (ressource.Status != RessourceStatus.Aktiv)
            return await Afslut(Udfald.AfvistRessourcePause, ressource.Id);
        if (ressource.ForbindelseId is not { } forbindelseId || ressource.TTLockLockId is not { } lockId)
            return await Afslut(Udfald.AfvistIkkeKonfigureret, ressource.Id);

        var forbindelse = await db.TTLockForbindelser.AsNoTracking().FirstOrDefaultAsync(f => f.Id == forbindelseId, ct);
        if (forbindelse is null) return await Afslut(Udfald.AfvistIkkeKonfigureret, ressource.Id);
        if (forbindelse.Status != ForbindelseStatus.Aktiv) return await Afslut(Udfald.AfvistForbindelseDeaktiveret, ressource.Id);

        try
        {
            var token1 = await tokens.HentAccessTokenAsync(forbindelseId, false, ct);
            if (!token1.Ok) return await Afslut(Udfald.TTLockFejl, ressource.Id, null, token1.ErrCode, token1.Fejl);

            var svar = await klient.AabnAsync(token1.Data!, lockId, ct);

            // Et afvist token løses med ét nyt login og ét nyt forsøg.
            if (!svar.Ok && TTLockKoder.ErTokenFejl(svar.ErrCode))
            {
                var token2 = await tokens.HentAccessTokenAsync(forbindelseId, true, ct);
                if (!token2.Ok) return await Afslut(Udfald.TTLockFejl, ressource.Id, null, token2.ErrCode, token2.Fejl);
                svar = await klient.AabnAsync(token2.Data!, lockId, ct);
            }

            return svar.Ok
                ? await Afslut(Udfald.Aabnet, ressource.Id)
                : await Afslut(Udfald.TTLockFejl, ressource.Id, null, svar.ErrCode, svar.Fejl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Uventet fejl ved oplåsning af ressource {RessourceId}", ressource.Id);
            return await Afslut(Udfald.TTLockFejl, ressource.Id, null, TTLockKoder.Teknisk, "Uventet fejl ved oplåsning.");
        }
    }

    /// <summary>
    /// Admin-test: åbner ressourcens dør uden SMS, nøgleord og grænser. Samme regler for aktiv lokation, pause, kobling og
    /// forbindelse som ved en SMS. Døren åbnes rigtigt. Testen skrives ikke i oplåsningsloggen, fordi den ikke hører til et SMS-nummer.
    /// </summary>
    public async Task<TestResultat> TestAabnAsync(Aktoer aktoer, Guid ressourceId, CancellationToken ct = default)
    {
        if (!Tenant.KanAdministrere(aktoer)) return new TestResultat(true, false, null, null, null);
        Tenant.Saet(db, aktoer);

        var ressource = await db.Ressourcer.Include(r => r.Lokation).FirstOrDefaultAsync(r => r.Id == ressourceId, ct);
        if (ressource is null) return new TestResultat(false, true, null, null, null);

        if (ressource.Lokation is null || ressource.Lokation.Status != LokationStatus.Aktiv)
            return new TestResultat(false, false, Udfald.AfvistLokationDeaktiveret, null, null);
        if (ressource.Status != RessourceStatus.Aktiv)
            return new TestResultat(false, false, Udfald.AfvistRessourcePause, null, null);
        if (ressource.ForbindelseId is not { } forbindelseId || ressource.TTLockLockId is not { } lockId)
            return new TestResultat(false, false, Udfald.AfvistIkkeKonfigureret, null, null);

        var forbindelse = await db.TTLockForbindelser.AsNoTracking().FirstOrDefaultAsync(f => f.Id == forbindelseId, ct);
        if (forbindelse is null) return new TestResultat(false, false, Udfald.AfvistIkkeKonfigureret, null, null);
        if (forbindelse.Status != ForbindelseStatus.Aktiv)
            return new TestResultat(false, false, Udfald.AfvistForbindelseDeaktiveret, null, null);

        try
        {
            var token1 = await tokens.HentAccessTokenAsync(forbindelseId, false, ct);
            if (!token1.Ok) return new TestResultat(false, false, Udfald.TTLockFejl, token1.ErrCode, token1.Fejl);

            var svar = await klient.AabnAsync(token1.Data!, lockId, ct);
            if (!svar.Ok && TTLockKoder.ErTokenFejl(svar.ErrCode))
            {
                var token2 = await tokens.HentAccessTokenAsync(forbindelseId, true, ct);
                if (!token2.Ok) return new TestResultat(false, false, Udfald.TTLockFejl, token2.ErrCode, token2.Fejl);
                svar = await klient.AabnAsync(token2.Data!, lockId, ct);
            }

            return svar.Ok
                ? new TestResultat(false, false, Udfald.Aabnet, null, null)
                : new TestResultat(false, false, Udfald.TTLockFejl, svar.ErrCode, svar.Fejl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Uventet fejl ved testoplåsning af ressource {RessourceId}", ressource.Id);
            return new TestResultat(false, false, Udfald.TTLockFejl, TTLockKoder.Teknisk, "Uventet fejl ved oplåsning.");
        }
    }

    private static string Afkort(string? tekst) =>
        string.IsNullOrEmpty(tekst) ? string.Empty : tekst.Length > 500 ? tekst[..500] : tekst;
}
