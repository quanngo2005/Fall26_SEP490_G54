using G54.Api.Authorization;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace G54.Api.Hubs;

/// <summary>
/// Realtime channel for notifications at <c>/hubs/notifications</c>. Each signed-in employee is put in a group of their own
/// when they connect; the client receives <see cref="ReceiveMethod"/> with a <c>NotificationDto</c> for every new notification.
/// Contract: docs/api/NOTIFICATION_CONTRACT.md.
/// </summary>
[Authorize]
public sealed class NotificationHub(INotificationService notifications) : Hub
{
    public const string Path = "/hubs/notifications";
    public const string ReceiveMethod = "notification";

    public static string GroupFor(Guid employeeId) => $"employee:{employeeId}";

    public override async Task OnConnectedAsync()
    {
        var employeeId = await notifications.GetEmployeeIdAsync(Context.User!.GetRequiredUserId(), Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(employeeId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
