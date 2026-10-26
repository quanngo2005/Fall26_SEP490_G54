using G54.BLL.Dtos.Auth;
using G54.BLL.Services;
using G54.DAL.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace G54.UnitTests;

/// <summary>One Postgres database with the auth schema for the whole test class (see <see cref="AuthServiceLoginPostgresTests"/>).</summary>
public sealed class AuthPostgresFixture : IAsyncLifetime
{
    private string? _database;

    public ServiceProvider? Provider { get; private set; }

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_auth_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection();
        services.AddAuthenticationDataAccess(connectionString);
        Provider = services.BuildServiceProvider();
        await using var scope = Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (Provider is null)
        {
            return;
        }

        await Provider.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(TestPostgres.AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Login runs <c>EF.Functions.ILike</c>, which only Npgsql translates, so these tests run against the real schema.
/// Each test gets its own scope and uses unique emails, so the tests can share the class database.
/// Skips when no Postgres is available (see <see cref="TestPostgres"/>).
/// </summary>
public sealed class AuthServiceLoginPostgresTests : IClassFixture<AuthPostgresFixture>, IDisposable
{
    private const string Password = "Current-Password-1";

    private readonly IServiceScope _scope;
    private readonly AuthDbContext _db;
    private readonly RecordingAuthSessionStore _sessions = new();
    private readonly string _email = $"hoa.{Guid.NewGuid():N}@progest.local";

    public AuthServiceLoginPostgresTests(AuthPostgresFixture fixture)
    {
        _scope = fixture.Provider!.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    }

    public void Dispose() => _scope.Dispose();

    [PostgresFact]
    public async Task Authenticate_IssuesTokensAndStoresTheSession_ForAnEmailMatchedIgnoringCase()
    {
        var account = await SeedAsync();

        var result = await Service().AuthenticateAsync(Login(Password, _email.ToUpperInvariant(), rememberMe: true), "10.0.0.7", CancellationToken.None);

        Assert.Null(result.FailureReason);
        Assert.NotNull(result.Response);
        Assert.Equal(account.Id, result.Response.User.Id);
        Assert.Equal("EMPLOYEE", Assert.Single(result.Response.User.Roles));
        Assert.True(result.Response.RefreshTokenExpiresAt - DateTimeOffset.UtcNow > TimeSpan.FromDays(29), "Remember-me sessions last 30 days.");
        Assert.Equal(1, _sessions.CountFor(account.Id));
        Assert.Contains(await _db.AuthAuditLogs.ToListAsync(), audit => audit.ActorId == account.Id && audit.Action == "LOGIN_SUCCESS" && audit.IpAddress == "10.0.0.7");
    }

    [PostgresFact]
    public async Task Authenticate_DefaultSessionLasts12Hours_WhenRememberMeIsOff()
    {
        await SeedAsync();

        var result = await Service().AuthenticateAsync(Login(Password), null, CancellationToken.None);

        Assert.NotNull(result.Response);
        var lifetime = result.Response.RefreshTokenExpiresAt - DateTimeOffset.UtcNow;
        Assert.True(lifetime > TimeSpan.FromHours(11) && lifetime <= TimeSpan.FromHours(12), $"Lifetime was {lifetime}.");
    }

    [PostgresFact]
    public async Task Authenticate_CountsAWrongPassword_WithoutIssuingTokens()
    {
        var account = await SeedAsync();

        var result = await Service().AuthenticateAsync(Login("wrong-password"), null, CancellationToken.None);

        Assert.Null(result.Response);
        Assert.Equal(AuthFailureReason.InvalidCredentials, result.FailureReason);
        var stored = await _db.Accounts.AsNoTracking().SingleAsync(candidate => candidate.Id == account.Id);
        Assert.Equal(1, stored.FailedLoginAttempts);
        Assert.False(stored.IsLocked);
        Assert.Equal(0, _sessions.CountFor(account.Id));
    }

    [PostgresFact]
    public async Task Authenticate_LocksTheAccount_AfterTheConfiguredFailures_AndBlocksTheRightPasswordToo()
    {
        var account = await SeedAsync();
        var service = Service(maxFailedAttempts: 2);

        await service.AuthenticateAsync(Login("wrong-1"), null, CancellationToken.None);
        var locking = await service.AuthenticateAsync(Login("wrong-2"), null, CancellationToken.None);
        var blocked = await service.AuthenticateAsync(Login(Password), "10.0.0.2", CancellationToken.None);

        Assert.Equal(AuthFailureReason.AccountLocked, locking.FailureReason);
        Assert.Equal(AuthFailureReason.AccountLocked, blocked.FailureReason);
        Assert.Null(blocked.Response);
        Assert.Contains(await _db.AuthAuditLogs.ToListAsync(), audit => audit.ActorId == account.Id && audit.Action == "LOGIN_BLOCKED" && audit.IpAddress == "10.0.0.2");
    }

    [PostgresFact]
    public async Task Authenticate_ReleasesAnExpiredLock_WhenTheRightPasswordIsGiven()
    {
        var account = await SeedAsync(isLocked: true, lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await Service().AuthenticateAsync(Login(Password), null, CancellationToken.None);

        Assert.NotNull(result.Response);
        Assert.False(account.IsLocked);
        Assert.Null(account.LockedUntil);
    }

    [PostgresFact]
    public async Task Authenticate_ReturnsInvalidCredentials_ForAnUnknownEmail_AndAuditsWithoutAnActor()
    {
        await SeedAsync();

        var result = await Service().AuthenticateAsync(Login(Password, $"nobody-{Guid.NewGuid():N}@progest.local"), "10.0.0.9", CancellationToken.None);

        Assert.Equal(AuthFailureReason.InvalidCredentials, result.FailureReason);
        Assert.Contains(await _db.AuthAuditLogs.ToListAsync(), audit => audit.Action == "LOGIN_FAILED" && audit.ActorId == null && audit.IpAddress == "10.0.0.9");
    }

    [PostgresFact]
    public async Task Authenticate_RejectsAnInactiveEmployee_EvenWithTheRightPassword()
    {
        var account = await SeedAsync(status: EmployeeStatus.Inactive);

        var result = await Service().AuthenticateAsync(Login(Password), null, CancellationToken.None);

        Assert.Null(result.Response);
        Assert.Equal(AuthFailureReason.InvalidCredentials, result.FailureReason);
        Assert.Equal(0, _sessions.CountFor(account.Id));
        Assert.Contains(await _db.AuthAuditLogs.ToListAsync(), audit => audit.ActorId == account.Id && audit.Action == "LOGIN_BLOCKED");
    }

    [PostgresFact]
    public async Task Authenticate_MatchesTheEmailLiterally_NotAsALikeWildcard()
    {
        var id = Guid.NewGuid().ToString("N");
        await SeedAsync(email: $"hoa_{id}@progest.local");

        var wildcard = await Service().AuthenticateAsync(Login(Password, $"hoax{id}@progest.local"), null, CancellationToken.None);
        var exact = await Service().AuthenticateAsync(Login(Password, $"hoa_{id}@progest.local"), null, CancellationToken.None);

        Assert.Null(wildcard.Response);
        Assert.NotNull(exact.Response);
    }

    private AuthService Service(int maxFailedAttempts = 5) => new(
        _db,
        new PlainPasswordEncoder(),
        AuthTestSupport.CreateTokens(),
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        _sessions,
        AuthTestSupport.Configuration(maxFailedAttempts));

    private LoginRequestDto Login(string password, string? email = null, bool rememberMe = false) =>
        new(email ?? _email, password, rememberMe);

    private async Task<Account> SeedAsync(
        string? email = null,
        EmployeeStatus status = EmployeeStatus.Active,
        bool isLocked = false,
        DateTimeOffset? lockedUntil = null)
    {
        var now = DateTimeOffset.UtcNow;
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = $"NV-{Guid.NewGuid():N}"[..11],
            FullName = "Tran Thi Hoa",
            Email = email ?? _email,
            Status = status,
        };
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = $"user-{Guid.NewGuid():N}",
            PasswordHash = PlainPasswordEncoder.Hash(Password),
            IsLocked = isLocked,
            LockedUntil = lockedUntil,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var role = await _db.Roles.SingleOrDefaultAsync(candidate => candidate.Code == "EMPLOYEE");
        if (role is null)
        {
            role = new Role { Id = Guid.NewGuid(), Code = "EMPLOYEE" };
            _db.Roles.Add(role);
        }

        _db.Employees.Add(employee);
        _db.Accounts.Add(account);
        _db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = account.Id, RoleId = role.Id });
        await _db.SaveChangesAsync();
        return account;
    }
}
