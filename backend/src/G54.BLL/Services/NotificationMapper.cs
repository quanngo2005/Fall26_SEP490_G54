using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Notification;
using G54.DAL.Entities;

namespace G54.BLL.Services;

internal static class NotificationMapper
{
    public static NotificationDto ToDto(Notification row, string? senderName) => new(
        row.Id,
        row.Type.ToEnumString(),
        row.Severity.ToEnumString(),
        row.Title,
        row.Body,
        row.RelatedType?.ToEnumString(),
        row.RelatedId,
        row.SenderEmployeeId,
        senderName,
        row.CreatedAt,
        row.ReadAt is not null,
        row.ReadAt);
}
