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
    [InlineData(nameof(HandoversController.AcceptHandover), "handovers/{id:guid}/accept", "handover", "respond")]
    [InlineData(nameof(HandoversController.RejectHandover), "handovers/{id:guid}/reject", "handover", "respond")]
    [InlineData(nameof(HandoversController.ApproveHandover), "handovers/{id:guid}/manager/approve", "handover", "manage")]
    [InlineData(nameof(HandoversController.ReassignHandover), "handovers/{id:guid}/manager/reassign", "handover", "manage")]
    [InlineData(nameof(HandoversController.RejectHandoverAsManager), "handovers/{id:guid}/manager/reject", "handover", "manage")]
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
    public async Task CreateHandover_Returns201_AndPicksTheMessageByOutcome()
    {
        var userId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = ControllerFor(service, userId);
        var request = new CreateHandoverRequestDto { ToEmployeeId = Guid.NewGuid(), Reason = "r", Amount = 1m };

        var pending = await controller.CreateHandover(workId, request, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(pending.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(userId, service.UserId);
        Assert.Equal(workId, service.WorkId);
        Assert.Same(request, service.Request);
        Assert.Equal("Gửi yêu cầu bàn giao thành công.", MessageOf(created));
        Assert.Equal("PENDING", StatusOf(created));

        service.Detail = service.Detail with { Status = "ACCEPTED" };
        var transferred = await controller.CreateHandover(workId, request, CancellationToken.None);

        Assert.Equal("Đã chuyển công việc cho người nhận.", MessageOf(Assert.IsType<ObjectResult>(transferred.Result)));
    }

    [Fact]
    public async Task RecipientActions_UseTheAuthenticatedUser()
    {
        var userId = Guid.NewGuid();
        var handoverId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = ControllerFor(service, userId);

        var accept = await controller.AcceptHandover(handoverId, CancellationToken.None);
        Assert.Equal("Đã nhận bàn giao công việc.", MessageOf(Assert.IsType<OkObjectResult>(accept.Result)));
        Assert.Equal(("accept", handoverId, userId), service.Last);

        var reject = await controller.RejectHandover(handoverId, CancellationToken.None);
        Assert.Equal("Đã từ chối yêu cầu bàn giao.", MessageOf(Assert.IsType<OkObjectResult>(reject.Result)));
        Assert.Equal(("reject", handoverId, userId), service.Last);
    }

    [Fact]
    public async Task ManagerActions_UseTheAuthenticatedUser()
    {
        var userId = Guid.NewGuid();
        var handoverId = Guid.NewGuid();
        var newRecipient = Guid.NewGuid();
        var service = new RecordingService();
        var controller = ControllerFor(service, userId);

        var approve = await controller.ApproveHandover(handoverId, CancellationToken.None);
        Assert.Equal("Đã chuyển công việc cho người nhận.", MessageOf(Assert.IsType<OkObjectResult>(approve.Result)));
        Assert.Equal(("approve", handoverId, userId), service.Last);

        var reassign = await controller.ReassignHandover(
            handoverId,
            new ReassignHandoverRequestDto { ToEmployeeId = newRecipient },
            CancellationToken.None);
        Assert.Equal("Đã chuyển công việc cho người nhận.", MessageOf(Assert.IsType<OkObjectResult>(reassign.Result)));
        Assert.Equal(("reassign", handoverId, userId), service.Last);
        Assert.Equal(newRecipient, service.Reassign!.ToEmployeeId);

        var reject = await controller.RejectHandoverAsManager(handoverId, CancellationToken.None);
        Assert.Equal("Đã từ chối yêu cầu bàn giao.", MessageOf(Assert.IsType<OkObjectResult>(reject.Result)));
        Assert.Equal(("manager-reject", handoverId, userId), service.Last);
    }

    [Fact]
    public async Task AnyAction_WithoutIdentity_NeverReachesTheService()
    {
        var service = new RecordingService();
        var controller = new HandoversController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            controller.CreateHandover(id, new CreateHandoverRequestDto(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.AcceptHandover(id, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.RejectHandover(id, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.ApproveHandover(id, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            controller.ReassignHandover(id, new ReassignHandoverRequestDto(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.RejectHandoverAsManager(id, CancellationToken.None));

        Assert.Equal(0, service.Calls);
    }

    private static string? MessageOf(ObjectResult result)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, JsonOptions));
        return document.RootElement.GetProperty("message").GetString();
    }

    private static string? StatusOf(ObjectResult result)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, JsonOptions));
        return document.RootElement.GetProperty("data").GetProperty("status").GetString();
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
        public ReassignHandoverRequestDto? Reassign { get; private set; }
        public (string Action, Guid HandoverId, Guid UserId) Last { get; private set; }

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
            "PENDING",
            DateTimeOffset.UtcNow,
            null,
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

        public Task<HandoverDetailDto> AcceptHandoverAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default)
            => Record("accept", handoverId, userId);

        public Task<HandoverDetailDto> RejectHandoverAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default)
            => Record("reject", handoverId, userId);

        public Task<HandoverDetailDto> ApproveByManagerAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default)
            => Record("approve", handoverId, userId);

        public Task<HandoverDetailDto> ReassignByManagerAsync(
            Guid handoverId,
            Guid userId,
            ReassignHandoverRequestDto request,
            CancellationToken cancellationToken = default)
        {
            Reassign = request;
            return Record("reassign", handoverId, userId);
        }

        public Task<HandoverDetailDto> RejectByManagerAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default)
            => Record("manager-reject", handoverId, userId);

        private Task<HandoverDetailDto> Record(string action, Guid handoverId, Guid userId)
        {
            Calls++;
            Last = (action, handoverId, userId);
            return Task.FromResult(Detail);
        }
    }
}
