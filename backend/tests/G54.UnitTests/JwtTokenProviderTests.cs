using G54.BLL.Services;
using Microsoft.Extensions.Configuration;

namespace G54.UnitTests;

public sealed class JwtTokenProviderTests
{
    private readonly JwtTokenProvider _provider = new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "G54.Tests",
                ["Jwt:Audience"] = "G54.Tests.Client",
                ["Jwt:Key"] = "test-signing-key-that-is-longer-than-thirty-two-characters",
            })
            .Build());

    [Fact]
    public void ValidateToken_OnlyAcceptsExpectedTokenType()
    {
        var accountId = Guid.NewGuid();
        var accessToken = _provider.CreateAccessToken(
            accountId,
            "person@example.test",
            "Example Person",
            ["Manager"],
            ["work.read"],
            TimeSpan.FromMinutes(15));
        var refreshToken = _provider.CreateRefreshToken(
            accountId,
            "person@example.test",
            TimeSpan.FromHours(1),
            rememberMe: true);

        var validatedAccess = _provider.ValidateToken(accessToken.Value, "access");

        Assert.NotNull(validatedAccess);
        Assert.Equal(accountId, validatedAccess.AccountId);
        Assert.Null(_provider.ValidateToken(accessToken.Value, "refresh"));
        var validatedRefresh = _provider.ValidateToken(refreshToken.Value, "refresh");
        Assert.NotNull(validatedRefresh);
        Assert.Equal(validatedRefresh.TokenId.ToString(), refreshToken.Id);
        Assert.True(validatedRefresh.RememberMe);
        Assert.Null(_provider.ValidateToken("malformed", "access"));
    }
}
