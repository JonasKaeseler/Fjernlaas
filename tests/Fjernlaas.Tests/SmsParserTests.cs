using Fjernlaas.Web.Services;
using Xunit;

namespace Fjernlaas.Tests;

public class SmsParserTests
{
    [Fact]
    public void Laeser_inmobiles_streng_med_linjeskift_til_sidst()
    {
        var besked = SmsParser.Parse("4512345678;Roshage\r");

        Assert.NotNull(besked);
        Assert.Equal("+4512345678", besked!.Afsender);
        Assert.Equal("Roshage", besked.Tekst);
        Assert.Equal("Roshage", besked.Noegleord);
    }

    [Theory]
    [InlineData("4512345678;Roshage", "+4512345678")]
    [InlineData("+4512345678;Roshage", "+4512345678")]
    [InlineData("0045 12 34 56 78;Roshage", "+4512345678")]
    [InlineData("12345678;Roshage", "+4512345678")]
    [InlineData("46701234567;Roshage", "+46701234567")]
    public void Normaliserer_afsenderens_nummer(string raa, string forventet)
    {
        Assert.Equal(forventet, SmsParser.Parse(raa)!.Afsender);
    }

    [Theory]
    [InlineData(";Roshage")]
    [InlineData("123;Roshage")]
    [InlineData("abc;Roshage")]
    [InlineData("1234567890123456;Roshage")]
    public void Afviser_ugyldigt_nummer(string raa)
    {
        Assert.Null(SmsParser.Parse(raa));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Roshage")]
    [InlineData("4512345678")]
    [InlineData("4512345678;")]
    [InlineData("4512345678; \r\n ")]
    [InlineData("4512345678;...")]
    public void Afviser_streng_uden_tekst_eller_semikolon(string? raa)
    {
        Assert.Null(SmsParser.Parse(raa));
    }

    [Fact]
    public void Splitter_ved_foerste_semikolon_og_beholder_resten_i_teksten()
    {
        var besked = SmsParser.Parse("4512345678;Roshage; tak for sidst");

        Assert.Equal("Roshage; tak for sidst", besked!.Tekst);
        Assert.Equal("Roshage", besked.Noegleord);
    }

    [Fact]
    public void Noegleord_er_foerste_ord_uden_tegnsaetning_i_enderne()
    {
        var besked = SmsParser.Parse("4512345678;  \"Roshage!\"   hej  \t der\r\n");

        Assert.Equal("Roshage", besked!.Noegleord);
        Assert.Equal("\"Roshage!\" hej der", besked.Tekst);
    }

    [Fact]
    public void Afkorter_lange_tekster()
    {
        var besked = SmsParser.Parse("4512345678;Roshage " + new string('x', 1000));

        Assert.Equal(SmsParser.MaxTekstLaengde, besked!.Tekst.Length);
    }
}
