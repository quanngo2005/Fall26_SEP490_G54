using System.Security.Claims;
using G54.Api.Controllers;
using G54.Api.Services;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.Auth;
using G54.BLL.Dtos.Profile;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using AuthAccount = G54.DAL.Authentication.Account;
using AuthContext = G54.DAL.Authentication.AuthDbContext;
using AuthEmployee = G54.DAL.Authentication.Employee;
using AuthEmployeeStatus = G54.DAL.Authentication.EmployeeStatus;
using AuthRole = G54.DAL.Authentication.Role;
using AuthUserRole = G54.DAL.Authentication.UserRole;
using Role = G54.DAL.Entities.Role;

namespace G54.UnitTests;

public sealed class PasswordPolicyTests
{
    private const string Email = "chau.le@progest.local";

    [Fact]
    public void AStrongPassword_IsAccepted() =>
        Assert.Empty(PasswordPolicy.Validate("Correct-Horse-9-Battery", "old-password-1", Email));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ABlankPassword_IsRejected(string? password) =>
        Assert.NotEmpty(PasswordPolicy.Validate(password, "old", Email));

    [Theory]
    [InlineData("Short1aA")]
    [InlineData("abcdefghij1")]
    public void ATooShortPassword_IsRejected(string password) =>
        Assert.Contains(PasswordPolicy.Validate(password, "old", Email), error => error.Contains("12", StringComparison.Ordinal));

    [Fact]
    public void ThePasswordAtExactlyTheMinimumAndMaximumLength_IsAccepted()
    {
        Assert.Empty(PasswordPolicy.Validate("abcdefghij1k", "old", Email));
        Assert.Empty(PasswordPolicy.Validate(new string('a', PasswordPolicy.MaxLength - 1) + "1", "old", Email));
    }

    [Fact]
    public void ATooLongPassword_IsRejected() =>
        Assert.NotEmpty(PasswordPolicy.Validate(new string('a', PasswordPolicy.MaxLength) + "1", "old", Email));

    [Theory]
    [InlineData("onlylettersandmore")]
    [InlineData("1234567890123456")]
    [InlineData("!!!!!!!!!!!!!!!!")]
    public void APasswordWithoutBothALetterAndADigit_IsRejected(string password) =>
        Assert.Contains(
            PasswordPolicy.Validate(password, "old", Email),
            error => error.Contains("chữ cái", StringComparison.Ordinal));

    [Fact]
    public void ThePasswordEqualToTheCurrentOne_IsRejected() =>
        Assert.Contains(
            PasswordPolicy.Validate("Correct-Horse-9-Battery", "Correct-Horse-9-Battery", Email),
            error => error.Contains("khác", StringComparison.Ordinal));

    [Fact]
    public void ThePasswordDifferingOnlyByCaseFromTheCurrentOne_IsStillADifferentPassword() =>
        Assert.Empty(PasswordPolicy.Validate("Correct-Horse-9-Battery", "correct-horse-9-battery", Email));

    [Theory]
    [InlineData("chau.le-Password-2026")]
    [InlineData("My-CHAU.LE-secret-99")]
    public void APasswordContainingTheEmailName_IsRejected(string password) =>
        Assert.Contains(
            PasswordPolicy.Validate(password, "old", Email),
            error => error.Contains("email", StringComparison.Ordinal));

    [Fact]
    public void AVeryShortEmailName_DoesNotRejectOtherwiseGoodPasswords() =>
        Assert.Empty(PasswordPolicy.Validate("Aa-correct-horse-9-battery", "old", "a@x.test"));

    [Fact]
    public void EveryBrokenRule_IsReportedAtOnce()
    {
        var errors = PasswordPolicy.Validate("short", "short", Email);

        Assert.True(errors.Count >= 3);
    }
}

public sealed class PhoneRulesTests
{
    [Theory]
    [InlineData("0901000004")]
    [InlineData("0123456789")]
    [InlineData("0000000000")]
    public void TenDigitsStartingWithZero_AreAccepted(string phone) => Assert.True(PhoneRules.IsValid(phone));

