using System.Net;
using System.Reflection;
using G54.Api.Controllers;
using G54.BLL.Dtos.Auth;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace G54.UnitTests;

public sealed class AuthControllerTests
{
    private static readonly AuthResponseDto Tokens = new(
        "access-token",
        "refresh-token",
        DateTimeOffset.UtcNow.AddMinutes(15),
        DateTimeOffset.UtcNow.AddHours(12),
        new AuthenticatedUserDto(Guid.NewGuid(), "hoa.tran@progest.local", "Tran Thi Hoa", ["EMPLOYEE"], ["work.read"]));

    [Fact]
    public async Task Login_ReturnsTokens_DisablesCaching_AndPassesTheClientIp()
    {
        var service = new StubAuthService { LoginResult = new AuthResult(Tokens, null) };
        var controller = ControllerFor(service);

        var result = await controller.Login(new LoginRequestDto("hoa.tran@progest.local", "pw", false), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(Tokens, ok.Value);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", controller.Response.Headers.Pragma.ToString());
        Assert.Equal("10.0.0.5", service.LastIpAddress);
    }

    [Fact]
    public async Task Login_Returns403_WhenTheAccountIsLocked()
    {
        var service = new StubAuthService { LoginResult = new AuthResult(null, AuthFailureReason.AccountLocked) };
        var controller = ControllerFor(service);

        var result = await controller.Login(new LoginRequestDto("hoa.tran@progest.local", "pw", false), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("Account locked", Assert.IsType<ProblemDetails>(problem.Value).Title);
    }

    [Fact]
    public async Task Login_Returns401_WhenTheCredentialsAreInvalid()
    {
        var service = new StubAuthService { LoginResult = new AuthResult(null, AuthFailureReason.InvalidCredentials) };
        var controller = ControllerFor(service);

        var result = await controller.Login(new LoginRequestDto("hoa.tran@progest.local", "wrong", false), CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var details = Assert.IsType<ProblemDetails>(unauthorized.Value);
        Assert.Equal(StatusCodes.Status401Unauthorized, details.Status);
        Assert.Equal("Invalid credentials", details.Title);
    }

    [Fact]
    public async Task Refresh_ReturnsRotatedTokens_WhenTheSessionIsValid()
    {
        var service = new StubAuthService { RefreshResult = Tokens };
        var controller = ControllerFor(service);

        var result = await controller.Refresh(new RefreshRequestDto("old-refresh"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(Tokens, ok.Value);
        Assert.Equal("old-refresh", service.LastRefreshToken);
    }

    [Fact]
    public async Task Refresh_Returns401_WhenTheSessionIsNoLongerValid()
    {
        var controller = ControllerFor(new StubAuthService { RefreshResult = null });

        var result = await controller.Refresh(new RefreshRequestDto("expired-refresh"), CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal("Invalid session", Assert.IsType<ProblemDetails>(unauthorized.Value).Title);
    }

    [Fact]
    public async Task Logout_Returns401_WithoutABearerToken_AndDoesNotCallTheService()
    {
        var service = new StubAuthService { LogoutResult = true };
        var controller = ControllerFor(service);

        var result = await controller.Logout(new LogoutRequest("refresh"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.False(service.LogoutCalled);
    }

    [Fact]
    public async Task Logout_ExtractsTheBearerToken_CaseInsensitively_AndReturnsTheMessage()
    {
        var service = new StubAuthService { LogoutResult = true };
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "bearer  access-123 ";
        var controller = ControllerFor(service, httpContext);

        var result = await controller.Logout(new LogoutRequest("refresh-123"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Logout successful", Assert.IsType<LogoutResponseDto>(ok.Value).Message);
        Assert.Equal("access-123", service.LastAccessToken);
        Assert.Equal("refresh-123", service.LastRefreshToken);
    }

    [Fact]
    public async Task Logout_Returns401_WhenTheServiceRejectsTheSession()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer access-123";
        var controller = ControllerFor(new StubAuthService { LogoutResult = false }, httpContext);

        var result = await controller.Logout(new LogoutRequest("refresh-123"), CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal("Invalid session", Assert.IsType<ProblemDetails>(unauthorized.Value).Title);
    }

    [Fact]
    public void ControllerAndActionAttributes_ExposeTheExpectedRoutesAndPolicies()
    {
        Assert.Equal("auth", typeof(AuthController).GetCustomAttribute<RouteAttribute>()?.Template);

        var login = typeof(AuthController).GetMethod(nameof(AuthController.Login))!;
        Assert.NotNull(login.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal(AuthController.LoginRateLimitPolicy, login.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);

        var logout = typeof(AuthController).GetMethod(nameof(AuthController.Logout))!;
        Assert.NotNull(logout.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(logout.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    private static AuthController ControllerFor(IAuthService service, HttpContext? httpContext = null)
    {
        var context = httpContext ?? new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        context.RequestServices = new ServiceCollection().AddControllers().Services.BuildServiceProvider();
        return new AuthController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private sealed class StubAuthService : IAuthService
    {
        public AuthResult LoginResult { get; init; } = new(null, AuthFailureReason.InvalidCredentials);

        public AuthResponseDto? RefreshResult { get; init; }

        public bool LogoutResult { get; init; }

        public string? LastIpAddress { get; private set; }

        public string? LastRefreshToken { get; private set; }

        public string? LastAccessToken { get; private set; }

        public bool LogoutCalled { get; private set; }

        public Task<AuthResult> AuthenticateAsync(LoginRequestDto request, string? ipAddress, CancellationToken cancellationToken)
        {
            LastIpAddress = ipAddress;
            return Task.FromResult(LoginResult);
        }

        public Task<AuthResponseDto?> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken)
        {
            LastRefreshToken = refreshToken;
            LastIpAddress = ipAddress;
            return Task.FromResult(RefreshResult);
        }

        public Task<bool> LogoutAsync(string accessToken, string refreshToken, string? ipAddress, CancellationToken cancellationToken)
        {
            LogoutCalled = true;
            LastAccessToken = accessToken;
            LastRefreshToken = refreshToken;
            return Task.FromResult(LogoutResult);
        }

        public Task<AuthResponseDto> ChangePasswordAsync(Guid accountId, ChangePasswordRequestDto request, string? ipAddress, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by the controller tests.");
    }
}
