using Fjernlaas.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Fjernlaas.Web.Services;

public enum LoginResultat
{
    Ok,
    Forkert,
    Laast
}

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

    public Task LogUdAsync() => signIn.SignOutAsync();
}
