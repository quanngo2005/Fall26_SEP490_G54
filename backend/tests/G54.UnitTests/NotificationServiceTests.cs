using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Notification;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class NotificationServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly RecordingNotifier _notifier = new();
    private Department _d1 = null!;
    private Department _d2 = null!;
    private Employee _manager = null!;
    private Employee _colleague = null!;
    private Employee _other = null!;
    private Employee _outsider = null!;

    public void Dispose() => _db.Dispose();

    private NotificationService Service(string? sendScope = "Dept") => new(
        _db,
        new FakePermissionService(null, sendScope is null ? [] : [new UserPermissionDto("notification", "send", sendScope, "NOTIFICATION_SEND")]),
        _notifier,
        new FixedTime());

    private async Task ArrangeAsync()
    {
        var branch = new Department { Id = Guid.NewGuid(), Code = "BR", Name = "Branch", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        _d1 = new Department { Id = Guid.NewGuid(), Code = "D1", Name = "Dept 1", LevelType = DepartmentLevelType.Department, ParentDepartmentId = branch.Id, Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        _d2 = new Department { Id = Guid.NewGuid(), Code = "D2", Name = "Dept 2", LevelType = DepartmentLevelType.Department, ParentDepartmentId = branch.Id, Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        var position = new JobPosition { Id = Guid.NewGuid(), Code = "CV", Title = "Officer", Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        _db.AddRange(branch, _d1, _d2, position);

        _manager = NewEmployee("MGR", _d1.Id, position.Id);
        _colleague = NewEmployee("COL", _d1.Id, position.Id);
        _other = NewEmployee("OTH", _d1.Id, position.Id);
        _outsider = NewEmployee("OUT", _d2.Id, position.Id);
        _db.AddRange(_manager, _colleague, _other, _outsider);
        await _db.SaveChangesAsync();
    }

    private static Employee NewEmployee(string code, Guid departmentId, Guid positionId, EmployeeStatus status = EmployeeStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = $"Employee {code}",
        Email = $"{code.ToLowerInvariant()}@bank.test",
        DepartmentId = departmentId,
        JobPositionId = positionId,
        Status = status,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private static SendNotificationDto Announcement() => new() { Title = "  Họp phòng  ", Body = "Họp lúc 9h" };

    private async Task<Notification> AddNotificationAsync(Employee recipient, DateTimeOffset createdAt, bool read = false, Employee? sender = null)
    {
        var row = new Notification
        {
            RecipientEmployeeId = recipient.Id,
            SenderEmployeeId = sender?.Id,
            Type = NotificationType.Announcement,
            Severity = NotificationSeverity.Normal,
            Title = $"N-{createdAt:HHmm}",
            CreatedAt = createdAt,
            ReadAt = read ? createdAt : null,
        };
        _db.Notifications.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    [Fact]
    public async Task Send_WithoutRecipients_GoesToEveryoneInTheScopeExceptTheSender()
    {
        await ArrangeAsync();

        var result = await Service().SendAsync(_manager.Id, Announcement());

        Assert.Equal(2, result.RecipientCount);
        Assert.Equal(new[] { _colleague.Id, _other.Id }.Order(), _notifier.Drafts.Select(d => d.RecipientEmployeeId).Order());
        var draft = _notifier.Drafts[0];
        Assert.Equal(NotificationType.Announcement, draft.Type);
        Assert.Equal("Họp phòng", draft.Title);
        Assert.Equal("Họp lúc 9h", draft.Body);
        Assert.Equal(NotificationSeverity.Normal, draft.Severity);
        Assert.Equal(_manager.Id, draft.SenderEmployeeId);
    }

    [Fact]
    public async Task Send_ToPickedEmployees_OnlyReachesThem()
    {
        await ArrangeAsync();

        var result = await Service().SendAsync(_manager.Id, Announcement() with { RecipientEmployeeIds = [_colleague.Id, _colleague.Id] });

        Assert.Equal(1, result.RecipientCount);
        Assert.Equal(_colleague.Id, Assert.Single(_notifier.Drafts).RecipientEmployeeId);
    }

    [Fact]
    public async Task Send_ToSomeoneOutsideTheScope_IsForbidden()
    {
        await ArrangeAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service().SendAsync(_manager.Id, Announcement() with { RecipientEmployeeIds = [_outsider.Id] }));
        Assert.Empty(_notifier.Drafts);
    }

    [Fact]
    public async Task Send_AllScope_ReachesEveryActiveEmployee()
    {
        await ArrangeAsync();
        var gone = NewEmployee("GONE", _d1.Id, _manager.JobPositionId, EmployeeStatus.Inactive);
        _db.Employees.Add(gone);
        await _db.SaveChangesAsync();

        var result = await Service("All").SendAsync(_manager.Id, Announcement());

        Assert.Equal(3, result.RecipientCount);
        Assert.DoesNotContain(_notifier.Drafts, d => d.RecipientEmployeeId == gone.Id);
    }

    [Fact]
    public async Task Send_ToAnUnknownOrInactiveEmployee_IsRejected()
    {
        await ArrangeAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SendAsync(_manager.Id, Announcement() with { RecipientEmployeeIds = [_colleague.Id, Guid.NewGuid()] }));
    }

    [Fact]
    public async Task Send_WithNobodyToReceiveIt_IsRejected()
    {
        await ArrangeAsync();
        _db.Employees.RemoveRange(_db.Employees.Where(e => e.Id != _manager.Id));
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => Service().SendAsync(_manager.Id, Announcement()));
    }

    [Fact]
    public async Task Send_WithoutTheSendPermission_IsForbidden()
    {
        await ArrangeAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(sendScope: "Own").SendAsync(_manager.Id, Announcement()));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(sendScope: null).SendAsync(_manager.Id, Announcement()));
    }

    [Fact]
    public async Task Send_ByAnUnknownAccount_IsUnauthorized()
    {
        await ArrangeAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() => Service().SendAsync(Guid.NewGuid(), Announcement()));
    }

    [Theory]
    [InlineData(null, "ok")]
    [InlineData("   ", "ok")]
    public async Task Send_RequiresATitle(string? title, string body)
    {
        await ArrangeAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SendAsync(_manager.Id, new SendNotificationDto { Title = title, Body = body }));
    }

    [Fact]
    public async Task Send_RejectsTooLongTextAndBadSeverity()
    {
        await ArrangeAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SendAsync(_manager.Id, Announcement() with { Title = new string('a', 201) }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SendAsync(_manager.Id, Announcement() with { Body = new string('a', 2001) }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SendAsync(_manager.Id, Announcement() with { Severity = "LOUD" }));
    }

    [Fact]
    public async Task Send_KeepsTheChosenSeverity()
    {
        await ArrangeAsync();

        await Service().SendAsync(_manager.Id, Announcement() with { Severity = "important" });

        Assert.All(_notifier.Drafts, d => Assert.Equal(NotificationSeverity.Important, d.Severity));
    }

    [Fact]
    public async Task Send_LinksToAnExistingWork_OrCampaign()
    {
        await ArrangeAsync();
        var work = new Work { Id = Guid.NewGuid(), Code = "W-1", Title = "Work", AssigneeEmployeeId = _colleague.Id, Priority = WorkPriority.Medium, Status = WorkStatus.New, UpdatedAt = Now };
        _db.Works.Add(work);
        await _db.SaveChangesAsync();

        await Service().SendAsync(_manager.Id, Announcement() with { RelatedType = "work", RelatedId = work.Id });

        Assert.All(_notifier.Drafts, d =>
        {
            Assert.Equal(NotificationRelatedType.Work, d.RelatedType);
            Assert.Equal(work.Id, d.RelatedId);
        });
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().SendAsync(_manager.Id, Announcement() with { RelatedType = "WORK", RelatedId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().SendAsync(_manager.Id, Announcement() with { RelatedType = "CAMPAIGN", RelatedId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Send_RejectsAHalfGivenOrUnsupportedLink()
    {
        await ArrangeAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SendAsync(_manager.Id, Announcement() with { RelatedType = "WORK" }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SendAsync(_manager.Id, Announcement() with { RelatedId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SendAsync(_manager.Id, Announcement() with { RelatedType = "HANDOVER", RelatedId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task GetMine_ListsOnlyMyNotifications_NewestFirst_WithTheSenderName()
    {
        await ArrangeAsync();
        await AddNotificationAsync(_colleague, Now.AddHours(-2));
        var newest = await AddNotificationAsync(_colleague, Now, sender: _manager);
        await AddNotificationAsync(_other, Now);

        var page = await Service().GetMineAsync(_colleague.Id, new NotificationFilterDto());

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(newest.Id, page.Items[0].Id);
        Assert.Equal("Employee MGR", page.Items[0].SenderName);
        Assert.Equal("ANNOUNCEMENT", page.Items[0].Type);
        Assert.Equal("NORMAL", page.Items[0].Severity);
        Assert.False(page.Items[0].IsRead);
    }

    [Fact]
    public async Task GetMine_CanListOnlyUnread_AndPages()
    {
        await ArrangeAsync();
        await AddNotificationAsync(_colleague, Now.AddHours(-3), read: true);
        await AddNotificationAsync(_colleague, Now.AddHours(-2));
        await AddNotificationAsync(_colleague, Now.AddHours(-1));

        var unread = await Service().GetMineAsync(_colleague.Id, new NotificationFilterDto { Unread = true });
        var paged = await Service().GetMineAsync(_colleague.Id, new NotificationFilterDto { Page = 2, PageSize = 2 });
        var clamped = await Service().GetMineAsync(_colleague.Id, new NotificationFilterDto { Page = 0, PageSize = 1000 });

        Assert.Equal(2, unread.TotalCount);
        Assert.Single(paged.Items);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(100, clamped.PageSize);
    }

    [Fact]
    public async Task UnreadCount_CountsMyUnreadOnly()
    {
        await ArrangeAsync();
        await AddNotificationAsync(_colleague, Now.AddHours(-2), read: true);
        await AddNotificationAsync(_colleague, Now.AddHours(-1));
        await AddNotificationAsync(_other, Now);

        Assert.Equal(1, (await Service().GetUnreadCountAsync(_colleague.Id)).Count);
    }

    [Fact]
    public async Task MarkRead_StampsTheTimeOnce()
    {
        await ArrangeAsync();
        var row = await AddNotificationAsync(_colleague, Now.AddHours(-1));

        var first = await Service().MarkReadAsync(_colleague.Id, row.Id);
        var again = await Service().MarkReadAsync(_colleague.Id, row.Id);

        Assert.True(first.IsRead);
        Assert.Equal(Now, first.ReadAt);
        Assert.Equal(first.ReadAt, again.ReadAt);
    }

    [Fact]
    public async Task MarkRead_OfSomeoneElsesOrAMissingNotification_IsNotFound()
    {
        await ArrangeAsync();
        var row = await AddNotificationAsync(_other, Now);

        await Assert.ThrowsAsync<NotFoundException>(() => Service().MarkReadAsync(_colleague.Id, row.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Service().MarkReadAsync(_colleague.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkAllRead_ChangesOnlyMyUnread()
    {
        await ArrangeAsync();
        await AddNotificationAsync(_colleague, Now.AddHours(-3), read: true);
        await AddNotificationAsync(_colleague, Now.AddHours(-2));
        await AddNotificationAsync(_colleague, Now.AddHours(-1));
        var others = await AddNotificationAsync(_other, Now);

        var result = await Service().MarkAllReadAsync(_colleague.Id);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, (await Service().GetUnreadCountAsync(_colleague.Id)).Count);
        Assert.Null((await _db.Notifications.AsNoTracking().SingleAsync(n => n.Id == others.Id)).ReadAt);
        Assert.Equal(0, (await Service().MarkAllReadAsync(_colleague.Id)).Count);
    }

    [Fact]
    public async Task GetEmployeeId_ResolvesAnAccountToItsEmployee()
    {
        await ArrangeAsync();
        var account = new Account { Id = Guid.NewGuid(), EmployeeId = _colleague.Id, Username = "col", PasswordHash = "hash", LastSyncAt = Now, UpdatedAt = Now };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();

        Assert.Equal(_colleague.Id, await Service().GetEmployeeIdAsync(account.Id));
        Assert.Equal(_colleague.Id, await Service().GetEmployeeIdAsync(_colleague.Id));
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
