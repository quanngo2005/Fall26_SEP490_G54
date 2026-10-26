using G54.BLL.Services;
using G54.DAL.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

/// <summary>Refresh and logout of <see cref="AuthService"/>. Login uses ILike, so it is covered against Postgres in <c>AuthServiceLoginPostgresTests</c>.</summary>
public sealed class AuthServiceSessionTests : IDisposable
{
    private const string Email = "hoa.tran@progest.local";

    private readonly AuthDbContext _db = new(new DbContextOptionsBuilder<AuthDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly RecordingAuthSessionStore _sessions = new();
    private readonly MemoryDistributedCache _cache = new(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly JwtTokenProvider _tokens = AuthTestSupport.CreateTokens();

    public void Dispose() => _db.Dispose();

    private AuthService Service() => new(
        _db,
        new PlainPasswordEncoder(),
        _tokens,
        _cache,
        _sessions,
        AuthTestSupport.Configuration());

    private async Task<Account> SeedAccountAsync(bool isLocked = false)
    {
        var now = DateTimeOffset.UtcNow;
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV010",
            FullName = "Tran Thi Hoa",
            Email = Email,
            Status = EmployeeStatus.Active,
        };
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = "hoa",
            PasswordHash = PlainPasswordEncoder.Hash("Current-Password-1"),
            IsLocked = isLocked,
            LockedUntil = isLocked ? now.AddMinutes(10) : null,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var role = new Role { Id = Guid.NewGuid(), Code = "EMPLOYEE" };
        _db.Employees.Add(employee);
        _db.Accounts.Add(account);
        _db.Roles.Add(role);
        _db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = account.Id, RoleId = role.Id });
        await _db.SaveChangesAsync();
        return account;
    }

    private async Task<string> StoreSessionAsync(Account account, TimeSpan lifetime, bool rememberMe = false)
    {
        var refresh = _tokens.CreateRefreshToken(account.Id, Email, lifetime, rememberMe);
        await _sessions.StoreAsync(
            account.Id,
            Guid.Parse(refresh.Id),
            AuthTestSupport.HashToken(refresh.Value),
            refresh.ExpiresAt,
            CancellationToken.None);
        return refresh.Value;
    }

    [Fact]
    public async Task RefreshAsync_ReturnsNull_ForAMalformedToken()
    {
        Assert.Null(await Service().RefreshAsync("not-a-jwt", null, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_ReturnsNull_WhenAnAccessTokenIsPresentedAsARefreshToken()
    {
        var account = await SeedAccountAsync();
        var access = _tokens.CreateAccessToken(account.Id, Email, "Tran Thi Hoa", ["EMPLOYEE"], [], TimeSpan.FromMinutes(15));

        Assert.Null(await Service().RefreshAsync(access.Value, null, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_RotatesTheSession_AndKeepsTheRememberMeLifetime()
    {
        var account = await SeedAccountAsync();
        var oldRefresh = await StoreSessionAsync(account, TimeSpan.FromHours(1), rememberMe: true);

        var response = await Service().RefreshAsync(oldRefresh, "10.0.0.8", CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotEqual(oldRefresh, response.RefreshToken);
        Assert.Equal(account.Id, response.User.Id);
        Assert.Equal("EMPLOYEE", Assert.Single(response.User.Roles));
        Assert.Equal(1, _sessions.CountFor(account.Id));
        Assert.False(_sessions.Contains(account.Id, _tokens.ValidateToken(oldRefresh, "refresh")!.TokenId));
        var rotated = _tokens.ValidateToken(response.RefreshToken, "refresh");
        Assert.NotNull(rotated);
        Assert.True(rotated.RememberMe);
        Assert.True(_sessions.Contains(account.Id, rotated.TokenId));
        Assert.Contains(await _db.AuthAuditLogs.ToListAsync(), audit => audit.Action == "TOKEN_REFRESH" && audit.IpAddress == "10.0.0.8");
    }

    [Fact]
    public async Task RefreshAsync_ReturnsNull_WhenTheSessionWasAlreadyUsed()
    {
        var account = await SeedAccountAsync();
        var refresh = await StoreSessionAsync(account, TimeSpan.FromHours(1));
        var service = Service();
        Assert.NotNull(await service.RefreshAsync(refresh, null, CancellationToken.None));

        // Replaying the same refresh token must fail because its session has been rotated away.
        Assert.Null(await service.RefreshAsync(refresh, null, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_RevokesTheSessionAndAudits_WhenTheAccountIsLocked()
    {
        var account = await SeedAccountAsync(isLocked: true);
        var refresh = await StoreSessionAsync(account, TimeSpan.FromHours(1));

        var response = await Service().RefreshAsync(refresh, null, CancellationToken.None);

        Assert.Null(response);
        Assert.Equal(0, _sessions.CountFor(account.Id));
        Assert.Contains(await _db.AuthAuditLogs.ToListAsync(), audit => audit.Action == "TOKEN_REFRESH_BLOCKED");
    }

    [Fact]
    public async Task LogoutAsync_ReturnsFalse_WhenAccessAndRefreshBelongToDifferentAccounts()
    {
        var first = await SeedAccountAsync();
        var refresh = await StoreSessionAsync(first, TimeSpan.FromHours(1));
        var access = _tokens.CreateAccessToken(Guid.NewGuid(), Email, "Someone Else", [], [], TimeSpan.FromMinutes(15));

        var loggedOut = await Service().LogoutAsync(access.Value, refresh, null, CancellationToken.None);

        Assert.False(loggedOut);
        Assert.Equal(1, _sessions.CountFor(first.Id));
    }

    [Fact]
    public async Task LogoutAsync_ReturnsFalse_WhenTheSessionIsNotStored()
    {
        var account = await SeedAccountAsync();
        var refresh = _tokens.CreateRefreshToken(account.Id, Email, TimeSpan.FromHours(1), rememberMe: false);
        var access = _tokens.CreateAccessToken(account.Id, Email, "Tran Thi Hoa", [], [], TimeSpan.FromMinutes(15));

        Assert.False(await Service().LogoutAsync(access.Value, refresh.Value, null, CancellationToken.None));
    }

    [Fact]
    public async Task LogoutAsync_RevokesTheSession_BlacklistsTheAccessToken_AndAudits()
    {
        var account = await SeedAccountAsync();
        var refresh = await StoreSessionAsync(account, TimeSpan.FromHours(1));
        var access = _tokens.CreateAccessToken(account.Id, Email, "Tran Thi Hoa", [], [], TimeSpan.FromMinutes(15));

        var loggedOut = await Service().LogoutAsync(access.Value, refresh, "10.0.0.9", CancellationToken.None);

        Assert.True(loggedOut);
        Assert.Equal(0, _sessions.CountFor(account.Id));
        var accessId = Guid.Parse(access.Id);
        Assert.Equal("revoked", await _cache.GetStringAsync(AuthService.CreateAccessBlacklistKey(accessId)));
        Assert.Contains(await _db.AuthAuditLogs.ToListAsync(), audit => audit.Action == "LOGOUT" && audit.IpAddress == "10.0.0.9");
    }

    [Fact]
    public void CreateAccessBlacklistKey_IsScopedToTheTokenId()
    {
        var tokenId = Guid.NewGuid();

        Assert.Equal($"auth:revoked:{tokenId}", AuthService.CreateAccessBlacklistKey(tokenId));
    }
}
