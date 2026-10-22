using System.Net.Mail;
using Fjernlaas.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Services;

public enum BrugerFejl
{
    Ingen = 0,
    IkkeTilladt,
    OrganisationIkkeFundet,
    NavnUgyldigt,
    EmailUgyldig,
    EmailFindesAllerede,
    RolleUgyldig,
    AdgangskodeAfvist,
    IkkeFundet,
    KanIkkeAendreSig
}

public sealed record BrugerResultat(BrugerFejl Fejl, ApplicationUser? Bruger, IReadOnlyList<string> Detaljer)
{
    public bool Ok => Fejl == BrugerFejl.Ingen;
}

public sealed record BrugerOversigt(
    Guid Id,
    string Navn,
    string Email,
    Guid? OrganisationId,
    string? OrganisationNavn,
    string Rolle,
    bool Aktiv,
    bool SkalSkifteAdgangskode,
    DateTime OprettetTid,
    DateTime? SidstLoggetInd);

/// <summary>
/// Brugerstyring. Reglerne i betaen: kun platformsadministratorer opretter og ændrer brugere.
/// Organisationsadministratorer kan se brugerne i deres egen organisation og intet andet.
/// Alle metoder får aktøren med og afviser det, aktøren ikke må.
/// </summary>
public class BrugerService(
    UserManager<ApplicationUser> brugere,
    RoleManager<IdentityRole<Guid>> roller,
    AppDbContext db)
{
    public const int MaxNavnLaengde = 200;

    private static readonly string[] Roller_ = [Data.Roller.PlatformAdmin, Data.Roller.OrganisationAdmin, Data.Roller.Bruger];

    /// <summary>
    /// Brugere, aktøren må se. Platformsadmin ser alle (eller en valgt organisation).
    /// Organisationsadmin ser kun sin egen organisation, og forsøg på mere afvises med en undtagelse.
    /// </summary>
    public async Task<IReadOnlyList<BrugerOversigt>> OversigtAsync(
        Aktoer aktoer, Guid? organisationId = null, CancellationToken ct = default)
    {
        Guid? afgraensning;
        if (aktoer.ErPlatformAdmin)
        {
            afgraensning = organisationId;
        }
        else if (aktoer.ErOrganisationAdmin && aktoer.OrganisationId is { } eget)
        {
            if (organisationId is not null && organisationId != eget)
                throw new UnauthorizedAccessException("Adgang til en anden organisations brugere er ikke tilladt.");
            afgraensning = eget;
        }
        else
        {
            throw new UnauthorizedAccessException("Kun administratorer må se brugerlisten.");
        }

        var foresporgsel = db.Users.AsNoTracking();
        if (afgraensning is { } valgt)
            foresporgsel = foresporgsel.Where(u => u.OrganisationId == valgt);

        var raekker = await (
            from u in foresporgsel
            join o in db.Organisationer on u.OrganisationId equals o.Id into organisationer
            from o in organisationer.DefaultIfEmpty()
            orderby u.Navn
            select new
            {
                u.Id,
                u.Navn,
                u.Email,
                u.OrganisationId,
                OrganisationNavn = o != null ? o.Navn : null,
                u.Aktiv,
                u.SkalSkifteAdgangskode,
                u.OprettetTid,
                u.SidstLoggetInd
            }).ToListAsync(ct);

        var ids = raekker.Select(r => r.Id).ToList();
        var rolleRaekker = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ids.Contains(ur.UserId)
            select new { ur.UserId, r.Name }).ToListAsync(ct);

        var rollerPrBruger = rolleRaekker
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => HoejesteRolle(g.Select(x => x.Name)));

        return raekker.Select(r => new BrugerOversigt(
            r.Id,
            r.Navn,
            r.Email ?? string.Empty,
            r.OrganisationId,
            r.OrganisationNavn,
            rollerPrBruger.GetValueOrDefault(r.Id, string.Empty),
            r.Aktiv,
            r.SkalSkifteAdgangskode,
            r.OprettetTid,
            r.SidstLoggetInd)).ToList();
    }

    public async Task<BrugerResultat> OpretAsync(
        Aktoer aktoer, Guid organisationId, string? navn, string? email, string rolle, string? midlertidigAdgangskode,
        CancellationToken ct = default)
    {
        if (!aktoer.ErPlatformAdmin) return Fejl(BrugerFejl.IkkeTilladt);

        if (rolle != Data.Roller.OrganisationAdmin && rolle != Data.Roller.Bruger)
            return Fejl(BrugerFejl.RolleUgyldig);

        var rentNavn = (navn ?? string.Empty).Trim();
        if (rentNavn.Length is 0 or > MaxNavnLaengde) return Fejl(BrugerFejl.NavnUgyldigt);

        var rentEmail = (email ?? string.Empty).Trim();
        if (!ErGyldigEmail(rentEmail)) return Fejl(BrugerFejl.EmailUgyldig);

        if (!await db.Organisationer.AnyAsync(o => o.Id == organisationId, ct))
            return Fejl(BrugerFejl.OrganisationIkkeFundet);

        if (await brugere.FindByEmailAsync(rentEmail) is not null)
            return Fejl(BrugerFejl.EmailFindesAllerede);

        var bruger = new ApplicationUser
        {
            UserName = rentEmail,
            Email = rentEmail,
            EmailConfirmed = true,
            Navn = rentNavn,
            OrganisationId = organisationId,
            SkalSkifteAdgangskode = true
        };

        var oprettet = await brugere.CreateAsync(bruger, midlertidigAdgangskode ?? string.Empty);
        if (!oprettet.Succeeded) return FraIdentity(oprettet);

        await SikrRolleAsync(rolle);
        var rolleResultat = await brugere.AddToRoleAsync(bruger, rolle);
        if (!rolleResultat.Succeeded)
        {
            // Ingen halvt oprettede brugere.
            await brugere.DeleteAsync(bruger);
            throw new InvalidOperationException(string.Join("; ", rolleResultat.Errors.Select(e => e.Description)));
        }

        return new BrugerResultat(BrugerFejl.Ingen, bruger, []);
    }

    public async Task<BrugerResultat> SaetAktivAsync(Aktoer aktoer, Guid brugerId, bool aktiv, CancellationToken ct = default)
    {
        if (!aktoer.ErPlatformAdmin) return Fejl(BrugerFejl.IkkeTilladt);
        if (!aktiv && aktoer.BrugerId == brugerId) return Fejl(BrugerFejl.KanIkkeAendreSig);

        var bruger = await brugere.FindByIdAsync(brugerId.ToString());
        if (bruger is null) return Fejl(BrugerFejl.IkkeFundet);

        if (bruger.Aktiv != aktiv)
        {
            bruger.Aktiv = aktiv;
            var gemt = await brugere.UpdateAsync(bruger);
            if (!gemt.Succeeded) return FraIdentity(gemt);

            // Gør eksisterende sessioner ugyldige, så en deaktivering virker med det samme.
            await brugere.UpdateSecurityStampAsync(bruger);
        }

        return new BrugerResultat(BrugerFejl.Ingen, bruger, []);
    }

    /// <summary>Sætter en ny midlertidig adgangskode, låser kontoen op og kræver skift ved næste login.</summary>
    public async Task<BrugerResultat> NulstilAdgangskodeAsync(
        Aktoer aktoer, Guid brugerId, string? midlertidigAdgangskode, CancellationToken ct = default)
    {
        if (!aktoer.ErPlatformAdmin) return Fejl(BrugerFejl.IkkeTilladt);

        var bruger = await brugere.FindByIdAsync(brugerId.ToString());
        if (bruger is null) return Fejl(BrugerFejl.IkkeFundet);

        var token = await brugere.GeneratePasswordResetTokenAsync(bruger);
        var nulstillet = await brugere.ResetPasswordAsync(bruger, token, midlertidigAdgangskode ?? string.Empty);
        if (!nulstillet.Succeeded) return FraIdentity(nulstillet);

        await brugere.ResetAccessFailedCountAsync(bruger);
        await brugere.SetLockoutEndDateAsync(bruger, null);

        bruger.SkalSkifteAdgangskode = true;
        var gemt = await brugere.UpdateAsync(bruger);
        if (!gemt.Succeeded) return FraIdentity(gemt);

        return new BrugerResultat(BrugerFejl.Ingen, bruger, []);
    }

    private async Task SikrRolleAsync(string rolle)
    {
        if (!await roller.RoleExistsAsync(rolle))
        {
            await roller.CreateAsync(new IdentityRole<Guid>(rolle) { Id = Guid.CreateVersion7() });
        }
    }

    private static string HoejesteRolle(IEnumerable<string?> navne)
    {
        var sat = navne.ToHashSet();
        return Roller_.FirstOrDefault(sat.Contains) ?? string.Empty;
    }

    private static bool ErGyldigEmail(string email)
    {
        if (email.Length is 0 or > 256) return false;
        return MailAddress.TryCreate(email, out var adresse) && adresse.Address == email;
    }

    private static BrugerResultat Fejl(BrugerFejl fejl) => new(fejl, null, []);

    private static BrugerResultat FraIdentity(IdentityResult resultat)
    {
        var detaljer = resultat.Errors.Select(e => e.Description).ToList();
        var koder = resultat.Errors.Select(e => e.Code).ToHashSet();

        if (koder.Contains(nameof(IdentityErrorDescriber.DuplicateEmail)) ||
            koder.Contains(nameof(IdentityErrorDescriber.DuplicateUserName)))
            return new BrugerResultat(BrugerFejl.EmailFindesAllerede, null, detaljer);

        if (koder.Contains(nameof(IdentityErrorDescriber.InvalidEmail)) ||
            koder.Contains(nameof(IdentityErrorDescriber.InvalidUserName)))
            return new BrugerResultat(BrugerFejl.EmailUgyldig, null, detaljer);

        return new BrugerResultat(BrugerFejl.AdgangskodeAfvist, null, detaljer);
    }
}