    [Theory]
    [InlineData("+84901000004")]
    [InlineData("+84 901 000 004")]
    [InlineData("(028) 3822-1234")]
    [InlineData("0901 000 004")]
    [InlineData("090-100-0004")]
    [InlineData("1901000004")]
    [InlineData("090100000")]
    [InlineData("09010000045")]
    [InlineData("0901000004\n")]
    [InlineData("0901000004; DROP TABLE")]
    [InlineData("090100000a")]
    [InlineData("<script>")]
    [InlineData("")]
    public void AnythingElse_IsRejected(string phone) => Assert.False(PhoneRules.IsValid(phone));

    [Fact]
    public void DigitsOutsideAscii_AreRejected() => Assert.False(PhoneRules.IsValid("0\u0969\u0969\u0969\u0969\u0969\u0969\u0969\u0969"));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  0901000004 ", "0901000004")]
    public void Normalize_TrimsAndTurnsBlankIntoNull(string? input, string? expected) =>
        Assert.Equal(expected, PhoneRules.Normalize(input));
}

public sealed class ProfileServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public void Dispose() => _db.Dispose();

    private ProfileService Service => new(_db, NullLogger<ProfileService>.Instance);

    private async Task<Account> SeedAsync(
        string? phone = "0901000004",
        EmployeeStatus status = EmployeeStatus.Active,
        bool locked = false)
    {
        var now = DateTimeOffset.UtcNow;
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV004",
            FullName = "Lê Minh Châu",
            Email = "chau.le@progest.local",
            Phone = phone,
            DepartmentId = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            Status = status,
            LastSyncAt = now.AddDays(-30),
            UpdatedAt = now.AddDays(-30),
        };
        var account = new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = "chau", PasswordHash = "hash", IsLocked = locked, LastSyncAt = now, UpdatedAt = now };
        _db.AddRange(employee, account);
        await _db.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task UpdatePhone_ReturnsTheNewNumber()
    {
        var account = await SeedAsync();

        var result = await Service.UpdatePhoneAsync(account.Id, new UpdateMyProfileDto("0901000999"));

        Assert.Equal("0901000999", result.Phone);
    }

    [Fact]
    public async Task UpdatePhone_OfAnUnknownAccount_IsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service.UpdatePhoneAsync(Guid.NewGuid(), new UpdateMyProfileDto("0901000999")));

    [Fact]
    public async Task UpdatePhone_OfAnInactiveEmployee_IsForbidden()
    {
        var account = await SeedAsync(status: EmployeeStatus.Inactive);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service.UpdatePhoneAsync(account.Id, new UpdateMyProfileDto("0901000999")));
    }

    [Fact]
    public async Task UpdatePhone_TrimsAndStoresTheNumber_WithoutTouchingTheHrSyncTimestamp()
    {
        var account = await SeedAsync();
        var before = await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == account.EmployeeId);

        var profile = await Service.UpdatePhoneAsync(account.Id, new UpdateMyProfileDto("  0901000888 "));

        Assert.Equal("0901000888", profile.Phone);
        var after = await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == account.EmployeeId);
        Assert.Equal("0901000888", after.Phone);
        Assert.Equal(before.LastSyncAt, after.LastSyncAt);
        Assert.True(after.UpdatedAt > before.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdatePhone_WithABlankValue_ClearsTheNumber(string? blank)
    {
        var account = await SeedAsync();

        var profile = await Service.UpdatePhoneAsync(account.Id, new UpdateMyProfileDto(blank));

        Assert.Null(profile.Phone);
        Assert.Null((await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == account.EmployeeId)).Phone);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("not a phone")]
    [InlineData("0901000004 <b>")]
    [InlineData("+84901000999")]
    [InlineData("090100099")]
    [InlineData("1901000999")]
    [InlineData("0901 000 999")]
    public async Task UpdatePhone_WithAnInvalidNumber_IsRejectedAndNothingChanges(string phone)
    {
        var account = await SeedAsync();

        await Assert.ThrowsAsync<ValidationException>(() => Service.UpdatePhoneAsync(account.Id, new UpdateMyProfileDto(phone)));

        Assert.Equal("0901000004", (await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == account.EmployeeId)).Phone);
    }

    [Fact]
    public async Task UpdatePhone_WithTheSameNumber_DoesNotBumpTheTimestamp()
    {
        var account = await SeedAsync();
        var before = await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == account.EmployeeId);

        await Service.UpdatePhoneAsync(account.Id, new UpdateMyProfileDto("0901000004"));

        Assert.Equal(before.UpdatedAt, (await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == account.EmployeeId)).UpdatedAt);
    }

    [Fact]
    public async Task UpdatePhone_OfALockedAccount_IsForbidden()
    {
        var account = await SeedAsync(locked: true);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service.UpdatePhoneAsync(account.Id, new UpdateMyProfileDto("0901000999")));
    }

    [Fact]
    public async Task UpdatePhone_NeverChangesAnyOtherField()
    {
        var account = await SeedAsync();
        var before = await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == account.EmployeeId);

        await Service.UpdatePhoneAsync(account.Id, new UpdateMyProfileDto("0901000999"));

        var after = await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == account.EmployeeId);
        Assert.Equal(before.FullName, after.FullName);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.DepartmentId, after.DepartmentId);
        Assert.Equal(before.JobPositionId, after.JobPositionId);
        Assert.Equal(before.ManagerId, after.ManagerId);
        Assert.Equal(before.Status, after.Status);
    }
}

