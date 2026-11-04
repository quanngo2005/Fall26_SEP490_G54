using System.Reflection;
using System.Security.Claims;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.Api.Hubs;
using G54.Api.Services;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Notification;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.SignalR;

namespace G54.UnitTests;

public sealed class NotificationsControllerTests
{
    private static readonly Guid CallerId = Guid.NewGuid();

    private static NotificationDto Sample() => new(
        Guid.NewGuid(), "ANNOUNCEMENT", "NORMAL", "Họp", null, null, null, null, null, DateTimeOffset.UtcNow, false, null);

    private static NotificationsController ControllerFor(RecordingService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-1",
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", CallerId.ToString())], "TestAuth")),
            },
        },
    };

    [Theory]
    [InlineData(nameof(NotificationsController.Send), typeof(HttpPostAttribute), "", "send", "Dept")]
    [InlineData(nameof(NotificationsController.GetMine), typeof(HttpGetAttribute), "", "read", "Own")]
    [InlineData(nameof(NotificationsController.GetUnreadCount), typeof(HttpGetAttribute), "unread-count", "read", "Own")]
    [InlineData(nameof(NotificationsController.MarkRead), typeof(HttpPostAttribute), "{id:guid}/read", "read", "Own")]
    [InlineData(nameof(NotificationsController.MarkAllRead), typeof(HttpPostAttribute), "read-all", "read", "Own")]
    public void EveryAction_HasItsRoute_AndNeedsItsPermission(string action, Type verb, string route, string permission, string scope)
    {
        var type = typeof(NotificationsController);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("notifications", type.GetCustomAttribute<RouteAttribute>()?.Template);

        var method = type.GetMethod(action)!;
        Assert.Equal(route, ((HttpMethodAttribute)method.GetCustomAttribute(verb)!).Template ?? string.Empty);
        var required = method.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(required);
        Assert.Equal("notification", required.Resource);
        Assert.Equal(permission, required.Action);
        Assert.Equal(scope, required.Scope);
    }

    [Fact]
    public async Task Send_Returns201WithTheRecipientCount()
    {
        var service = new RecordingService();
        var dto = new SendNotificationDto { Title = "Họp" };

        var result = await ControllerFor(service).Send(dto, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(3, Assert.IsType<ApiResponse<SendNotificationResultDto>>(created.Value).Data!.RecipientCount);
        Assert.Equal((CallerId, "send"), (service.UserId, service.Called));
        Assert.Same(dto, service.SendDto);
    }

    [Fact]
    public async Task Reads_AndMarks_ArePassedTheCaller()
    {
        var service = new RecordingService();
        var controller = ControllerFor(service);
        var filter = new NotificationFilterDto { Unread = true };
        var id = Guid.NewGuid();

        Assert.IsType<OkObjectResult>((await controller.GetMine(filter, CancellationToken.None)).Result);
        Assert.Same(filter, service.Filter);
        Assert.IsType<OkObjectResult>((await controller.GetUnreadCount(CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.MarkRead(id, CancellationToken.None)).Result);
        Assert.Equal(id, service.ReadId);
        Assert.IsType<OkObjectResult>((await controller.MarkAllRead(CancellationToken.None)).Result);
        Assert.Equal(["list", "unread", "read", "read-all"], service.Calls);
        Assert.Equal(CallerId, service.UserId);
    }

    [Fact]
    public async Task Pusher_SendsToTheGroupOfTheEmployee()
    {
        var group = new RecordingGroup();
        var pusher = new SignalRNotificationPusher(new FakeHub(group));
        var employeeId = Guid.NewGuid();
        var notification = Sample();

        await pusher.PushAsync(employeeId, notification);

        Assert.Equal(NotificationHub.GroupFor(employeeId), group.Name);
        Assert.Equal((NotificationHub.ReceiveMethod, notification), (group.Method, group.Argument));
    }

    private sealed class RecordingService : INotificationService
    {
        public Guid UserId { get; private set; }
        public Guid ReadId { get; private set; }
        public string? Called { get; private set; }
        public List<string> Calls { get; } = [];
        public SendNotificationDto? SendDto { get; private set; }
        public NotificationFilterDto? Filter { get; private set; }

        public Task<SendNotificationResultDto> SendAsync(Guid userId, SendNotificationDto request, CancellationToken cancellationToken = default)
        {
            (UserId, Called, SendDto) = (userId, "send", request);
            return Task.FromResult(new SendNotificationResultDto(3));
        }

        public Task<PagedResult<NotificationDto>> GetMineAsync(Guid userId, NotificationFilterDto filter, CancellationToken cancellationToken = default)
        {
            (UserId, Filter) = (userId, filter);
            Calls.Add("list");
            return Task.FromResult(new PagedResult<NotificationDto>([], 0, 1, 20));
        }

        public Task<NotificationCountDto> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Calls.Add("unread");
            return Task.FromResult(new NotificationCountDto(2));
        }

        public Task<NotificationDto> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
        {
            ReadId = notificationId;
            Calls.Add("read");
            return Task.FromResult(Sample());
        }

        public Task<NotificationCountDto> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Calls.Add("read-all");
            return Task.FromResult(new NotificationCountDto(2));
        }

        public Task<Guid> GetEmployeeIdAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(userId);
    }

    private sealed class FakeHub(RecordingGroup group) : IHubContext<NotificationHub>
    {
        public IHubClients Clients { get; } = new FakeClients(group);

        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class FakeClients(RecordingGroup group) : IHubClients
    {
        public IClientProxy All => throw new NotSupportedException();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Client(string connectionId) => throw new NotSupportedException();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public IClientProxy Group(string groupName)
        {
            group.Name = groupName;
            return group;
        }

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public IClientProxy User(string userId) => throw new NotSupportedException();

        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class RecordingGroup : IClientProxy
    {
        public string? Name { get; set; }
        public string? Method { get; private set; }
        public object? Argument { get; private set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            (Method, Argument) = (method, args[0]);
            return Task.CompletedTask;
        }
    }
}
