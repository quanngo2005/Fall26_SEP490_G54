using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Notification;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>Announcements from managers and each employee's inbox. Automatic notifications are raised by the services that own the event.</summary>
public sealed class NotificationService(
    AppDbContext db,
    IPermissionService permissions,
    INotifier notifier,
    TimeProvider time) : INotificationService
{
    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 2000;
    private const int MaxPageSize = 100;

    public async Task<SendNotificationResultDto> SendAsync(
        Guid userId,
        SendNotificationDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sender = await ResolveEmployeeAsync(userId, cancellationToken);
        var input = Validate(request);

        var granted = await permissions.GetEffectivePermissionsAsync(userId, cancellationToken);
        var level = AccessScope.LevelFor(granted, "notification", "send");
        if (level < AccessScope.DeptLevel)
        {
            throw new ForbiddenException("Bạn không có quyền gửi thông báo.");
        }

        await EnsureRelatedExistsAsync(input, cancellationToken);

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        var scope = AccessScope.DepartmentsInScope(level, sender.DepartmentId, hierarchy);
        var recipientIds = await ResolveRecipientsAsync(sender.Id, input.RecipientIds, scope, cancellationToken);

        var drafts = recipientIds
            .Select(id => new NotificationDraft(
                id,
                NotificationType.Announcement,
                input.Title,
                input.Body,
                input.Severity,
                input.RelatedType,
                input.RelatedId,
                sender.Id))
            .ToList();
        await notifier.PublishAsync(drafts, cancellationToken);

        return new SendNotificationResultDto(drafts.Count);
    }

    public async Task<PagedResult<NotificationDto>> GetMineAsync(
        Guid userId,
        NotificationFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var employeeId = await GetEmployeeIdAsync(userId, cancellationToken);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientEmployeeId == employeeId);
        if (filter.Unread == true)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .Include(n => n.Sender)
            .OrderByDescending(n => n.CreatedAt)
            .ThenBy(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDto>(
            rows.Select(row => NotificationMapper.ToDto(row, row.Sender?.FullName)).ToList(),
            totalCount,
            page,
            pageSize);
    }

    public async Task<NotificationCountDto> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var employeeId = await GetEmployeeIdAsync(userId, cancellationToken);
        var count = await db.Notifications.CountAsync(n => n.RecipientEmployeeId == employeeId && n.ReadAt == null, cancellationToken);
        return new NotificationCountDto(count);
    }

    public async Task<NotificationDto> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var employeeId = await GetEmployeeIdAsync(userId, cancellationToken);
        var row = await db.Notifications
            .Include(n => n.Sender)
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientEmployeeId == employeeId, cancellationToken)
            ?? throw new NotFoundException("Thông báo", notificationId);

        if (row.ReadAt is null)
        {
            row.ReadAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        return NotificationMapper.ToDto(row, row.Sender?.FullName);
    }

    public async Task<NotificationCountDto> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var employeeId = await GetEmployeeIdAsync(userId, cancellationToken);
        var now = time.GetUtcNow();
        var unread = await db.Notifications
            .Where(n => n.RecipientEmployeeId == employeeId && n.ReadAt == null)
            .ToListAsync(cancellationToken);
        foreach (var row in unread)
        {
            row.ReadAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new NotificationCountDto(unread.Count);
    }

    public async Task<Guid> GetEmployeeIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => (await ResolveEmployeeAsync(userId, cancellationToken)).Id;

    private async Task<Employee> ResolveEmployeeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == userId || (e.Account != null && e.Account.Id == userId), cancellationToken);
        if (employee is null || employee.Status != EmployeeStatus.Active)
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return employee;
    }

    /// <summary>The picked employees, or every active employee in scope; the sender never receives their own announcement.</summary>
    private async Task<List<Guid>> ResolveRecipientsAsync(
        Guid senderId,
        IReadOnlyList<Guid> picked,
        HashSet<Guid>? scope,
        CancellationToken cancellationToken)
    {
        var query = db.Employees.AsNoTracking()
            .Where(e => e.Status == EmployeeStatus.Active && e.Id != senderId);
        if (picked.Count > 0)
        {
            query = query.Where(e => picked.Contains(e.Id));
        }

        var candidates = await query.Select(e => new { e.Id, e.DepartmentId }).ToListAsync(cancellationToken);

        if (scope is not null)
        {
            if (picked.Count > 0 && candidates.Any(c => !scope.Contains(c.DepartmentId)))
            {
                throw new ForbiddenException("Chỉ được gửi thông báo cho nhân viên trong phạm vi phòng ban của bạn.");
            }

            candidates = candidates.Where(c => scope.Contains(c.DepartmentId)).ToList();
        }

        if (picked.Count > 0 && candidates.Count != picked.Count(id => id != senderId))
        {
            throw new ValidationException("Có người nhận không tồn tại hoặc không còn hoạt động.");
        }

        if (candidates.Count == 0)
        {
            throw new ValidationException("Không có người nhận hợp lệ.");
        }

        return candidates.Select(c => c.Id).ToList();
    }

    private async Task EnsureRelatedExistsAsync(ValidInput input, CancellationToken cancellationToken)
    {
        if (input.RelatedType is null)
        {
            return;
        }

        var id = input.RelatedId!.Value;
        var exists = input.RelatedType == NotificationRelatedType.Work
            ? await db.Works.AnyAsync(w => w.Id == id, cancellationToken)
            : await db.BusinessCampaigns.AnyAsync(c => c.Id == id, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException(input.RelatedType == NotificationRelatedType.Work ? "Công việc" : "Chiến dịch", id);
        }
    }

    private static ValidInput Validate(SendNotificationDto request)
    {
        var errors = new List<string>();

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            errors.Add("Tiêu đề là bắt buộc.");
        }
        else if (title.Length > MaxTitleLength)
        {
            errors.Add($"Tiêu đề tối đa {MaxTitleLength} ký tự.");
        }

        var body = string.IsNullOrWhiteSpace(request.Body) ? null : request.Body.Trim();
        if (body?.Length > MaxBodyLength)
        {
            errors.Add($"Nội dung tối đa {MaxBodyLength} ký tự.");
        }

        var relatedType = EnumApiFormat.ParseEnum<NotificationRelatedType>(request.RelatedType, "relatedType");
        if (relatedType == NotificationRelatedType.Handover)
        {
            errors.Add("Chỉ được liên kết tới công việc hoặc chiến dịch.");
        }

        if ((relatedType is null) != (request.RelatedId is null))
        {
            errors.Add("relatedType và relatedId phải được gửi cùng nhau.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var severity = EnumApiFormat.ParseEnum<NotificationSeverity>(request.Severity, "severity") ?? NotificationSeverity.Normal;
        var picked = (request.RecipientEmployeeIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        return new ValidInput(title!, body, severity, picked, relatedType, request.RelatedId);
    }

    private sealed record ValidInput(
        string Title,
        string? Body,
        NotificationSeverity Severity,
        IReadOnlyList<Guid> RecipientIds,
        NotificationRelatedType? RelatedType,
        Guid? RelatedId);
}
