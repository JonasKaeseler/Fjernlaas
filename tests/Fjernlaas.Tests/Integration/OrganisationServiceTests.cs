using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Xunit;

namespace Fjernlaas.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class OrganisationServiceTests(SqlServerFixture fixture)
{
    private static string Unik() => Guid.NewGuid().ToString("N");

    private OrganisationService NyService() => new(fixture.CreateContext());

    [Fact]
    public async Task Opret_goer_organisationen_aktiv_og_trimmer_navnet()
    {
        var navn = $"Butik {Unik()}";

        var resultat = await NyService().OpretAsync($"  {navn}  ");

        Assert.True(resultat.Ok);
        Assert.Equal(navn, resultat.Organisation!.Navn);
        Assert.Equal(OrganisationStatus.Aktiv, resultat.Organisation.Status);

        var gemt = await NyService().HentAsync(resultat.Organisation.Id);
        Assert.NotNull(gemt);
        Assert.Equal(navn, gemt!.Navn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Opret_afviser_tomt_navn(string? navn)
    {
        var resultat = await NyService().OpretAsync(navn);

        Assert.Equal(OrganisationFejl.NavnUgyldigt, resultat.Fejl);
    }

    [Fact]
    public async Task Opret_afviser_for_langt_navn()
    {
        var resultat = await NyService().OpretAsync(new string('a', OrganisationService.MaxNavnLaengde + 1));

        Assert.Equal(OrganisationFejl.NavnUgyldigt, resultat.Fejl);
    }

    [Fact]
    public async Task Opret_afviser_navn_der_findes_uanset_store_og_smaa_bogstaver()
    {
        var navn = $"Roshage {Unik()}";
        Assert.True((await NyService().OpretAsync(navn)).Ok);

        var resultat = await NyService().OpretAsync($" {navn.ToUpperInvariant()} ");

        Assert.Equal(OrganisationFejl.NavnFindesAllerede, resultat.Fejl);
    }

    [Fact]
    public async Task Liste_kan_soeges_uanset_store_og_smaa_bogstaver_og_er_sorteret()
    {
        var u = Unik();
        await NyService().OpretAsync($"Beta {u}");
        await NyService().OpretAsync($"Alpha {u}");
        await NyService().OpretAsync($"Andet {Unik()}");

        var fundet = await NyService().ListAsync(u.ToUpperInvariant());

        Assert.Equal([$"Alpha {u}", $"Beta {u}"], fundet.Select(o => o.Navn).ToArray());
    }

    [Fact]
    public async Task Liste_uden_soegning_indeholder_oprettede_organisationer()
    {
        var navn = $"Butik {Unik()}";
        await NyService().OpretAsync(navn);

        var alle = await NyService().ListAsync();

        Assert.Contains(alle, o => o.Navn == navn);
    }

    [Fact]
    public async Task Omdoeb_aendrer_navnet()
    {
        var org = (await NyService().OpretAsync($"Gammelt {Unik()}")).Organisation!;
        var nytNavn = $"Nyt {Unik()}";

        var resultat = await NyService().OmdoebAsync(org.Id, nytNavn);

        Assert.True(resultat.Ok);
        Assert.Equal(nytNavn, (await NyService().HentAsync(org.Id))!.Navn);
    }

    [Fact]
    public async Task Omdoeb_tillader_at_aendre_store_og_smaa_bogstaver_paa_eget_navn()
    {
        var u = Unik();
        var org = (await NyService().OpretAsync($"butik {u}")).Organisation!;

        var resultat = await NyService().OmdoebAsync(org.Id, $"BUTIK {u}");

        Assert.True(resultat.Ok);
        Assert.Equal($"BUTIK {u}", (await NyService().HentAsync(org.Id))!.Navn);
    }

    [Fact]
    public async Task Omdoeb_afviser_navn_som_en_anden_organisation_har()
    {
        var andens = $"Optaget {Unik()}";
        await NyService().OpretAsync(andens);
        var org = (await NyService().OpretAsync($"Min {Unik()}")).Organisation!;

        var resultat = await NyService().OmdoebAsync(org.Id, andens.ToUpperInvariant());

        Assert.Equal(OrganisationFejl.NavnFindesAllerede, resultat.Fejl);
    }

    [Fact]
    public async Task Omdoeb_giver_ikke_fundet_for_ukendt_id()
    {
        var resultat = await NyService().OmdoebAsync(Guid.CreateVersion7(), "Hvad som helst");

        Assert.Equal(OrganisationFejl.IkkeFundet, resultat.Fejl);
    }

    [Fact]
    public async Task Deaktiver_og_aktiver_aendrer_status_og_kan_gentages()
    {
        var org = (await NyService().OpretAsync($"Butik {Unik()}")).Organisation!;

        Assert.True((await NyService().SaetStatusAsync(org.Id, OrganisationStatus.Deaktiveret)).Ok);
        Assert.True((await NyService().SaetStatusAsync(org.Id, OrganisationStatus.Deaktiveret)).Ok);
        Assert.Equal(OrganisationStatus.Deaktiveret, (await NyService().HentAsync(org.Id))!.Status);

        Assert.True((await NyService().SaetStatusAsync(org.Id, OrganisationStatus.Aktiv)).Ok);
        Assert.Equal(OrganisationStatus.Aktiv, (await NyService().HentAsync(org.Id))!.Status);
    }

    [Fact]
    public async Task Status_giver_ikke_fundet_for_ukendt_id()
    {
        var resultat = await NyService().SaetStatusAsync(Guid.CreateVersion7(), OrganisationStatus.Deaktiveret);

        Assert.Equal(OrganisationFejl.IkkeFundet, resultat.Fejl);
    }

    [Fact]
    public async Task Oversigt_viser_antal_brugere_status_og_kan_soeges()
    {
        var u = Unik();
        var medBrugere = (await NyService().OpretAsync($"Med brugere {u}")).Organisation!;
        var tom = (await NyService().OpretAsync($"Tom {u}")).Organisation!;
        await NyService().SaetStatusAsync(tom.Id, OrganisationStatus.Deaktiveret);

        await using (var db = fixture.CreateContext())
        {
            foreach (var i in new[] { 1, 2 })
            {
                var email = $"b{i}-{u}@example.com";
                db.Users.Add(new ApplicationUser
                {
                    UserName = email,
                    NormalizedUserName = Normalisering.Normaliser(email),
                    Email = email,
                    NormalizedEmail = Normalisering.Normaliser(email),
                    Navn = $"Bruger {i}",
                    OrganisationId = medBrugere.Id
                });
            }
            await db.SaveChangesAsync();
        }

        var oversigt = await NyService().OversigtAsync(u);

        Assert.Equal(2, oversigt.Count);
        var a = oversigt.Single(o => o.Id == medBrugere.Id);
        var b = oversigt.Single(o => o.Id == tom.Id);
        Assert.Equal(2, a.AntalBrugere);
        Assert.Equal(OrganisationStatus.Aktiv, a.Status);
        Assert.Equal(0, b.AntalBrugere);
        Assert.Equal(OrganisationStatus.Deaktiveret, b.Status);
    }
}
