using Fjernlaas.Web.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fjernlaas.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class DatabaseTests(SqlServerFixture fixture)
{
    private static string Unik() => Guid.NewGuid().ToString("N");

    private static ApplicationUser NyBruger(Guid organisationId, string email, string brugernavn) => new()
    {
        UserName = brugernavn,
        NormalizedUserName = Normalisering.Normaliser(brugernavn),
        Email = email,
        NormalizedEmail = Normalisering.Normaliser(email),
        Navn = "Testbruger",
        OrganisationId = organisationId
    };

    [Fact]
    public async Task Organisation_kan_gemmes_og_laeses()
    {
        await using var db = fixture.CreateContext();
        var org = new Organisation { Navn = $"Butik {Unik()}" };
        db.Organisationer.Add(org);
        await db.SaveChangesAsync();

        await using var laes = fixture.CreateContext();
        var gemt = await laes.Organisationer.SingleAsync(o => o.Id == org.Id);

        Assert.Equal(org.Navn, gemt.Navn);
        Assert.Equal(OrganisationStatus.Aktiv, gemt.Status);
    }

    [Fact]
    public async Task Organisationsnavn_er_unikt_uanset_store_og_smaa_bogstaver()
    {
        var suffix = Unik();
        await using var db = fixture.CreateContext();
        db.Organisationer.Add(new Organisation { Navn = $"Lukas Butik {suffix}" });
        await db.SaveChangesAsync();

        db.Organisationer.Add(new Organisation { Navn = $"lukas butik {suffix}" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Email_er_unik_uanset_store_og_smaa_bogstaver()
    {
        var suffix = Unik();
        await using var db = fixture.CreateContext();
        var org = new Organisation { Navn = $"Org {suffix}" };
        db.Organisationer.Add(org);
        db.Users.Add(NyBruger(org.Id, $"Anna{suffix}@eksempel.dk", $"anna1-{suffix}"));
        await db.SaveChangesAsync();

        db.Users.Add(NyBruger(org.Id, $"anna{suffix}@EKSEMPEL.dk", $"anna2-{suffix}"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Organisation_med_brugere_kan_ikke_slettes()
    {
        var suffix = Unik();
        await using var db = fixture.CreateContext();
        var org = new Organisation { Navn = $"Org {suffix}" };
        db.Organisationer.Add(org);
        db.Users.Add(NyBruger(org.Id, $"bo{suffix}@eksempel.dk", $"bo-{suffix}"));
        await db.SaveChangesAsync();

        // Ny kontekst, så EF ikke kender brugerne og databasens Restrict-regel afgør udfaldet.
        await using var slet = fixture.CreateContext();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            slet.Organisationer.Where(o => o.Id == org.Id).ExecuteDeleteAsync());
    }

    [Fact]
    public async Task Bruger_kan_oprettes_uden_organisation_som_platformsadmin()
    {
        var suffix = Unik();
        await using var db = fixture.CreateContext();
        var bruger = new ApplicationUser
        {
            UserName = $"admin-{suffix}",
            NormalizedUserName = Normalisering.Normaliser($"admin-{suffix}"),
            Email = $"admin{suffix}@eksempel.dk",
            NormalizedEmail = Normalisering.Normaliser($"admin{suffix}@eksempel.dk"),
            Navn = "Platformsadmin"
        };
        db.Users.Add(bruger);

        await db.SaveChangesAsync();

        await using var laes = fixture.CreateContext();
        var gemt = await laes.Users.SingleAsync(u => u.Id == bruger.Id);
        Assert.Null(gemt.OrganisationId);
    }
}
