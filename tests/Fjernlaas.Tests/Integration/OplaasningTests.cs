using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fjernlaas.Tests.Integration;

/// <summary>Hele flowet fra SMS til åben dør mod en rigtig SQL Server og en falsk TTLock.</summary>
[Collection(SqlServerCollection.Name)]
public class OplaasningTests(SqlServerFixture fixture)
{
    private const string Raa = "4512345678;Roshage\r";

    private static string Unik() => Guid.NewGuid().ToString("N");
    private static Aktoer Platform() => new(Guid.NewGuid(), null, true, false);

    private static readonly IHemmelighedBeskytter Beskytter = new DataProtectionBeskytter(new EphemeralDataProtectionProvider());
    private static readonly TokenLaase Laase = new();
    private static readonly IOptions<TTLockOptions> Opt = Options.Create(new TTLockOptions { ClientId = "id", ClientSecret = "hemmelig" });

    /// <summary>Alt, en SMS-oplåsning kræver, oprettet gennem de rigtige services.</summary>
    private sealed record Opsaetning(
        Guid OrganisationId, Guid LokationId, Guid RessourceId, Guid ForbindelseId, Guid SmsNummerId, string Token, long LockId);

    private async Task<Opsaetning> OpsaetAsync(FalskTTLockKlient klient, TestTid tid, long lockId = 32429590, bool kobl = true)
    {
        var org = (await new OrganisationService(fixture.CreateContext()).OpretAsync($"Org {Unik()}")).Organisation!.Id;
        var admin = Platform();

        var lokation = (await new LokationService(fixture.CreateContext())
            .OpretAsync(admin, org, $"Butik {Unik()}", null, "Europe/Copenhagen")).Lokation!;
        var ressource = (await new RessourceService(fixture.CreateContext()).OpretAsync(admin, lokation.Id, "Hoveddør")).Ressource!;

        var db = fixture.CreateContext();
        var tokens = new TTLockTokenService(db, klient, Beskytter, Laase, tid);
        var forbindelse = (await new TTLockForbindelseService(db, klient, tokens, Beskytter, Opt)
            .OpretAsync(admin, org, "Konto", "lukas@example.com", "MinKode123")).Forbindelse!;

        if (kobl)
        {
            var koblet = await new RessourceService(fixture.CreateContext()).KoblAsync(admin, ressource.Id, forbindelse.Id, lockId);
            Assert.True(koblet.Ok);
        }

        var nummer = await new SmsNummerService(fixture.CreateContext()).OpretAsync(admin, org, "Roshage-nummer", null);
        var noegleord = await new NoegleordService(fixture.CreateContext()).OpretAsync(admin, nummer.SmsNummer!.Id, ressource.Id, "ROSHAGE");
        Assert.True(noegleord.Ok);

        return new Opsaetning(org, lokation.Id, ressource.Id, forbindelse.Id, nummer.SmsNummer.Id, nummer.Token!, lockId);
    }

    private OplaasningService NyService(FalskTTLockKlient klient, TestTid tid)
    {
        var db = fixture.CreateContext();
        var tokens = new TTLockTokenService(db, klient, Beskytter, Laase, tid);
        return new OplaasningService(db, klient, tokens, tid, NullLogger<OplaasningService>.Instance);
    }

    private async Task<List<Oplaasningsforsoeg>> LogAsync(Opsaetning o)
    {
        await using var db = fixture.CreateContext();
        db.SaetTenant(true, null);
        return await db.Forsoeg.AsNoTracking().Where(f => f.SmsNummerId == o.SmsNummerId).OrderBy(f => f.TidspunktUtc).ThenBy(f => f.Id).ToListAsync();
    }

    [Fact]
    public async Task Aabner_doeren_og_logger_forsoeget()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.True(res.TokenKendt);
        Assert.Equal(Udfald.Aabnet, res.Udfald);
        Assert.Equal(1, klient.AabnKald);
        Assert.Equal(o.LockId, klient.SidsteLockId);

