using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Handover;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class HandoversControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(nameof(HandoversController.CreateHandover), "works/{workId:guid}/handovers", "handover", "create")]
    public void EveryAction_ExposesItsDocumentedRoute_AndNeedsItsPermission(string action, string route, string resource, string permission)
    {
        var type = typeof(HandoversController);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(string.Empty, type.GetCustomAttribute<RouteAttribute>()?.Template);

        var method = type.GetMethod(action)!;
        Assert.Equal(route, method.GetCustomAttribute<HttpPostAttribute>()?.Template);

        var required = method.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(required);
        Assert.Equal(resource, required.Resource);
        Assert.Equal(permission, required.Action);
        Assert.Equal("Own", required.Scope);
    }

    [Fact]
    public async Task CreateHandover_Returns201WithTheTransferMessage()
    {
        var userId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = ControllerFor(service, userId);
        var request = new CreateHandoverRequestDto { ToEmployeeId = Guid.NewGuid(), Reason = "r", Amount = 1m };

        var result = await controller.CreateHandover(workId, request, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(userId, service.UserId);
        Assert.Equal(workId, service.WorkId);
        Assert.Same(request, service.Request);
        Assert.Equal("Đã chuyển công việc cho người nhận.", MessageOf(created));
    }

    [Fact]
    public async Task CreateHandover_WithoutIdentity_NeverReachesTheService()
    {
        var service = new RecordingService();
        var controller = new HandoversController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            controller.CreateHandover(id, new CreateHandoverRequestDto(), CancellationToken.None));

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task GetRecipients_IsAGetNeedingHandoverCreate_AndReturnsTheServiceList()
    {
        var method = typeof(HandoversController).GetMethod(nameof(HandoversController.GetRecipients))!;
        Assert.Equal("handovers/recipients", method.GetCustomAttribute<HttpGetAttribute>()?.Template);
        var required = method.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(required);
        Assert.Equal("handover", required.Resource);
        Assert.Equal("create", required.Action);
        Assert.Equal("Own", required.Scope);

        var userId = Guid.NewGuid();
        var service = new RecordingService();
        service.Recipients.Add(new HandoverRecipientDto(Guid.NewGuid(), "NV002", "Trần Thị B", "Giao dịch viên"));

        var result = await ControllerFor(service, userId).GetRecipients(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(userId, service.UserId);
        Assert.Equal(1, JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, JsonOptions))
            .RootElement.GetProperty("data").GetArrayLength());
    }

    private static string? MessageOf(ObjectResult result)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, JsonOptions));
        return document.RootElement.GetProperty("message").GetString();
    }

    private static HandoversController ControllerFor(IHandoverService service, Guid userId) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-1",
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "TestAuth")),
            },
        },
    };

    private sealed class RecordingService : IHandoverService
    {
        public int Calls { get; private set; }
        public Guid UserId { get; private set; }
        public Guid WorkId { get; private set; }
        public CreateHandoverRequestDto? Request { get; private set; }

        public HandoverDetailDto Detail { get; set; } = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "W-1",
            "Work",
            Guid.NewGuid(),
            "From",
            Guid.NewGuid(),
            "To",
            "r",
            0m,
            "ACCEPTED",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        public Task<HandoverDetailDto> CreateHandoverAsync(
            Guid workId,
            Guid userId,
            CreateHandoverRequestDto request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            WorkId = workId;
            UserId = userId;
            Request = request;
            return Task.FromResult(Detail);
        }

        public Task<IReadOnlyList<HandoverRecipientDto>> GetRecipientsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            UserId = userId;
            return Task.FromResult<IReadOnlyList<HandoverRecipientDto>>(Recipients);
        }

        public List<HandoverRecipientDto> Recipients { get; } = [];
    }
}
