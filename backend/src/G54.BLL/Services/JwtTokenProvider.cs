using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace G54.BLL.Services;

public sealed class JwtTokenProvider(IConfiguration configuration)
{
    private const string TokenTypeClaim = "token_type";
    private const string RememberMeClaim = "remember_me";
    private readonly string _issuer = configuration["Jwt:Issuer"]
        ?? throw new InvalidOperationException("Jwt:Issuer is required.");
    private readonly string _audience = configuration["Jwt:Audience"]
        ?? throw new InvalidOperationException("Jwt:Audience is required.");
    private readonly string _key = configuration["Jwt:Key"]
        ?? throw new InvalidOperationException("Jwt:Key is required.");

    public IssuedToken CreateAccessToken(
        Guid accountId,
        string email,
        string fullName,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        TimeSpan lifetime)
    {
        var tokenId = Guid.NewGuid().ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, accountId.ToString()),
            new(JwtRegisteredClaimNames.Jti, tokenId),
            new(JwtRegisteredClaimNames.Email, email),
            new("name", fullName),
            new(TokenTypeClaim, "access"),
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));
        claims.AddRange(permissions.Select(permission => new Claim("scope", permission)));
        return CreateToken(claims, lifetime, tokenId);
    }

    public IssuedToken CreateRefreshToken(Guid accountId, string email, TimeSpan lifetime, bool rememberMe)
    {
        var tokenId = Guid.NewGuid().ToString();
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, tokenId),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(TokenTypeClaim, "refresh"),
            new Claim(RememberMeClaim, rememberMe.ToString()),
        };
        return CreateToken(claims, lifetime, tokenId);
    }

    public ValidatedToken? ValidateToken(string token, string expectedTokenType)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = handler.ValidateToken(token, CreateValidationParameters(), out _);
            var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var tokenType = principal.FindFirstValue(TokenTypeClaim);
            var expirationValue = principal.FindFirstValue(JwtRegisteredClaimNames.Exp);
            var rememberMeClaim = principal.FindFirstValue(RememberMeClaim);
            if (!Guid.TryParse(subject, out var accountId)
                || !Guid.TryParse(tokenId, out var jti)
                || tokenType != expectedTokenType
                || !long.TryParse(expirationValue, out var expiration)
                || (expectedTokenType == "refresh" && !bool.TryParse(rememberMeClaim, out _)))
            {
                return null;
            }

            var rememberMe = bool.TryParse(rememberMeClaim, out var remember) && remember;
            return new ValidatedToken(accountId, jti, DateTimeOffset.FromUnixTimeSeconds(expiration), rememberMe);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = _issuer,
        ValidAudience = _audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key)),
        ClockSkew = TimeSpan.Zero,
        NameClaimType = JwtRegisteredClaimNames.Email,
        RoleClaimType = "role",
    };

    private IssuedToken CreateToken(IEnumerable<Claim> claims, TimeSpan lifetime, string tokenId)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _issuer,
            _audience,
            claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);
        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt, tokenId);
    }
}

public sealed record IssuedToken(string Value, DateTimeOffset ExpiresAt, string Id);
public sealed record ValidatedToken(Guid AccountId, Guid TokenId, DateTimeOffset ExpiresAt, bool RememberMe);
