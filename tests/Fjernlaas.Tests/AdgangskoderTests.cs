using Fjernlaas.Web.Services;
using Xunit;

namespace Fjernlaas.Tests;

public class AdgangskoderTests
{
    [Fact]
    public void Ny_opfylder_kravene_hver_gang()
    {
        for (var i = 0; i < 300; i++)
        {
            var kode = Adgangskoder.Ny();

            Assert.True(kode.Length >= 10);
            Assert.Contains(kode, char.IsUpper);
            Assert.Contains(kode, char.IsLower);
            Assert.Contains(kode, char.IsDigit);
            Assert.Contains(kode, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public void Ny_giver_forskellige_koder()
    {
        var koder = Enumerable.Range(0, 200).Select(_ => Adgangskoder.Ny()).ToHashSet();

        Assert.Equal(200, koder.Count);
    }

    [Fact]
    public void Ny_afviser_for_kort_laengde()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Adgangskoder.Ny(8));
    }
}
