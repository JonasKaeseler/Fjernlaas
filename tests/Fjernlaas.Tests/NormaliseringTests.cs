using Fjernlaas.Web.Data;
using Xunit;

namespace Fjernlaas.Tests;

public class NormaliseringTests
{
    [Theory]
    [InlineData("roshage", "ROSHAGE")]
    [InlineData("  Roshage ", "ROSHAGE")]
    [InlineData("Åben Butik", "ÅBEN BUTIK")]
    [InlineData(null, "")]
    public void Normaliser_trimmer_og_bruger_store_bogstaver(string? input, string forventet)
    {
        Assert.Equal(forventet, Normalisering.Normaliser(input));
    }

    [Fact]
    public void Organisation_saetter_normaliseret_navn_naar_navn_aendres()
    {
        var org = new Organisation { Navn = "  Lukas Butik " };

        Assert.Equal("Lukas Butik", org.Navn);
        Assert.Equal("LUKAS BUTIK", org.NavnNormaliseret);
    }

    [Fact]
    public void Ny_organisation_er_aktiv_med_utc_tid_og_id()
    {
        var org = new Organisation { Navn = "Test" };

        Assert.Equal(OrganisationStatus.Aktiv, org.Status);
        Assert.NotEqual(Guid.Empty, org.Id);
        Assert.Equal(DateTimeKind.Utc, org.OprettetTid.Kind);
    }
}
