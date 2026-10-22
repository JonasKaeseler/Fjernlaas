using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fjernlaas.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class TestOplaasningTests(SqlServerFixture fixture)
{
    private static Aktoer Platform() => new(Guid.NewGuid(), null, true, false);
    private static Aktoer OrgAdmin(Guid org) => new(Guid.NewGuid(), org, false, true);
    private static Aktoer Almindelig(Guid org) => new(Guid.NewGuid(), org, false, false);

    // Fælles nøgle, så tokens gemt af én service kan læses af en anden, som i en kørende app.
    private static readonly EphemeralDataProtectionProvider Noegler = new();

    private (OplaasningService Service, FalskTTLockKlient Klient, TTLockForbindelseService Forbindelser) Byg()
    {
        var klient = new FalskTTLockKlient();
        var db = fixture.CreateContext();
        var beskytter = new DataProtectionBeskytter(Noegler);
        var tid = new TestTid();
        var tokens = new TTLockTokenService(db, klient, beskytter, new TokenLaase(), tid);
        var forbindelser = new TTLockForbindelseService(db, klient, tokens, beskytter,
            Options.Create(new TTLockOptions { ClientId = "id", ClientSecret = "s" }));
        return (new OplaasningService(db, klient, tokens, tid, NullLogger<OplaasningService>.Instance), klient, forbindelser);
    }

    private async Task<(Guid Org, Guid Ressource, Guid Forbindelse)> OpsaetAsync(bool kobl = true)
    {
        var org = (await new OrganisationService(fixture.CreateContext()).OpretAsync($"Org {Guid.NewGuid():N}")).Organisation!.Id;
        var lokation = (await new LokationService(fixture.CreateContext()).OpretAsync(Platform(), org, "Butik", null, "UTC")).Lokation!;
        var ressource = (await new RessourceService(fixture.CreateContext()).OpretAsync(Platform(), lokation.Id, "Dør")).Ressource!;
        var forbindelse = (await Byg().Forbindelser.OpretAsync(Platform(), org, "Konto", "x@example.com", "kode")).Forbindelse!;
        if (kobl) Assert.True((await new RessourceService(fixture.CreateContext()).KoblAsync(Platform(), ressource.Id, forbindelse.Id, 42)).Ok);
        return (org, ressource.Id, forbindelse.Id);
    }

    [Fact]
    public async Task Admin_kan_teste_og_doeren_aabnes()
    {
        var (org, ressource, _) = await OpsaetAsync();
        var (service, klient, _) = Byg();

        var res = await service.TestAabnAsync(OrgAdmin(org), ressource);

        Assert.True(res.Aabnet);
        Assert.Equal(1, klient.AabnKald);
        Assert.Equal(42, klient.SidsteLockId);
    }

    [Fact]
    public async Task Almindelig_bruger_og_anden_organisation_kan_ikke_teste()
    {
        var (org, ressource, _) = await OpsaetAsync();
        var anden = (await new OrganisationService(fixture.CreateContext()).OpretAsync($"Org {Guid.NewGuid():N}")).Organisation!.Id;
        var (service, klient, _) = Byg();

        Assert.True((await service.TestAabnAsync(Almindelig(org), ressource)).IkkeTilladt);
        Assert.True((await Byg().Service.TestAabnAsync(OrgAdmin(anden), ressource)).IkkeFundet);
        Assert.Equal(0, klient.AabnKald);
    }

    [Fact]
    public async Task Ikke_koblet_pause_og_ttlock_fejl_forklares()
    {
        var (org, ressource, _) = await OpsaetAsync(kobl: false);
        Assert.Equal(Udfald.AfvistIkkeKonfigureret, (await Byg().Service.TestAabnAsync(OrgAdmin(org), ressource)).Udfald);

        var (org2, ressource2, _) = await OpsaetAsync();
        await new RessourceService(fixture.CreateContext()).SaetStatusAsync(Platform(), ressource2, RessourceStatus.Pause);
        var (service, klient, _) = Byg();
        Assert.Equal(Udfald.AfvistRessourcePause, (await service.TestAabnAsync(OrgAdmin(org2), ressource2)).Udfald);
        Assert.Equal(0, klient.AabnKald);

        var (org3, ressource3, _) = await OpsaetAsync();
        var (service3, klient3, _) = Byg();
        klient3.AabnSvar.Enqueue(TTLockSvar<bool>.Fejlet(TTLockKoder.IngenGateway, "ingen gateway"));
        var res = await service3.TestAabnAsync(OrgAdmin(org3), ressource3);

        Assert.Equal(Udfald.TTLockFejl, res.Udfald);
        Assert.Equal(TTLockKoder.IngenGateway, res.ErrCode);
    }
}