        var log = Assert.Single(await LogAsync(o));
        Assert.Equal(Udfald.Aabnet, log.Udfald);
        Assert.Equal("+4512345678", log.Afsender);
        Assert.Equal("Roshage", log.Tekst);
        Assert.Equal("Roshage", log.Noegleord);
        Assert.Equal(o.RessourceId, log.RessourceId);
        Assert.Equal(o.OrganisationId, log.OrganisationId);
        Assert.Null(log.TTLockErrcode);
    }

    [Fact]
    public async Task Ukendt_token_er_ukendt_og_aabner_intet()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        await OpsaetAsync(klient, tid);

        var res = await NyService(klient, tid).HaandterAsync("forkert-token", Raa);

        Assert.False(res.TokenKendt);
        Assert.Equal(0, klient.AabnKald);
    }

    [Fact]
    public async Task Ukendt_ord_afvises_og_logges()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, "4512345678;Aaben sesam");

        Assert.Equal(Udfald.AfvistUkendtOrd, res.Udfald);
        Assert.Equal(0, klient.AabnKald);
        Assert.Equal(Udfald.AfvistUkendtOrd, Assert.Single(await LogAsync(o)).Udfald);
    }

    [Theory]
    [InlineData("Roshage")]
    [InlineData("ROSHAGE")]
    [InlineData("roshage")]
    [InlineData("  RoShAgE.  ")]
    public async Task Noegleord_matches_uden_hensyn_til_store_og_smaa_bogstaver(string tekst)
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, $"4512345678;{tekst}\r\n");

        Assert.Equal(Udfald.Aabnet, res.Udfald);
    }

    [Theory]
    [InlineData("Roshage")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("kun-tekst-uden-nummer;")]
    public async Task Ugyldigt_format_afvises_og_logges_med_den_raa_tekst(string? raa)
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, raa);

        Assert.Equal(Udfald.AfvistUgyldigtFormat, res.Udfald);
        Assert.Equal(0, klient.AabnKald);
        var log = Assert.Single(await LogAsync(o));
        Assert.Equal("ukendt", log.Afsender);
        Assert.Equal(raa ?? string.Empty, log.Tekst);
    }

    [Fact]
    public async Task Ressource_paa_pause_afvises()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);
        await new RessourceService(fixture.CreateContext()).SaetStatusAsync(Platform(), o.RessourceId, RessourceStatus.Pause);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.AfvistRessourcePause, res.Udfald);
        Assert.Equal(0, klient.AabnKald);
        Assert.Equal(o.RessourceId, Assert.Single(await LogAsync(o)).RessourceId);
    }

    [Fact]
    public async Task Deaktiveret_lokation_afvises()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);
        await new LokationService(fixture.CreateContext()).SaetStatusAsync(Platform(), o.LokationId, LokationStatus.Deaktiveret);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.AfvistLokationDeaktiveret, res.Udfald);
        Assert.Equal(0, klient.AabnKald);
    }

    [Fact]
    public async Task Deaktiveret_organisation_afvises()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);
        await new OrganisationService(fixture.CreateContext()).SaetStatusAsync(o.OrganisationId, OrganisationStatus.Deaktiveret);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.AfvistOrganisationDeaktiveret, res.Udfald);
        Assert.Equal(0, klient.AabnKald);
    }

    [Fact]
    public async Task Deaktiveret_sms_nummer_afvises()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);
        await new SmsNummerService(fixture.CreateContext()).SaetStatusAsync(Platform(), o.SmsNummerId, SmsNummerStatus.Deaktiveret);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.AfvistNummerDeaktiveret, res.Udfald);
        Assert.Equal(0, klient.AabnKald);
    }

    [Fact]
    public async Task Deaktiveret_forbindelse_afvises()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);
        var db = fixture.CreateContext();
        var tokens = new TTLockTokenService(db, klient, Beskytter, Laase, tid);
        await new TTLockForbindelseService(db, klient, tokens, Beskytter, Opt)
            .SaetStatusAsync(Platform(), o.ForbindelseId, ForbindelseStatus.Deaktiveret);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.AfvistForbindelseDeaktiveret, res.Udfald);
        Assert.Equal(0, klient.AabnKald);
    }

    [Fact]
    public async Task Ressource_uden_kobling_til_en_laas_afvises()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid, kobl: false);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.AfvistIkkeKonfigureret, res.Udfald);
        Assert.Equal(0, klient.AabnKald);
    }

    [Fact]
    public async Task Deaktiveret_noegleord_afvises_som_ukendt()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);
        var admin = Platform();
        var ord = (await new NoegleordService(fixture.CreateContext()).OversigtAsync(admin, o.SmsNummerId)).Single();
        await new NoegleordService(fixture.CreateContext()).SaetStatusAsync(admin, ord.Id, NoegleordStatus.Deaktiveret);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.AfvistUkendtOrd, res.Udfald);
        Assert.Equal(0, klient.AabnKald);
    }

    [Fact]
    public async Task Gensendt_kald_inden_for_ti_sekunder_aabner_ikke_dobbelt()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);

        var foerste = await NyService(klient, tid).HaandterAsync(o.Token, Raa);
        tid.Nu = tid.Nu.AddSeconds(3);
        var andet = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.Aabnet, foerste.Udfald);
        Assert.Equal(Udfald.AfvistDublet, andet.Udfald);
        Assert.Equal(1, klient.AabnKald);
    }

    [Fact]
    public async Task Samme_besked_igen_efter_dublet_vinduet_aabner_igen()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);

        await NyService(klient, tid).HaandterAsync(o.Token, Raa);
        tid.Nu = tid.Nu.AddSeconds(OplaasningService.DubletVinduSekunder + 5);
        var igen = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.Aabnet, igen.Udfald);
        Assert.Equal(2, klient.AabnKald);
    }

    [Fact]
    public async Task For_mange_forsoeg_fra_samme_afsender_inden_for_et_minut_afvises()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);

        for (var i = 0; i < OplaasningService.MaxForsoegPrMinut; i++)
        {
            tid.Nu = tid.Nu.AddSeconds(1);
            var res = await NyService(klient, tid).HaandterAsync(o.Token, $"4512345678;forkert{i}");
            Assert.Equal(Udfald.AfvistUkendtOrd, res.Udfald);
        }

        tid.Nu = tid.Nu.AddSeconds(1);
        var for_mange = await NyService(klient, tid).HaandterAsync(o.Token, Raa);
        Assert.Equal(Udfald.AfvistForMangeForsoeg, for_mange.Udfald);
        Assert.Equal(0, klient.AabnKald);

        // En anden afsender rammes ikke, og samme afsender kan igen efter et minut.
        var andenAfsender = await NyService(klient, tid).HaandterAsync(o.Token, "4587654321;Roshage");
        Assert.Equal(Udfald.Aabnet, andenAfsender.Udfald);

        tid.Nu = tid.Nu.AddMinutes(2);
        var senere = await NyService(klient, tid).HaandterAsync(o.Token, Raa);
        Assert.Equal(Udfald.Aabnet, senere.Udfald);
    }

    [Fact]
    public async Task Ttlock_fejl_logges_med_fejlkode_og_doeren_regnes_ikke_for_aaben()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);
        klient.AabnSvar.Enqueue(TTLockSvar<bool>.Fejlet(TTLockKoder.IngenGateway));

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.TTLockFejl, res.Udfald);
        var log = Assert.Single(await LogAsync(o));
        Assert.Equal(Udfald.TTLockFejl, log.Udfald);
        Assert.Equal(TTLockKoder.IngenGateway, log.TTLockErrcode);
        Assert.Contains("gateway", log.Fejltekst, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Afvist_token_giver_et_nyt_login_og_et_nyt_forsoeg()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);
        klient.AabnSvar.Enqueue(TTLockSvar<bool>.Fejlet(TTLockKoder.UgyldigtToken));

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.Aabnet, res.Udfald);
        Assert.Equal(2, klient.AabnKald);
        Assert.Equal(1, klient.FornyKald);
        Assert.Equal("fornyet-1", klient.SidsteAccessToken);
    }

    [Fact]
    public async Task Fejl_ved_hentning_af_token_logges_som_ttlock_fejl()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var o = await OpsaetAsync(klient, tid);

        // Uret rykkes frem, så tokenet er udløbet, og både fornyelse og login afvises.
        tid.Nu = tid.Nu.AddDays(100);
        klient.FornySvar = TTLockSvar<TTLockToken>.Fejlet(TTLockKoder.UgyldigtRefreshToken);
        klient.LoginSvar = TTLockSvar<TTLockToken>.Fejlet(TTLockKoder.ForkertLogin);

        var res = await NyService(klient, tid).HaandterAsync(o.Token, Raa);

        Assert.Equal(Udfald.TTLockFejl, res.Udfald);
        Assert.Equal(0, klient.AabnKald);
        Assert.Equal(TTLockKoder.ForkertLogin, Assert.Single(await LogAsync(o)).TTLockErrcode);
    }

    [Fact]
    public async Task To_organisationer_med_samme_noegleord_aabner_hver_sin_doer()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var a = await OpsaetAsync(klient, tid, lockId: 1001);
        var b = await OpsaetAsync(klient, tid, lockId: 2002);

        await NyService(klient, tid).HaandterAsync(a.Token, "4511111111;Roshage");
        Assert.Equal(1001, klient.SidsteLockId);

        await NyService(klient, tid).HaandterAsync(b.Token, "4522222222;Roshage");
        Assert.Equal(2002, klient.SidsteLockId);

        // A's log indeholder kun A's forsøg.
        var logA = Assert.Single(await LogAsync(a));
        Assert.Equal(a.OrganisationId, logA.OrganisationId);
    }

    [Fact]
    public async Task Oplaasningsloggen_er_afgraenset_til_organisationen()
    {
        var klient = new FalskTTLockKlient();
        var tid = new TestTid();
        var a = await OpsaetAsync(klient, tid);
        var b = await OpsaetAsync(klient, tid);
        await NyService(klient, tid).HaandterAsync(a.Token, "4511111111;Roshage");
        await NyService(klient, tid).HaandterAsync(b.Token, "4522222222;Roshage");

        await using var db = fixture.CreateContext();
        db.SaetTenant(false, a.OrganisationId);
        var synlige = await db.Forsoeg.AsNoTracking().ToListAsync();

        Assert.NotEmpty(synlige);
        Assert.All(synlige, f => Assert.Equal(a.OrganisationId, f.OrganisationId));
    }
}
