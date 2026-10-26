using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Sidebar;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class SidebarConfigurationsControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task ControllerUsesAuthenticatedSubjectAndCanonicalEnvelope()
    {
        var accountId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = new SidebarConfigurationsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", accountId.ToString())], "test")),
                },
            },
        };
        var result = await controller.GetCurrent(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(accountId, service.AccountId);
        Assert.False(service.AllRoles);
        var json = JsonSerializer.Serialize(ok.Value, JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(200, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("employee", document.RootElement.GetProperty("data")[0].GetProperty("role").GetString());
        Assert.Equal("sidebar-configurations", typeof(SidebarConfigurationsController).GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.NotNull(typeof(SidebarConfigurationsController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public async Task MissingIdentityCannotReachService()
    {
        var service = new RecordingService();
        var controller = new SidebarConfigurationsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.GetAll(CancellationToken.None));
        Assert.Equal(Guid.Empty, service.AccountId);
    }

    private sealed class RecordingService : ISidebarConfigurationService
    {
        public Guid AccountId { get; private set; }
        public bool AllRoles { get; private set; }
        public Task<IReadOnlyList<SidebarConfigurationDto>> GetAsync(Guid accountId, bool allRoles, CancellationToken cancellationToken)
        {
            AccountId = accountId;
            AllRoles = allRoles;
            return Task.FromResult<IReadOnlyList<SidebarConfigurationDto>>([new("employee", ["overview"], 0)]);
        }

        public Task<SidebarConfigurationDto> SaveAsync(Guid accountId, string role, SaveSidebarConfigurationDto request, CancellationToken cancellationToken)
            => Task.FromResult(new SidebarConfigurationDto(role, request.ItemIds, request.Version + 1));
    }
}
