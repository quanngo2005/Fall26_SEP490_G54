using G54.Api.Hubs;
using G54.BLL.Dtos.Notification;
using G54.BLL.Services;
using Microsoft.AspNetCore.SignalR;

namespace G54.Api.Services;

public sealed class SignalRNotificationPusher(IHubContext<NotificationHub> hub) : INotificationPusher
{
    public Task PushAsync(Guid employeeId, NotificationDto notification, CancellationToken cancellationToken = default)
        => hub.Clients.Group(NotificationHub.GroupFor(employeeId))
            .SendAsync(NotificationHub.ReceiveMethod, notification, cancellationToken);
}
