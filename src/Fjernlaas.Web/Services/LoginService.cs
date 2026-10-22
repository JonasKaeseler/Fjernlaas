using System.Security.Claims;
using Fjernlaas.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Fjernlaas.Web.Services;

public enum LoginResultat
{
    Ok,
    Forkert,
    Laast
}

public sealed record SkiftAdgangskodeResultat(bool Ok, IReadOnlyList<string> Fejl);

/// <summary>Login og logout. Fejlbeskeder afslører ikke, om en e-mail findes eller er deaktiveret.</summary>
public class LoginService(AppSignInManager signIn, UserManager<ApplicationUser> brugere)
{
    public async Task<LoginResultat> LogIndAsync(string? email, string? adgangskode, bool huskMig)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(adgangskode))
            return LoginResultat.Forkert;

        var bruger = await brugere.FindByEmailAsync(email.Trim());
        if (bruger is null) return LoginResultat.Forkert;

        var resultat = await signIn.PasswordSignInAsync(bruger, adgangskode, huskMig, lockoutOnFailure: true);

        if (resultat.Succeeded)
        {
            bruger.SidstLoggetInd = DateTime.UtcNow;
            await brugere.UpdateAsync(bruger);
            return LoginResultat.Ok;
        }

        return resultat.IsLockedOut ? LoginResultat.Laast : LoginResultat.Forkert;
    }

    /// <summary>
    /// Brugeren vælger selv en ny adgangskode (første login med midlertidig kode, eller frivilligt).
    /// Sessionen fornyes, så den nye kode og det fjernede skift-krav slår igennem med det samme.
    /// </summary>
    public async Task<SkiftAdgangskodeResultat> SkiftAdgangskodeAsync(
        ClaimsPrincipal principal, string? nuvaerende, string? ny, string? gentag)
    {
        var bruger = await brugere.GetUserAsync(principal);
        if (bruger is null) return new SkiftAdgangskodeResultat(false, ["Du er ikke logget ind."]);

        if (string.IsNullOrEmpty(nuvaerende) || string.IsNullOrEmpty(ny))
            return new SkiftAdgangskodeResultat(false, ["Udfyld både nuværende og ny adgangskode."]);

        if (ny != gentag)
            return new SkiftAdgangskodeResultat(false, ["De to nye adgangskoder er ikke ens."]);

        if (ny == nuvaerende)
            return new SkiftAdgangskodeResultat(false, ["Den nye adgangskode skal være en anden end den nuværende."]);

        var resultat = await brugere.ChangePasswordAsync(bruger, nuvaerende, ny);
        if (!resultat.Succeeded)
            return new SkiftAdgangskodeResultat(false, resultat.Errors.Select(e => e.Description).ToList());

        bruger.SkalSkifteAdgangskode = false;
        await brugere.UpdateAsync(bruger);
        await signIn.RefreshSignInAsync(bruger);

        return new SkiftAdgangskodeResultat(true, []);
    }

    public Task LogUdAsync() => signIn.SignOutAsync();
}
