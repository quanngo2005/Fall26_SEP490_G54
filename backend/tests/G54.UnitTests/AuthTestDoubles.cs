using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using G54.BLL.Services;
using Microsoft.Extensions.Configuration;

namespace G54.UnitTests;

/// <summary>Shared configuration and fakes for the authentication tests.</summary>
internal static class AuthTestSupport
{
    public static JwtTokenProvider CreateTokens() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "G54.Tests",
            ["Jwt:Audience"] = "G54.Tests.Client",
            ["Jwt:Key"] = "test-signing-key-that-is-longer-than-thirty-two-characters",
        })
        .Build());

    public static IConfiguration Configuration(int maxFailedAttempts = 5) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:MaxFailedAttempts"] = maxFailedAttempts.ToString(CultureInfo.InvariantCulture),
            ["Authentication:LockoutMinutes"] = "15",
        })
        .Build();

    /// <summary>Same digest the service stores for refresh tokens.</summary>
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

internal sealed class PlainPasswordEncoder : IPasswordEncoder
{
    public static string Hash(string raw) => "plain$" + raw;

    public string Encode(string rawPassword) => Hash(rawPassword);

    public bool Verify(string rawPassword, string encodedPassword) => encodedPassword == Hash(rawPassword);
}

/// <summary>In-memory session store following the Redis contract: rotate and revoke only succeed with the matching hash.</summary>
internal sealed class RecordingAuthSessionStore : IAuthSessionStore
{
    private readonly Dictionary<(Guid Account, Guid Token), string> _items = [];

    public bool Contains(Guid accountId, Guid tokenId) => _items.ContainsKey((accountId, tokenId));

    public int CountFor(Guid accountId) => _items.Keys.Count(key => key.Account == accountId);

    public Task<bool> StoreAsync(Guid accountId, Guid refreshTokenId, string refreshTokenHash, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        _items[(accountId, refreshTokenId)] = refreshTokenHash;
        return Task.FromResult(true);
    }

    public Task<bool> RotateAsync(
        Guid accountId,
        Guid currentTokenId,
        string currentTokenHash,
        Guid nextTokenId,
        string nextTokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        if (!Matches(accountId, currentTokenId, currentTokenHash))
        {
            return Task.FromResult(false);
        }

        _items.Remove((accountId, currentTokenId));
        _items[(accountId, nextTokenId)] = nextTokenHash;
        return Task.FromResult(true);
    }

    public Task<bool> RevokeAsync(Guid accountId, Guid refreshTokenId, string refreshTokenHash, CancellationToken cancellationToken)
    {
        var revoked = Matches(accountId, refreshTokenId, refreshTokenHash) && _items.Remove((accountId, refreshTokenId));
        return Task.FromResult(revoked);
    }

    public Task<int> RevokeAllAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var keys = _items.Keys.Where(key => key.Account == accountId).ToList();
        keys.ForEach(key => _items.Remove(key));
        return Task.FromResult(keys.Count);
    }

    private bool Matches(Guid accountId, Guid tokenId, string hash) =>
        _items.TryGetValue((accountId, tokenId), out var stored) && stored == hash;
}
