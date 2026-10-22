using Microsoft.AspNetCore.Identity;

namespace Fjernlaas.Web.Services;

/// <summary>Danske fejlbeskeder fra Identity, så de kan vises direkte til brugeren.</summary>
public class DanskIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DuplicateEmail(string email) =>
        Fejl(nameof(DuplicateEmail), "Der findes allerede en bruger med den e-mail.");

    public override IdentityError DuplicateUserName(string userName) =>
        Fejl(nameof(DuplicateUserName), "Der findes allerede en bruger med den e-mail.");

    public override IdentityError InvalidEmail(string? email) =>
        Fejl(nameof(InvalidEmail), "E-mailadressen er ikke gyldig.");

    public override IdentityError InvalidUserName(string? userName) =>
        Fejl(nameof(InvalidUserName), "E-mailadressen er ikke gyldig.");

    public override IdentityError PasswordMismatch() =>
        Fejl(nameof(PasswordMismatch), "Den nuværende adgangskode er forkert.");

    public override IdentityError PasswordTooShort(int length) =>
        Fejl(nameof(PasswordTooShort), $"Adgangskoden skal være mindst {length} tegn.");

    public override IdentityError PasswordRequiresDigit() =>
        Fejl(nameof(PasswordRequiresDigit), "Adgangskoden skal indeholde mindst ét tal.");

    public override IdentityError PasswordRequiresLower() =>
        Fejl(nameof(PasswordRequiresLower), "Adgangskoden skal indeholde mindst ét lille bogstav.");

    public override IdentityError PasswordRequiresUpper() =>
        Fejl(nameof(PasswordRequiresUpper), "Adgangskoden skal indeholde mindst ét stort bogstav.");

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Fejl(nameof(PasswordRequiresNonAlphanumeric), "Adgangskoden skal indeholde mindst ét specialtegn.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Fejl(nameof(PasswordRequiresUniqueChars), $"Adgangskoden skal indeholde mindst {uniqueChars} forskellige tegn.");

    private static IdentityError Fejl(string kode, string beskrivelse) => new() { Code = kode, Description = beskrivelse };
}
