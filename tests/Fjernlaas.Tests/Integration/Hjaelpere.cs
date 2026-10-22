using Fjernlaas.Web.Services;

namespace Fjernlaas.Tests.Integration;

/// <summary>Styrbart ur til tests.</summary>
public sealed class TestTid : TimeProvider
{
    public DateTimeOffset Nu { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Nu;
}

/// <summary>Falsk TTLock, så testene ikke kalder det rigtige API. Tæller kald og kan få svar sat.</summary>
public sealed class FalskTTLockKlient : ITTLockKlient
{
    public TTLockSvar<TTLockToken>? LoginSvar { get; set; }

    public TTLockSvar<TTLockToken>? FornySvar { get; set; }

    public TTLockSvar<IReadOnlyList<TTLockLaas>> LaaseSvar { get; set; } =
        TTLockSvar<IReadOnlyList<TTLockLaas>>.Succes([new TTLockLaas(111, "M201", "Hoveddør", 90, true)]);

    /// <summary>Svar på de næste oplåsninger i rækkefølge. Når køen er tom, lykkes oplåsningen.</summary>
    public Queue<TTLockSvar<bool>> AabnSvar { get; } = new();

    public int LoginKald { get; private set; }

    public int FornyKald { get; private set; }

    public int AabnKald { get; private set; }

    public long? SidsteLockId { get; private set; }

    public string? SidsteAccessToken { get; private set; }

    public string? SidsteLoginBrugernavn { get; private set; }

    public string? SidsteLoginHash { get; private set; }

    public Task<TTLockSvar<TTLockToken>> HentTokenAsync(string brugernavn, string adgangskodeHash, CancellationToken ct = default)
    {
        LoginKald++;
        SidsteLoginBrugernavn = brugernavn;
        SidsteLoginHash = adgangskodeHash;
        return Task.FromResult(LoginSvar ?? TTLockSvar<TTLockToken>.Succes(
            new TTLockToken($"access-{LoginKald}", $"refresh-{LoginKald}", DateTime.UtcNow.AddDays(56))));
    }

    public Task<TTLockSvar<TTLockToken>> FornyTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        FornyKald++;
        return Task.FromResult(FornySvar ?? TTLockSvar<TTLockToken>.Succes(
            new TTLockToken($"fornyet-{FornyKald}", $"fornyet-refresh-{FornyKald}", DateTime.UtcNow.AddDays(56))));
    }

    public Task<TTLockSvar<IReadOnlyList<TTLockLaas>>> ListLaaseAsync(string accessToken, CancellationToken ct = default) =>
        Task.FromResult(LaaseSvar);

    public Task<TTLockSvar<bool>> AabnAsync(string accessToken, long lockId, CancellationToken ct = default)
    {
        AabnKald++;
        SidsteAccessToken = accessToken;
        SidsteLockId = lockId;
        return Task.FromResult(AabnSvar.Count > 0 ? AabnSvar.Dequeue() : TTLockSvar<bool>.Succes(true));
    }
}
