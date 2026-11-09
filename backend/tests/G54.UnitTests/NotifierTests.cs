using G54.BLL.Dtos.Notification;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class NotifierTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public void Dispose() => _db.Dispose();

    private Notifier Notifier(INotificationPusher pusher) => new(_db, pusher, new FixedTime(), NullLogger<Notifier>.Instance);

    private async Task<Employee> AddEmployeeAsync(string code)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Employee {code}",
            Email = $"{code}@bank.test",
            DepartmentId = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        return employee;
    }

    [Fact]
    public async Task Publish_StoresEachNotificationAndPushesItToItsRecipient()
    {
        var sender = await AddEmployeeAsync("SND");
        var first = await AddEmployeeAsync("ONE");
        var second = await AddEmployeeAsync("TWO");
        var pusher = new RecordingPusher();
        var relatedId = Guid.NewGuid();

        await Notifier(pusher).PublishAsync(
        [
            new NotificationDraft(first.Id, NotificationType.Announcement, "Họp", "9h", NotificationSeverity.Important, NotificationRelatedType.Work, relatedId, sender.Id),
            new NotificationDraft(second.Id, NotificationType.WorkOverdue, "Quá hạn"),
        ]);

        var rows = await _db.Notifications.AsNoTracking().OrderBy(n => n.Title).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(Now, row.CreatedAt));
        Assert.All(rows, row => Assert.Null(row.ReadAt));

        Assert.Equal(2, pusher.Pushed.Count);
        var pushed = pusher.Pushed.Single(p => p.EmployeeId == first.Id).Notification;
        Assert.Equal("ANNOUNCEMENT", pushed.Type);
        Assert.Equal("IMPORTANT", pushed.Severity);
        Assert.Equal("WORK", pushed.RelatedType);
        Assert.Equal(relatedId, pushed.RelatedId);
        Assert.Equal("Employee SND", pushed.SenderName);
        Assert.False(pushed.IsRead);
        Assert.Null(pusher.Pushed.Single(p => p.EmployeeId == second.Id).Notification.SenderName);
    }

    [Fact]
    public async Task Publish_WithNothingToSend_DoesNothing()
    {
        var pusher = new RecordingPusher();

        await Notifier(pusher).PublishAsync([]);

        Assert.Empty(pusher.Pushed);
        Assert.Empty(_db.Notifications);
    }

    [Fact]
    public async Task Publish_KeepsTheNotificationWhenThePushFails()
    {
        var recipient = await AddEmployeeAsync("ONE");

        await Notifier(new RecordingPusher { Fails = true }).PublishAsync(
            [new NotificationDraft(recipient.Id, NotificationType.Announcement, "Họp")]);

        Assert.Single(_db.Notifications);
    }

    [Fact]
    public async Task TryPublish_SwallowsAStorageFailure_ButPublishDoesNot()
    {
        var recipient = await AddEmployeeAsync("ONE");
        var notifier = Notifier(new RecordingPusher());
        _db.Dispose();
        var drafts = new[] { new NotificationDraft(recipient.Id, NotificationType.Announcement, "Họp") };

        await notifier.TryPublishAsync(drafts);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => notifier.PublishAsync(drafts));
    }

    private sealed class RecordingPusher : INotificationPusher
    {
        public List<(Guid EmployeeId, NotificationDto Notification)> Pushed { get; } = [];

        public bool Fails { get; init; }

        public Task PushAsync(Guid employeeId, NotificationDto notification, CancellationToken cancellationToken = default)
        {
            if (Fails)
            {
                throw new InvalidOperationException("hub is down");
            }

            Pushed.Add((employeeId, notification));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
