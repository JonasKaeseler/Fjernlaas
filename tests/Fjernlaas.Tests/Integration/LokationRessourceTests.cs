using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fjernlaas.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class LokationRessourceTests(SqlServerFixture fixture)
{
    private static string Unik() => Guid.NewGuid().ToString("N");

    private static Aktoer Platform() => new(Guid.NewGuid(), null, true, false);
    private static Aktoer OrgAdmin(Guid org) => new(Guid.NewGuid(), org, false, true);
    private static Aktoer Almindelig(Guid org) => new(Guid.NewGuid(), org, false, false);

    private LokationService Lokationer() => new(fixture.CreateContext());
    private RessourceService Ressourcer() => new(fixture.CreateContext());

    private async Task<Guid> NyOrganisationAsync()
    {
        var res = await new OrganisationService(fixture.CreateContext()).OpretAsync($"Org {Unik()}");
        return res.Organisation!.Id;
    }

    private async Task<Lokation> NyLokationAsync(Guid org, string? navn = null)
    {
        var res = await Lokationer().OpretAsync(Platform(), org, navn ?? $"Lok {Unik()}", null, "Europe/Copenhagen");
        Assert.True(res.Ok);
        return res.Lokation!;
    }

    [Fact]
    public async Task Opret_lokation_gemmer_felterne_og_goer_den_aktiv()
    {
        var org = await NyOrganisationAsync();

        var res = await Lokationer().OpretAsync(OrgAdmin(org), org, "  Butikken  ", " Vej 1 ", "Europe/Copenhagen");

        Assert.True(res.Ok);
        Assert.Equal("Butikken", res.Lokation!.Navn);
        Assert.Equal("Vej 1", res.Lokation.Adresse);
        Assert.Equal(LokationStatus.Aktiv, res.Lokation.Status);
        Assert.Equal(org, res.Lokation.OrganisationId);
    }

    [Theory]
    [InlineData("", "Europe/Copenhagen", LokationFejl.NavnUgyldigt)]
    [InlineData("   ", "Europe/Copenhagen", LokationFejl.NavnUgyldigt)]
    [InlineData("Ok", "Mars/Olympus", LokationFejl.TidszoneUgyldig)]
    [InlineData("Ok", null, LokationFejl.TidszoneUgyldig)]
    public async Task Opret_lokation_validerer(string navn, string? tidszone, LokationFejl forventet)
    {
        var org = await NyOrganisationAsync();

        var res = await Lokationer().OpretAsync(Platform(), org, navn, null, tidszone);

        Assert.Equal(forventet, res.Fejl);
    }

    [Fact]
    public async Task Opret_lokation_afviser_dublet_i_samme_organisation_men_ikke_i_en_anden()
    {
        var org1 = await NyOrganisationAsync();
        var org2 = await NyOrganisationAsync();
        var navn = $"Butik {Unik()}";
        await NyLokationAsync(org1, navn);

        var dublet = await Lokationer().OpretAsync(Platform(), org1, navn.ToUpperInvariant(), null, "UTC");
        var andenOrg = await Lokationer().OpretAsync(Platform(), org2, navn, null, "UTC");

        Assert.Equal(LokationFejl.NavnFindesAllerede, dublet.Fejl);
        Assert.True(andenOrg.Ok);
    }

    [Fact]
    public async Task Opret_lokation_i_ukendt_organisation_giver_fejl()
    {
        var res = await Lokationer().OpretAsync(Platform(), Guid.NewGuid(), "X", null, "UTC");

        Assert.Equal(LokationFejl.OrganisationIkkeFundet, res.Fejl);
    }

    [Fact]
    public async Task Almindelig_bruger_maa_ikke_administrere()
    {
        var org = await NyOrganisationAsync();
        var bruger = Almindelig(org);

        var res = await Lokationer().OpretAsync(bruger, org, "X", null, "UTC");

        Assert.Equal(LokationFejl.IkkeTilladt, res.Fejl);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Lokationer().OversigtAsync(bruger));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Ressourcer().OversigtAsync(bruger));
    }

    [Fact]
    public async Task Organisationsadmin_kan_ikke_oprette_i_en_anden_organisation()
    {
        var egen = await NyOrganisationAsync();
        var anden = await NyOrganisationAsync();

        var res = await Lokationer().OpretAsync(OrgAdmin(egen), anden, "X", null, "UTC");

        Assert.Equal(LokationFejl.IkkeTilladt, res.Fejl);
    }

    [Fact]
    public async Task Organisationsadmin_ser_kun_egne_lokationer_og_ressourcer()
    {
        var org1 = await NyOrganisationAsync();
        var org2 = await NyOrganisationAsync();
        var lok1 = await NyLokationAsync(org1);
        var lok2 = await NyLokationAsync(org2);
        Assert.True((await Ressourcer().OpretAsync(Platform(), lok1.Id, "Dør 1")).Ok);
        Assert.True((await Ressourcer().OpretAsync(Platform(), lok2.Id, "Dør 2")).Ok);

        var admin1 = OrgAdmin(org1);
        var lokationer = await Lokationer().OversigtAsync(admin1);
        var ressourcer = await Ressourcer().OversigtAsync(admin1);

        Assert.Contains(lokationer, l => l.Id == lok1.Id);
        Assert.DoesNotContain(lokationer, l => l.Id == lok2.Id);
        Assert.All(lokationer, l => Assert.Equal(org1, l.OrganisationId));
        Assert.All(ressourcer, r => Assert.Equal(org1, r.OrganisationId));
        Assert.Contains(ressourcer, r => r.Navn == "Dør 1");
        Assert.DoesNotContain(ressourcer, r => r.Navn == "Dør 2");
    }

    [Fact]
    public async Task Platformsadmin_ser_alle_organisationer()
    {
        var org1 = await NyOrganisationAsync();
        var org2 = await NyOrganisationAsync();
        var lok1 = await NyLokationAsync(org1);
        var lok2 = await NyLokationAsync(org2);

        var alle = await Lokationer().OversigtAsync(Platform());

        Assert.Contains(alle, l => l.Id == lok1.Id);
        Assert.Contains(alle, l => l.Id == lok2.Id);
    }

    [Fact]
    public async Task Fremmed_organisations_lokation_er_ikke_fundet_hverken_ved_opslag_eller_aendring()
    {
        var egen = await NyOrganisationAsync();
        var anden = await NyOrganisationAsync();
        var fremmed = await NyLokationAsync(anden);
        var admin = OrgAdmin(egen);

        Assert.Null(await Lokationer().HentAsync(admin, fremmed.Id));
        Assert.Equal(LokationFejl.IkkeFundet, (await Lokationer().RedigerAsync(admin, fremmed.Id, "Hacket", null, "UTC")).Fejl);
        Assert.Equal(LokationFejl.IkkeFundet, (await Lokationer().SaetStatusAsync(admin, fremmed.Id, LokationStatus.Deaktiveret)).Fejl);
        Assert.Equal(RessourceFejl.LokationIkkeFundet, (await Ressourcer().OpretAsync(admin, fremmed.Id, "Dør")).Fejl);

        var uaendret = await Lokationer().HentAsync(Platform(), fremmed.Id);
        Assert.Equal(fremmed.Navn, uaendret!.Navn);
        Assert.Equal(LokationStatus.Aktiv, uaendret.Status);
    }

    [Fact]
    public async Task Fremmed_organisations_ressource_kan_ikke_aendres()
    {
        var egen = await NyOrganisationAsync();
        var anden = await NyOrganisationAsync();
        var lok = await NyLokationAsync(anden);
        var ressource = (await Ressourcer().OpretAsync(Platform(), lok.Id, "Dør")).Ressource!;
        var admin = OrgAdmin(egen);

        Assert.Equal(RessourceFejl.IkkeFundet, (await Ressourcer().OmdoebAsync(admin, ressource.Id, "Hacket")).Fejl);
        Assert.Equal(RessourceFejl.IkkeFundet, (await Ressourcer().SaetStatusAsync(admin, ressource.Id, RessourceStatus.Pause)).Fejl);
    }

    [Fact]
    public async Task Kontekst_uden_tenant_ser_ingenting()
    {
        var org = await NyOrganisationAsync();
        var lok = await NyLokationAsync(org);
        await Ressourcer().OpretAsync(Platform(), lok.Id, "Dør");

        await using var db = fixture.CreateContext();

        Assert.Empty(await db.Lokationer.Where(l => l.OrganisationId == org).ToListAsync());
        Assert.Empty(await db.Ressourcer.Where(r => r.OrganisationId == org).ToListAsync());
        Assert.NotEmpty(await db.Lokationer.IgnoreQueryFilters().Where(l => l.OrganisationId == org).ToListAsync());
    }

    [Fact]
    public async Task Rediger_og_status_virker()
    {
        var org = await NyOrganisationAsync();
        var lok = await NyLokationAsync(org);
        var admin = OrgAdmin(org);

        var ret = await Lokationer().RedigerAsync(admin, lok.Id, "Nyt navn " + Unik(), "Ny adresse", "Europe/Oslo");
        var deaktiveret = await Lokationer().SaetStatusAsync(admin, lok.Id, LokationStatus.Deaktiveret);

        Assert.True(ret.Ok);
        Assert.True(deaktiveret.Ok);
        var gemt = await Lokationer().HentAsync(admin, lok.Id);
        Assert.Equal("Ny adresse", gemt!.Adresse);
        Assert.Equal("Europe/Oslo", gemt.Tidszone);
        Assert.Equal(LokationStatus.Deaktiveret, gemt.Status);
    }

    [Fact]
    public async Task Rediger_afviser_navn_der_tilhoerer_en_anden_lokation_i_organisationen()
    {
        var org = await NyOrganisationAsync();
        var a = await NyLokationAsync(org);
        var b = await NyLokationAsync(org);

        var res = await Lokationer().RedigerAsync(Platform(), b.Id, a.Navn.ToLowerInvariant(), null, "UTC");

        Assert.Equal(LokationFejl.NavnFindesAllerede, res.Fejl);
    }

    [Fact]
    public async Task Ressource_arver_organisation_og_navn_er_unikt_pr_lokation()
    {
        var org = await NyOrganisationAsync();
        var lok1 = await NyLokationAsync(org);
        var lok2 = await NyLokationAsync(org);

        var foerste = await Ressourcer().OpretAsync(OrgAdmin(org), lok1.Id, "  Hoveddør ");
        var dublet = await Ressourcer().OpretAsync(OrgAdmin(org), lok1.Id, "HOVEDDØR");
        var andenLokation = await Ressourcer().OpretAsync(OrgAdmin(org), lok2.Id, "Hoveddør");

        Assert.True(foerste.Ok);
        Assert.Equal(org, foerste.Ressource!.OrganisationId);
        Assert.Equal("Hoveddør", foerste.Ressource.Navn);
        Assert.Equal(RessourceStatus.Aktiv, foerste.Ressource.Status);
        Assert.Equal(RessourceFejl.NavnFindesAllerede, dublet.Fejl);
        Assert.True(andenLokation.Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ressource_afviser_tomt_navn(string navn)
    {
        var org = await NyOrganisationAsync();
        var lok = await NyLokationAsync(org);

        Assert.Equal(RessourceFejl.NavnUgyldigt, (await Ressourcer().OpretAsync(Platform(), lok.Id, navn)).Fejl);
    }

    [Fact]
    public async Task Ressource_kan_omdoebes_og_saettes_paa_pause()
    {
        var org = await NyOrganisationAsync();
        var lok = await NyLokationAsync(org);
        var admin = OrgAdmin(org);
        var ressource = (await Ressourcer().OpretAsync(admin, lok.Id, "Dør")).Ressource!;

        Assert.True((await Ressourcer().OmdoebAsync(admin, ressource.Id, "Bagdør")).Ok);
        Assert.True((await Ressourcer().SaetStatusAsync(admin, ressource.Id, RessourceStatus.Pause)).Ok);

        var liste = await Ressourcer().OversigtAsync(admin, lok.Id);
        var gemt = Assert.Single(liste);
        Assert.Equal("Bagdør", gemt.Navn);
        Assert.Equal(RessourceStatus.Pause, gemt.Status);

        Assert.True((await Ressourcer().SaetStatusAsync(admin, ressource.Id, RessourceStatus.Aktiv)).Ok);
        Assert.Equal(RessourceStatus.Aktiv, Assert.Single(await Ressourcer().OversigtAsync(admin, lok.Id)).Status);
    }

    [Fact]
    public async Task Oversigt_taeller_ressourcer_pr_lokation()
    {
        var org = await NyOrganisationAsync();
        var lok = await NyLokationAsync(org);
        await Ressourcer().OpretAsync(Platform(), lok.Id, "A");
        await Ressourcer().OpretAsync(Platform(), lok.Id, "B");

        var oversigt = await Lokationer().OversigtAsync(OrgAdmin(org));

        Assert.Equal(2, Assert.Single(oversigt, l => l.Id == lok.Id).AntalRessourcer);
    }
}
