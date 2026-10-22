using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Xunit;

namespace Fjernlaas.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class OplaasningsLogTests(SqlServerFixture fixture)
{
    private static Aktoer Platform() => new(Guid.NewGuid(), null, true, false);
    private static Aktoer OrgAdmin(Guid org) => new(Guid.NewGuid(), org, false, true);
    private static Aktoer Almindelig(Guid org) => new(Guid.NewGuid(), org, false, false);

    private async Task<(Guid Org, Guid Nummer)> OpsaetAsync(int antal, Udfald udfald = Udfald.Aabnet, DateTime? start = null)
    {
        var org = (await new OrganisationService(fixture.CreateContext()).OpretAsync($"Org {Guid.NewGuid():N}")).Organisation!.Id;
        var nummer = (await new SmsNummerService(fixture.CreateContext()).OpretAsync(Platform(), org, "Nr", null)).SmsNummer!.Id;

        await using var db = fixture.CreateContext();
        db.SaetTenant(true, null);
        var t = start ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < antal; i++)
        {
            db.Forsoeg.Add(new Oplaasningsforsoeg
            {
                TidspunktUtc = t.AddMinutes(i), OrganisationId = org, SmsNummerId = nummer,
                Afsender = "+4512345678", Tekst = "x", Udfald = udfald
            });
        }
        await db.SaveChangesAsync();
        return (org, nummer);
    }

    [Fact]
    public async Task Nyeste_foerst_og_med_sider()
    {
        var (org, _) = await OpsaetAsync(5);
        var service = new OplaasningsLogService(fixture.CreateContext());

        var side1 = await service.HentAsync(OrgAdmin(org), null, 1, 2);
        var side3 = await new OplaasningsLogService(fixture.CreateContext()).HentAsync(OrgAdmin(org), null, 3, 2);

        Assert.Equal(5, side1.Total);
        Assert.Equal(3, side1.AntalSider);
        Assert.Equal(2, side1.Raekker.Count);
        Assert.True(side1.Raekker[0].TidspunktUtc > side1.Raekker[1].TidspunktUtc);
        Assert.Single(side3.Raekker);
    }

    [Fact]
    public async Task Filtrerer_paa_udfald_og_dato()
    {
        var (org, nummer) = await OpsaetAsync(3);
        await using (var db = fixture.CreateContext())
        {
            db.SaetTenant(true, null);
            db.Forsoeg.Add(new Oplaasningsforsoeg
            {
                TidspunktUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), OrganisationId = org, SmsNummerId = nummer,
                Afsender = "+4512345678", Tekst = "y", Udfald = Udfald.AfvistUkendtOrd
            });
            await db.SaveChangesAsync();
        }
        var admin = OrgAdmin(org);

        var afvist = await new OplaasningsLogService(fixture.CreateContext()).HentAsync(admin, new LogFilter(Udfald.AfvistUkendtOrd));
        var iFebruar = await new OplaasningsLogService(fixture.CreateContext())
            .HentAsync(admin, new LogFilter(FraUtc: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
        var foerFebruar = await new OplaasningsLogService(fixture.CreateContext())
            .HentAsync(admin, new LogFilter(TilUtc: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(1, afvist.Total);
        Assert.Equal(1, iFebruar.Total);
        Assert.Equal(3, foerFebruar.Total);
    }

    [Fact]
    public async Task Organisationer_ser_kun_deres_egen_log_og_almindelige_brugere_ingen()
    {
        var (org1, _) = await OpsaetAsync(2);
        var (org2, _) = await OpsaetAsync(3);

        var set1 = await new OplaasningsLogService(fixture.CreateContext()).HentAsync(OrgAdmin(org1));
        var alle = await new OplaasningsLogService(fixture.CreateContext()).HentAsync(Platform());

        Assert.Equal(2, set1.Total);
        Assert.All(set1.Raekker, r => Assert.Equal(set1.Raekker[0].OrganisationNavn, r.OrganisationNavn));
        Assert.True(alle.Total >= 5);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => new OplaasningsLogService(fixture.CreateContext()).HentAsync(Almindelig(org2)));
    }
}
