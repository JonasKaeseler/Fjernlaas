using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fjernlaas.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class SmsNummerNoegleordTests(SqlServerFixture fixture)
{
    private static string Unik() => Guid.NewGuid().ToString("N");

    private static Aktoer Platform() => new(Guid.NewGuid(), null, true, false);
    private static Aktoer OrgAdmin(Guid org) => new(Guid.NewGuid(), org, false, true);
    private static Aktoer Almindelig(Guid org) => new(Guid.NewGuid(), org, false, false);

    private SmsNummerService Numre() => new(fixture.CreateContext());
    private NoegleordService Ord() => new(fixture.CreateContext());

    private async Task<Guid> NyOrganisationAsync() =>
        (await new OrganisationService(fixture.CreateContext()).OpretAsync($"Org {Unik()}")).Organisation!.Id;

    private async Task<Ressource> NyRessourceAsync(Guid org)
    {
        var lokation = (await new LokationService(fixture.CreateContext())
            .OpretAsync(Platform(), org, $"Butik {Unik()}", null, "UTC")).Lokation!;
        return (await new RessourceService(fixture.CreateContext()).OpretAsync(Platform(), lokation.Id, $"Dør {Unik()}")).Ressource!;
    }

    [Fact]
    public async Task Opret_giver_en_token_og_gemmer_kun_hashet()
    {
        var org = await NyOrganisationAsync();

        var res = await Numre().OpretAsync(OrgAdmin(org), org, "Roshage-nummer", "12345678");

        Assert.True(res.Ok);
        Assert.False(string.IsNullOrWhiteSpace(res.Token));
        Assert.True(res.Token!.Length >= 40);
        Assert.Equal("+4512345678", res.SmsNummer!.Nummer);
        Assert.Equal(res.Token[..SmsNummerService.PraefiksLaengde], res.SmsNummer.TokenPraefiks);
        Assert.Equal(SmsNummerService.HashToken(res.Token), res.SmsNummer.TokenHash);

        await using var db = fixture.CreateContext();
        db.SaetTenant(true, null);
        var gemt = await db.SmsNumre.AsNoTracking().SingleAsync(n => n.Id == res.SmsNummer.Id);
        Assert.DoesNotContain(res.Token, gemt.TokenHash);
        Assert.Equal(64, gemt.TokenHash.Length);
    }

    [Fact]
    public async Task To_numre_faar_forskellige_tokens()
    {
        var org = await NyOrganisationAsync();

        var a = await Numre().OpretAsync(Platform(), org, "A", null);
        var b = await Numre().OpretAsync(Platform(), org, "B", null);

        Assert.NotEqual(a.Token, b.Token);
        Assert.NotEqual(a.SmsNummer!.TokenHash, b.SmsNummer!.TokenHash);
    }

    [Fact]
    public async Task Nyt_token_goer_det_gamle_ugyldigt()
    {
        var org = await NyOrganisationAsync();
        var oprettet = await Numre().OpretAsync(Platform(), org, "A", null);

        var nyt = await Numre().NytTokenAsync(OrgAdmin(org), oprettet.SmsNummer!.Id);

        Assert.True(nyt.Ok);
        Assert.NotEqual(oprettet.Token, nyt.Token);

        await using var db = fixture.CreateContext();
        db.SaetTenant(true, null);
        var gammelHash = SmsNummerService.HashToken(oprettet.Token!);
        Assert.Empty(await db.SmsNumre.Where(n => n.TokenHash == gammelHash).ToListAsync());
        Assert.Single(await db.SmsNumre.Where(n => n.TokenHash == SmsNummerService.HashToken(nyt.Token!)).ToListAsync());
    }

    [Theory]
    [InlineData("", null, SmsNummerFejl.NavnUgyldigt)]
    [InlineData("A", "abc", SmsNummerFejl.NummerUgyldigt)]
    public async Task Opret_validerer(string navn, string? nummer, SmsNummerFejl forventet)
    {
        var org = await NyOrganisationAsync();

        Assert.Equal(forventet, (await Numre().OpretAsync(Platform(), org, navn, nummer)).Fejl);
    }

    [Fact]
    public async Task Rettigheder_og_isolation_for_sms_numre()
    {
        var org1 = await NyOrganisationAsync();
        var org2 = await NyOrganisationAsync();
        var n1 = (await Numre().OpretAsync(Platform(), org1, "A", null)).SmsNummer!;
        var n2 = (await Numre().OpretAsync(Platform(), org2, "B", null)).SmsNummer!;

        Assert.Equal(SmsNummerFejl.IkkeTilladt, (await Numre().OpretAsync(Almindelig(org1), org1, "X", null)).Fejl);
        Assert.Equal(SmsNummerFejl.IkkeTilladt, (await Numre().OpretAsync(OrgAdmin(org1), org2, "X", null)).Fejl);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Numre().OversigtAsync(Almindelig(org1)));

        var liste = await Numre().OversigtAsync(OrgAdmin(org1));
        Assert.Contains(liste, n => n.Id == n1.Id);
        Assert.DoesNotContain(liste, n => n.Id == n2.Id);

        Assert.Equal(SmsNummerFejl.IkkeFundet, (await Numre().NytTokenAsync(OrgAdmin(org1), n2.Id)).Fejl);
        Assert.Equal(SmsNummerFejl.IkkeFundet, (await Numre().SaetStatusAsync(OrgAdmin(org1), n2.Id, SmsNummerStatus.Deaktiveret)).Fejl);
    }

    [Fact]
    public async Task Noegleord_oprettes_og_er_unikt_pr_sms_nummer_uanset_store_og_smaa_bogstaver()
    {
        var org = await NyOrganisationAsync();
        var ressource = await NyRessourceAsync(org);
        var nummer = (await Numre().OpretAsync(Platform(), org, "A", null)).SmsNummer!;

        var foerste = await Ord().OpretAsync(OrgAdmin(org), nummer.Id, ressource.Id, "Roshage");
        var dublet = await Ord().OpretAsync(OrgAdmin(org), nummer.Id, ressource.Id, "ROSHAGE");

        Assert.True(foerste.Ok);
        Assert.Equal("ROSHAGE", foerste.Noegleord!.OrdNormaliseret);
        Assert.Equal(NoegleordFejl.OrdFindesAllerede, dublet.Fejl);

        // Samme ord på et andet nummer er i orden.
        var andetNummer = (await Numre().OpretAsync(Platform(), org, "B", null)).SmsNummer!;
        Assert.True((await Ord().OpretAsync(OrgAdmin(org), andetNummer.Id, ressource.Id, "Roshage")).Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("to ord")]
    [InlineData("a;b")]
    [InlineData("tab\tord")]
    public async Task Noegleord_skal_vaere_et_enkelt_ord(string ord)
    {
        var org = await NyOrganisationAsync();
        var ressource = await NyRessourceAsync(org);
        var nummer = (await Numre().OpretAsync(Platform(), org, "A", null)).SmsNummer!;

        Assert.Equal(NoegleordFejl.OrdUgyldigt, (await Ord().OpretAsync(Platform(), nummer.Id, ressource.Id, ord)).Fejl);
    }

    [Fact]
    public async Task Noegleord_kan_ikke_pege_paa_en_anden_organisations_ressource_eller_nummer()
    {
        var org1 = await NyOrganisationAsync();
        var org2 = await NyOrganisationAsync();
        var ressource1 = await NyRessourceAsync(org1);
        var ressource2 = await NyRessourceAsync(org2);
        var nummer1 = (await Numre().OpretAsync(Platform(), org1, "A", null)).SmsNummer!;
        var nummer2 = (await Numre().OpretAsync(Platform(), org2, "B", null)).SmsNummer!;

        // Platformsadmin ser alt, men ressource og nummer skal stadig høre til samme organisation.
        Assert.Equal(NoegleordFejl.RessourceIkkeFundet, (await Ord().OpretAsync(Platform(), nummer1.Id, ressource2.Id, "ord")).Fejl);

        // Organisationsadmin i org1 kan hverken bruge org2's nummer eller ressource.
        var admin1 = OrgAdmin(org1);
        Assert.Equal(NoegleordFejl.SmsNummerIkkeFundet, (await Ord().OpretAsync(admin1, nummer2.Id, ressource1.Id, "ord")).Fejl);
        Assert.Equal(NoegleordFejl.RessourceIkkeFundet, (await Ord().OpretAsync(admin1, nummer1.Id, ressource2.Id, "ord")).Fejl);
    }

    [Fact]
    public async Task Noegleord_oversigt_og_status_er_afgraenset_til_organisationen()
    {
        var org1 = await NyOrganisationAsync();
        var org2 = await NyOrganisationAsync();
        var r1 = await NyRessourceAsync(org1);
        var r2 = await NyRessourceAsync(org2);
        var n1 = (await Numre().OpretAsync(Platform(), org1, "A", null)).SmsNummer!;
        var n2 = (await Numre().OpretAsync(Platform(), org2, "B", null)).SmsNummer!;
        var k1 = (await Ord().OpretAsync(Platform(), n1.Id, r1.Id, "eet")).Noegleord!;
        var k2 = (await Ord().OpretAsync(Platform(), n2.Id, r2.Id, "to")).Noegleord!;

        var liste = await Ord().OversigtAsync(OrgAdmin(org1));

        Assert.Contains(liste, k => k.Id == k1.Id);
        Assert.DoesNotContain(liste, k => k.Id == k2.Id);
        Assert.Equal(NoegleordFejl.IkkeFundet, (await Ord().SaetStatusAsync(OrgAdmin(org1), k2.Id, NoegleordStatus.Deaktiveret)).Fejl);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Ord().OversigtAsync(Almindelig(org1)));
    }

    [Fact]
    public async Task Ressource_kan_kun_kobles_til_en_forbindelse_i_samme_organisation()
    {
        var org1 = await NyOrganisationAsync();
        var org2 = await NyOrganisationAsync();
        var ressource = await NyRessourceAsync(org1);

        var klient = new FalskTTLockKlient();
        var db = fixture.CreateContext();
        var tid = new TestTid();
        var beskytter = new DataProtectionBeskytterTilTest();
        var tokens = new TTLockTokenService(db, klient, beskytter.Beskytter, new TokenLaase(), tid);
        var service = new TTLockForbindelseService(db, klient, tokens, beskytter.Beskytter,
            Microsoft.Extensions.Options.Options.Create(new TTLockOptions { ClientId = "id", ClientSecret = "s" }));
        var fremmed = (await service.OpretAsync(Platform(), org2, "Fremmed", "x@example.com", "kode")).Forbindelse!;
        var egen = (await service.OpretAsync(Platform(), org1, "Egen", "x@example.com", "kode")).Forbindelse!;

        var r = new RessourceService(fixture.CreateContext());
        Assert.Equal(RessourceFejl.ForbindelseIkkeFundet, (await r.KoblAsync(Platform(), ressource.Id, fremmed.Id, 5)).Fejl);
        Assert.Equal(RessourceFejl.KoblingUgyldig, (await r.KoblAsync(Platform(), ressource.Id, egen.Id, null)).Fejl);
        Assert.Equal(RessourceFejl.KoblingUgyldig, (await r.KoblAsync(Platform(), ressource.Id, null, 5)).Fejl);

        var ok = await r.KoblAsync(OrgAdmin(org1), ressource.Id, egen.Id, 5);
        Assert.True(ok.Ok);
        Assert.Equal(5, ok.Ressource!.TTLockLockId);

        var fjernet = await r.KoblAsync(OrgAdmin(org1), ressource.Id, null, null);
        Assert.True(fjernet.Ok);
        Assert.Null(fjernet.Ressource!.ForbindelseId);
    }

    private sealed class DataProtectionBeskytterTilTest
    {
        public IHemmelighedBeskytter Beskytter { get; } =
            new DataProtectionBeskytter(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
    }
}
