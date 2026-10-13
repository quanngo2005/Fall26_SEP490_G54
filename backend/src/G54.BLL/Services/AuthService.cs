using System.Security.Cryptography;
using System.Text;
using G54.BLL.Dtos.Auth;
using G54.DAL.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;

namespace G54.BLL.Services;

public sealed class AuthService(
    AuthDbContext dbContext,
    IPasswordEncoder passwordEncoder,
    JwtTokenProvider tokenProvider,
    IDistributedCache cache,
    IAuthSessionStore sessionStore,
    IConfiguration configuration) : IAuthService
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultRefreshTokenLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan RememberedRefreshTokenLifetime = TimeSpan.FromDays(30);
    private readonly int _maxFailedAttempts = configuration.GetValue("Authentication:MaxFailedAttempts", 5);
    private readonly TimeSpan _lockoutDuration = TimeSpan.FromMinutes(
        configuration.GetValue("Authentication:LockoutMinutes", 15));

    public async Task<AuthResult> AuthenticateAsync(
        LoginRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var account = await dbContext.Accounts
            .AsSplitQuery()
            .Include(candidate => candidate.Employee)
            .Include(candidate => candidate.UserRoles)
                .ThenInclude(userRole => userRole.Role)
                    .ThenInclude(role => role.RolePermissions)
                        .ThenInclude(rolePermission => rolePermission.Permission)
            .FirstOrDefaultAsync(
                    candidate => EF.Functions.ILike(candidate.Employee.Email, EscapeLikePattern(email), "\\"),
                cancellationToken);

        if (account is null)
        {
            RecordAudit(null, "LOGIN_FAILED", ipAddress);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AuthResult(null, AuthFailureReason.InvalidCredentials);
        }

        var now = DateTimeOffset.UtcNow;
        if (account.IsLocked)
        {
            if (account.LockedUntil is null || account.LockedUntil > now)
            {
                RecordAudit(account.Id, "LOGIN_BLOCKED", ipAddress);
                await dbContext.SaveChangesAsync(cancellationToken);
                return new AuthResult(null, AuthFailureReason.AccountLocked);
            }

            account.IsLocked = false;
            account.LockedUntil = null;
            account.FailedLoginAttempts = 0;
        }

        if (account.Employee.Status != EmployeeStatus.Active)
        {
            RecordAudit(account.Id, "LOGIN_BLOCKED", ipAddress);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AuthResult(null, AuthFailureReason.InvalidCredentials);
        }

        if (!passwordEncoder.Verify(request.Password, account.PasswordHash))
        {
            account.FailedLoginAttempts++;
            if (account.FailedLoginAttempts >= _maxFailedAttempts)
            {
                account.IsLocked = true;
                account.LockedUntil = now.Add(_lockoutDuration);
            }

            account.UpdatedAt = now;
            RecordAudit(account.Id, "LOGIN_FAILED", ipAddress);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AuthResult(
                null,
                account.IsLocked ? AuthFailureReason.AccountLocked : AuthFailureReason.InvalidCredentials);
        }

        account.FailedLoginAttempts = 0;
        account.LockedUntil = null;
        account.UpdatedAt = now;
        var refreshLifetime = request.RememberMe ? RememberedRefreshTokenLifetime : DefaultRefreshTokenLifetime;
        var issued = IssueTokens(account, request.RememberMe, refreshLifetime);
        var sessionStored = await sessionStore.StoreAsync(
            account.Id,
            Guid.Parse(issued.RefreshToken.Id),
            HashToken(issued.RefreshToken.Value),
            issued.RefreshToken.ExpiresAt,
            cancellationToken);
        if (!sessionStored)
        {
            throw new InvalidOperationException("Could not create an authentication session.");
        }

        RecordAudit(account.Id, "LOGIN_SUCCESS", ipAddress);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResult(issued.Response, null);
    }

    public async Task<AuthResponseDto?> RefreshAsync(
        string refreshToken,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var currentToken = tokenProvider.ValidateToken(refreshToken, "refresh");
        if (currentToken is null)
        {
            return null;
        }

        var account = await dbContext.Accounts
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.Employee)
            .Include(candidate => candidate.UserRoles)
                .ThenInclude(userRole => userRole.Role)
                    .ThenInclude(role => role.RolePermissions)
                        .ThenInclude(rolePermission => rolePermission.Permission)
            .SingleOrDefaultAsync(candidate => candidate.Id == currentToken.AccountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        if (account.Employee.Status != EmployeeStatus.Active)
        {
            var revoked = await sessionStore.RevokeAsync(
                currentToken.AccountId,
                currentToken.TokenId,
                HashToken(refreshToken),
                cancellationToken);
            if (revoked)
            {
                RecordAudit(account.Id, "TOKEN_REFRESH_BLOCKED", ipAddress);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return null;
        }

        var remainingLifetime = currentToken.ExpiresAt - DateTimeOffset.UtcNow;
        if (remainingLifetime <= TimeSpan.Zero)
        {
            return null;
        }

        var issued = IssueTokens(account, currentToken.RememberMe, remainingLifetime);
        var rotated = await sessionStore.RotateAsync(
            currentToken.AccountId,
            currentToken.TokenId,
            HashToken(refreshToken),
            Guid.Parse(issued.RefreshToken.Id),
            HashToken(issued.RefreshToken.Value),
            issued.RefreshToken.ExpiresAt,
            cancellationToken);
        if (!rotated)
        {
            return null;
        }

        RecordAudit(account.Id, "TOKEN_REFRESH", ipAddress);
        await dbContext.SaveChangesAsync(cancellationToken);
        return issued.Response;
    }

    public async Task<bool> LogoutAsync(
        string accessToken,
        string refreshToken,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var access = tokenProvider.ValidateToken(accessToken, "access");
        var refresh = tokenProvider.ValidateToken(refreshToken, "refresh");
        if (access is null || refresh is null || access.AccountId != refresh.AccountId)
        {
            return false;
        }

        var revoked = await sessionStore.RevokeAsync(
                refresh.AccountId,
                refresh.TokenId,
                HashToken(refreshToken),
                cancellationToken);
        if (!revoked)
        {
            return false;
        }

        var accessBlacklistLifetime = access.ExpiresAt - DateTimeOffset.UtcNow;
        if (accessBlacklistLifetime > TimeSpan.Zero)
        {
            await cache.SetStringAsync(
                CreateAccessBlacklistKey(access.TokenId),
                "revoked",
                new DistributedCacheEntryOptions { AbsoluteExpiration = access.ExpiresAt },
                cancellationToken);
        }

        RecordAudit(access.AccountId, "LOGOUT", ipAddress);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static string CreateAccessBlacklistKey(Guid tokenId) => $"auth:revoked:{tokenId}";

    private void RecordAudit(
        Guid? actorId,
        string action,
        string? ipAddress)
    {
        dbContext.AuthAuditLogs.Add(new AuthAuditLog
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            Action = action,
            IpAddress = ipAddress,
        });
    }

    private (AuthResponseDto Response, IssuedToken RefreshToken) IssueTokens(
        Account account,
        bool rememberMe,
        TimeSpan refreshLifetime)
    {
        var roles = account.UserRoles
            .Select(userRole => userRole.Role.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var permissions = account.UserRoles
            .SelectMany(userRole => userRole.Role.RolePermissions)
            .Select(rolePermission => rolePermission.Permission.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var accessToken = tokenProvider.CreateAccessToken(
            account.Id,
            account.Employee.Email,
            account.Employee.FullName,
            roles,
            permissions,
            AccessTokenLifetime);
        var refreshToken = tokenProvider.CreateRefreshToken(
            account.Id,
            account.Employee.Email,
            refreshLifetime,
            rememberMe);
        return (
            new AuthResponseDto(
                accessToken.Value,
                refreshToken.Value,
                accessToken.ExpiresAt,
                refreshToken.ExpiresAt,
                new AuthenticatedUserDto(
                    account.Id,
                    account.Employee.Email,
                    account.Employee.FullName,
                    roles,
                    permissions)),
            refreshToken);
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

}