public sealed class ChangePasswordTests : IDisposable
{
    private const string CurrentPassword = "Current-Password-1";
    private const string NewPassword = "Brand-New-Password-2";

    private readonly AuthContext _db = new(new DbContextOptionsBuilder<AuthContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly SessionStore _sessions = new();
    private readonly JwtTokenProvider _tokens = new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "G54.Tests",
                ["Jwt:Audience"] = "G54.Tests.Client",
                ["Jwt:Key"] = "test-signing-key-that-is-longer-than-thirty-two-characters",
            })
            .Build());

    public void Dispose() => _db.Dispose();

    private AuthService Service(int maxFailedAttempts = 5) => new(
        _db,
        new PlainEncoder(),
        _tokens,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        _sessions,
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:MaxFailedAttempts"] = maxFailedAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Authentication:LockoutMinutes"] = "15",
            })
            .Build());

    private async Task<(AuthAccount Account, string RefreshToken)> SeedAsync(
        AuthEmployeeStatus status = AuthEmployeeStatus.Active,
        bool rememberMe = false,
        TimeSpan? refreshLifetime = null)
    {
        var employee = new AuthEmployee { Id = Guid.NewGuid(), EmployeeCode = "NV004", FullName = "Lê Minh Châu", Email = "chau.le@progest.local", Status = status };
        var account = new AuthAccount { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = "chau", PasswordHash = PlainEncoder.Hash(CurrentPassword), UpdatedAt = DateTimeOffset.UtcNow, LastSyncAt = DateTimeOffset.UtcNow };
        var role = new AuthRole { Id = Guid.NewGuid(), Code = "EMPLOYEE" };
        _db.Employees.Add(employee);
        _db.Accounts.Add(account);
        _db.Roles.Add(role);
        _db.UserRoles.Add(new AuthUserRole { Id = Guid.NewGuid(), AccountId = account.Id, RoleId = role.Id });
        await _db.SaveChangesAsync();

        var refresh = _tokens.CreateRefreshToken(account.Id, employee.Email, refreshLifetime ?? TimeSpan.FromHours(12), rememberMe);
        await _sessions.StoreAsync(account.Id, Guid.Parse(refresh.Id), "hash-of-" + refresh.Id, refresh.ExpiresAt, CancellationToken.None);
        return (account, refresh.Value);
    }

    private static ChangePasswordRequestDto Request(string refreshToken, string current = CurrentPassword, string next = NewPassword) =>
        new(current, next, refreshToken);

    private async Task<AuthAccount> Reload(Guid id) => await _db.Accounts.AsNoTracking().SingleAsync(a => a.Id == id);

    [Fact]
    public async Task Success_ChangesThePasswordRevokesEverySessionAndReturnsAWorkingNewSession()
    {
        var (account, refresh) = await SeedAsync();
        var otherDevice = _tokens.CreateRefreshToken(account.Id, "chau.le@progest.local", TimeSpan.FromHours(12), false);
        await _sessions.StoreAsync(account.Id, Guid.Parse(otherDevice.Id), "other", otherDevice.ExpiresAt, CancellationToken.None);
        Assert.Equal(2, _sessions.CountFor(account.Id));

        var response = await Service().ChangePasswordAsync(account.Id, Request(refresh), "10.0.0.1", CancellationToken.None);

        Assert.Equal(PlainEncoder.Hash(NewPassword), (await Reload(account.Id)).PasswordHash);
        Assert.Equal(1, _sessions.CountFor(account.Id));
        Assert.False(_sessions.Has(account.Id, Guid.Parse(otherDevice.Id)));
        var newRefresh = _tokens.ValidateToken(response.RefreshToken, "refresh");
        Assert.NotNull(newRefresh);
        Assert.True(_sessions.Has(account.Id, newRefresh.TokenId));
        Assert.NotNull(_tokens.ValidateToken(response.AccessToken, "access"));
        Assert.Equal("chau.le@progest.local", response.User.Email);
        Assert.Contains("EMPLOYEE", response.User.Roles);
    }

    [Fact]
    public async Task Success_KeepsTheRememberMeChoiceAndNeverExtendsTheSession()
    {
        var (account, refresh) = await SeedAsync(rememberMe: true, refreshLifetime: TimeSpan.FromDays(10));
        var original = _tokens.ValidateToken(refresh, "refresh")!;

        var response = await Service().ChangePasswordAsync(account.Id, Request(refresh), null, CancellationToken.None);

        var renewed = _tokens.ValidateToken(response.RefreshToken, "refresh")!;
        Assert.True(renewed.RememberMe);
        Assert.True(response.RefreshTokenExpiresAt <= original.ExpiresAt.AddSeconds(2));
    }

    [Fact]
    public async Task Success_ResetsTheFailedAttemptCounterAndRecordsAnAuditEntry()
    {
        var (account, refresh) = await SeedAsync();
        var tracked = await _db.Accounts.SingleAsync(a => a.Id == account.Id);
        tracked.FailedLoginAttempts = 3;
        await _db.SaveChangesAsync();

        await Service().ChangePasswordAsync(account.Id, Request(refresh), "10.0.0.1", CancellationToken.None);

        Assert.Equal(0, (await Reload(account.Id)).FailedLoginAttempts);
        var audit = Assert.Single(_db.AuthAuditLogs.Where(a => a.Action == "PASSWORD_CHANGED"));
        Assert.Equal(account.Id, audit.ActorId);
        Assert.Equal("10.0.0.1", audit.IpAddress);
    }

    [Fact]
    public async Task AWrongCurrentPassword_Is400WithItsOwnErrorCode_NotA401()
    {
        var (account, refresh) = await SeedAsync();

        var thrown = await Assert.ThrowsAsync<AppException>(() =>
            Service().ChangePasswordAsync(account.Id, Request(refresh, current: "wrong"), null, CancellationToken.None));

        Assert.Equal(ErrorCodes.InvalidCurrentPassword, thrown.ErrorCode);
        Assert.Equal(400, thrown.StatusCode);
        Assert.Equal(PlainEncoder.Hash(CurrentPassword), (await Reload(account.Id)).PasswordHash);
        Assert.Equal(1, _sessions.CountFor(account.Id));
        Assert.Equal(1, (await Reload(account.Id)).FailedLoginAttempts);
        Assert.Single(_db.AuthAuditLogs.Where(a => a.Action == "PASSWORD_CHANGE_FAILED"));
    }

    [Fact]
    public async Task WrongCurrentPasswordsAtTheThreshold_LockTheAccountAndAnswer403()
    {
        var (account, refresh) = await SeedAsync();
        var service = Service(maxFailedAttempts: 3);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var wrong = await Assert.ThrowsAsync<AppException>(() =>
                service.ChangePasswordAsync(account.Id, Request(refresh, current: "wrong"), null, CancellationToken.None));
            Assert.Equal(400, wrong.StatusCode);
        }

        var locking = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ChangePasswordAsync(account.Id, Request(refresh, current: "wrong"), null, CancellationToken.None));
        Assert.Equal(403, locking.StatusCode);
        var locked = await Reload(account.Id);
        Assert.True(locked.IsLocked);
        Assert.True(locked.LockedUntil > DateTimeOffset.UtcNow.AddMinutes(14));
        // Even the right password is refused while the account is locked.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ChangePasswordAsync(account.Id, Request(refresh), null, CancellationToken.None));
        Assert.Equal(PlainEncoder.Hash(CurrentPassword), (await Reload(account.Id)).PasswordHash);
    }

    [Fact]
    public async Task AnExpiredLock_IsClearedAndTheChangeProceeds()
    {
        var (account, refresh) = await SeedAsync();
        var tracked = await _db.Accounts.SingleAsync(a => a.Id == account.Id);
        tracked.IsLocked = true;
        tracked.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
        tracked.FailedLoginAttempts = 5;
        await _db.SaveChangesAsync();

        await Service().ChangePasswordAsync(account.Id, Request(refresh), null, CancellationToken.None);

        var after = await Reload(account.Id);
        Assert.False(after.IsLocked);
        Assert.Equal(PlainEncoder.Hash(NewPassword), after.PasswordHash);
    }

    [Theory]
    [InlineData("short1A")]
    [InlineData("nodigitsatallhere")]
    [InlineData("chau.le-Password-2026")]
    public async Task ANewPasswordBreakingThePolicy_Is400AndChangesNothing(string weak)
    {
        var (account, refresh) = await SeedAsync();

        var thrown = await Assert.ThrowsAsync<ValidationException>(() =>
            Service().ChangePasswordAsync(account.Id, Request(refresh, next: weak), null, CancellationToken.None));

        Assert.Equal(ErrorCodes.ValidationError, thrown.ErrorCode);
        Assert.NotEmpty(thrown.Errors!);
        Assert.Equal(PlainEncoder.Hash(CurrentPassword), (await Reload(account.Id)).PasswordHash);
        Assert.Equal(1, _sessions.CountFor(account.Id));
    }

    [Fact]
    public async Task TheSamePasswordAsBefore_IsRejected()
    {
        var (account, refresh) = await SeedAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().ChangePasswordAsync(account.Id, Request(refresh, next: CurrentPassword), null, CancellationToken.None));
    }

    [Fact]
    public async Task ARefreshTokenOfAnotherAccount_IsUnauthorized_AndTheCurrentPasswordIsNotChecked()
    {
        var (account, _) = await SeedAsync();
        var foreign = _tokens.CreateRefreshToken(Guid.NewGuid(), "someone@x.test", TimeSpan.FromHours(1), false);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Service().ChangePasswordAsync(account.Id, Request(foreign.Value, current: "wrong"), null, CancellationToken.None));

        Assert.Equal(0, (await Reload(account.Id)).FailedLoginAttempts);
    }

    [Theory]
    [InlineData("garbage")]
    public async Task AGarbageRefreshToken_IsUnauthorized(string token)
    {
        var (account, _) = await SeedAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Service().ChangePasswordAsync(account.Id, Request(token), null, CancellationToken.None));
    }

    [Fact]
    public async Task AnAccessTokenUsedAsTheRefreshToken_IsUnauthorized()
    {
        var (account, _) = await SeedAsync();
        var access = _tokens.CreateAccessToken(account.Id, "chau.le@progest.local", "Châu", [], [], TimeSpan.FromMinutes(5));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Service().ChangePasswordAsync(account.Id, Request(access.Value), null, CancellationToken.None));
    }

    [Fact]
    public async Task AnInactiveEmployee_IsForbidden()
    {
        var (account, refresh) = await SeedAsync(status: AuthEmployeeStatus.Inactive);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service().ChangePasswordAsync(account.Id, Request(refresh), null, CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownAccount_IsUnauthorized()
    {
        var (_, refresh) = await SeedAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Service().ChangePasswordAsync(Guid.NewGuid(), Request(refresh), null, CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheNewSessionCannotBeStored_TheCallFails()
    {
        var (account, refresh) = await SeedAsync();
        _sessions.FailStore = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().ChangePasswordAsync(account.Id, Request(refresh), null, CancellationToken.None));
    }

    /// <summary>Deterministic stand-in for PBKDF2 so the tests stay fast; <see cref="PasswordEncoder"/> has its own tests.</summary>
    private sealed class PlainEncoder : IPasswordEncoder
    {
        public static string Hash(string raw) => "plain$" + raw;

        public string Encode(string rawPassword) => Hash(rawPassword);

        public bool Verify(string rawPassword, string encodedPassword) => encodedPassword == Hash(rawPassword);
    }

    private sealed class SessionStore : IAuthSessionStore
    {
        private readonly Dictionary<(Guid Account, Guid Token), string> _items = [];

        public bool FailStore { get; set; }

        public int CountFor(Guid accountId) => _items.Keys.Count(key => key.Account == accountId);

        public bool Has(Guid accountId, Guid tokenId) => _items.ContainsKey((accountId, tokenId));

        public Task<bool> StoreAsync(Guid accountId, Guid refreshTokenId, string refreshTokenHash, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            if (FailStore)
            {
                return Task.FromResult(false);
            }

            _items[(accountId, refreshTokenId)] = refreshTokenHash;
            return Task.FromResult(true);
        }

        public Task<bool> RotateAsync(Guid accountId, Guid currentTokenId, string currentTokenHash, Guid nextTokenId, string nextTokenHash, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> RevokeAsync(Guid accountId, Guid refreshTokenId, string refreshTokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(_items.Remove((accountId, refreshTokenId)));

        public Task<int> RevokeAllAsync(Guid accountId, CancellationToken cancellationToken)
        {
            var keys = _items.Keys.Where(key => key.Account == accountId).ToList();
            keys.ForEach(key => _items.Remove(key));
            return Task.FromResult(keys.Count);
        }
    }
}

public sealed class MyProfileControllerTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private static MyProfileController ControllerFor(StubProfiles profiles, StubAuth auth) => new(profiles, auth)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", AccountId.ToString())], "TestAuth")),
            },
        },
    };


    [Fact]
    public async Task UpdateProfile_PassesThePhoneThroughAndReturnsTheUpdatedProfile()
    {
        var profiles = new StubProfiles(new MyPhoneDto("0901000999"));

        var result = await ControllerFor(profiles, new StubAuth()).UpdateProfile(new UpdateMyProfileDto("0901000999"), CancellationToken.None);

        var response = Assert.IsType<ApiResponse<MyPhoneDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("0901000999", response.Data!.Phone);
        Assert.Equal("0901000999", profiles.LastPhone);
    }

    [Fact]
    public async Task ChangePassword_ReturnsTheNewTokensWithNoStoreHeaders_UsingTheSignedInAccount()
    {
        var auth = new StubAuth();
        var controller = ControllerFor(new StubProfiles(new MyPhoneDto(null)), auth);

        var result = await controller.ChangePassword(new ChangePasswordRequestDto("old", "new-password-123", "refresh"), CancellationToken.None);

        var body = Assert.IsType<AuthResponseDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("new-access", body.AccessToken);
        Assert.Equal(AccountId, auth.LastAccountId);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", controller.Response.Headers.Pragma.ToString());
    }

    [Fact]
    public async Task ChangePassword_LetsServiceErrorsReachTheGlobalHandler()
    {
        var auth = new StubAuth { Failure = new AppException("nope", ErrorCodes.InvalidCurrentPassword, 400) };

        var thrown = await Assert.ThrowsAsync<AppException>(() =>
            ControllerFor(new StubProfiles(new MyPhoneDto(null)), auth).ChangePassword(new ChangePasswordRequestDto("o", "n", "r"), CancellationToken.None));

        Assert.Equal(ErrorCodes.InvalidCurrentPassword, thrown.ErrorCode);
    }

    [Fact]
    public void TheRoutesFollowTheContract()
    {
        var type = typeof(MyProfileController);
        Assert.Equal("me", type.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Single().Template);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false));
        Assert.Equal("profile", Template(nameof(MyProfileController.UpdateProfile)));
        Assert.Equal("password", Template(nameof(MyProfileController.ChangePassword)));

        static string? Template(string method) =>
            typeof(MyProfileController).GetMethod(method)!
                .GetCustomAttributes(typeof(HttpMethodAttribute), false).Cast<HttpMethodAttribute>().Single().Template;
    }

    private sealed class StubProfiles(MyPhoneDto profile) : IProfileService
    {
        public Guid? LastAccountId { get; private set; }

        public string? LastPhone { get; private set; }

        public Task<MyPhoneDto> UpdatePhoneAsync(Guid accountId, UpdateMyProfileDto request, CancellationToken cancellationToken = default)
        {
            LastAccountId = accountId;
            LastPhone = request.Phone;
            return Task.FromResult(profile);
        }
    }

    private sealed class StubAuth : IAuthService
    {
        public Guid? LastAccountId { get; private set; }

        public AppException? Failure { get; init; }

        public Task<AuthResponseDto> ChangePasswordAsync(Guid accountId, ChangePasswordRequestDto request, string? ipAddress, CancellationToken cancellationToken)
        {
            LastAccountId = accountId;
            return Failure is null
                ? Task.FromResult(new AuthResponseDto("new-access", "new-refresh", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new AuthenticatedUserDto(accountId, "e@x.test", "E", [], [])))
                : Task.FromException<AuthResponseDto>(Failure);
        }

        public Task<AuthResult> AuthenticateAsync(LoginRequestDto request, string? ipAddress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AuthResponseDto?> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> LogoutAsync(string accessToken, string refreshToken, string? ipAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

/// <summary>Skips when no Redis is available (see <see cref="TestRedis"/>); under <c>CI=true</c> a missing Redis fails instead.</summary>
public sealed class RedisSessionFactAttribute : FactAttribute
{
    public RedisSessionFactAttribute()
    {
        if (TestRedis.ConnectionString is null)
        {
            Skip = TestRedis.SkipReason;
        }
    }
}

public sealed class RedisRevokeAllTests : IAsyncLifetime
{
    private ConnectionMultiplexer? _connection;

    public async Task InitializeAsync()
    {
        var configuration = TestRedis.ConnectionString;
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            _connection = await ConnectionMultiplexer.ConnectAsync(configuration + ",abortConnect=false");
        }
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    [RedisSessionFact]
    public async Task RevokeAll_RemovesEverySessionOfTheAccountAndLeavesOtherAccountsAlone()
    {
        var store = new RedisAuthSessionStore(_connection!);
        var (mine, someoneElse) = (Guid.NewGuid(), Guid.NewGuid());
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        var (first, second, foreign) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await store.StoreAsync(mine, first, "h1", expiry, CancellationToken.None);
        await store.StoreAsync(mine, second, "h2", expiry, CancellationToken.None);
        await store.StoreAsync(someoneElse, foreign, "h3", expiry, CancellationToken.None);

        var removed = await store.RevokeAllAsync(mine, CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.False(await store.RevokeAsync(mine, first, "h1", CancellationToken.None));
        Assert.False(await store.RevokeAsync(mine, second, "h2", CancellationToken.None));
        Assert.True(await store.RevokeAsync(someoneElse, foreign, "h3", CancellationToken.None));
    }

    [RedisSessionFact]
    public async Task RevokeAll_ForAnAccountWithoutSessions_RemovesNothing()
    {
        var removed = await new RedisAuthSessionStore(_connection!).RevokeAllAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, removed);
    }

    [RedisSessionFact]
    public async Task RevokeAll_ThenStore_LeavesExactlyTheNewSession()
    {
        var store = new RedisAuthSessionStore(_connection!);
        var account = Guid.NewGuid();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        await store.StoreAsync(account, Guid.NewGuid(), "old", expiry, CancellationToken.None);
        var survivor = Guid.NewGuid();

        await store.RevokeAllAsync(account, CancellationToken.None);
        await store.StoreAsync(account, survivor, "new", expiry, CancellationToken.None);

        Assert.True(await store.RevokeAsync(account, survivor, "new", CancellationToken.None));
    }
}
