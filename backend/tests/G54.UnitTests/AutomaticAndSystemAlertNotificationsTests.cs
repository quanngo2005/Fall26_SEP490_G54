using G54.Api.Middleware;
using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Dtos.Auth;
using G54.BLL.Dtos.Submission;
using G54.BLL.Dtos.WorkTransaction;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class AutomaticAndSystemAlertNotificationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AutomaticNotifications_DraftBuilders_ShapeExpectedTypesSeveritiesAndRecipients()
    {
        var manager = new Employee { Id = Guid.NewGuid(), EmployeeCode = "MGR", FullName = "Trưởng Phòng" };
        var staff = new Employee { Id = Guid.NewGuid(), EmployeeCode = "EMP", FullName = "Nhân Viên", ManagerId = manager.Id };
        var admin = new Employee { Id = Guid.NewGuid(), EmployeeCode = "ADM", FullName = "Quản Trị" };
        var qa = new Employee { Id = Guid.NewGuid(), EmployeeCode = "QA1", FullName = "Kiểm Soát QA" };
        var dept = new Department { Id = Guid.NewGuid(), Code = "KD", Name = "Phòng Kinh doanh" };
        var period = new EvaluationPeriod { Id = Guid.NewGuid(), Code = "2026-Q4", Name = "Quý 4/2026" };
        var campaign = new BusinessCampaign { Id = Guid.NewGuid(), Code = "CMP-01", Name = "Chiến dịch CASA" };
        var work = new Work { Id = Guid.NewGuid(), Code = "WRK-01", Title = "Mở thẻ", AssigneeEmployeeId = staff.Id, PlannedEndDate = new DateOnly(2026, 12, 31) };
        var submission = new ResultSubmission { Id = Guid.NewGuid(), WorkId = work.Id, SubmitterEmployeeId = staff.Id, SubmissionNo = 2 };

        Assert.Null(AutomaticNotifications.WorkAssigned(work, staff, staff));
        var assigned = AutomaticNotifications.WorkAssigned(work, staff, manager)!;
        Assert.Equal(NotificationType.WorkAssigned, assigned.Type);
        Assert.Equal(staff.Id, assigned.RecipientEmployeeId);
        Assert.Contains("31/12/2026", assigned.Body, StringComparison.Ordinal);

        var batch = AutomaticNotifications.BatchWorksAssigned(campaign, [(staff.Id, 4), (manager.Id, 2)], manager);
        Assert.Equal(staff.Id, Assert.Single(batch).RecipientEmployeeId);
        Assert.Contains("4 đầu việc", batch[0].Body, StringComparison.Ordinal);

        var submitted = AutomaticNotifications.SubmissionSubmitted(submission.Id, work.Code, work.Title, 2, staff)!;
        Assert.Equal(NotificationType.SubmissionSubmitted, submitted.Type);
        Assert.Equal(manager.Id, submitted.RecipientEmployeeId);

        var approved = AutomaticNotifications.SubmissionDecided(submission, work, manager, "APPROVED", null);
        Assert.Equal(NotificationSeverity.Normal, approved.Severity);
        var rejected = AutomaticNotifications.SubmissionDecided(submission, work, manager, "REJECTED", "Thiếu chữ ký");
        Assert.Equal(NotificationSeverity.Important, rejected.Severity);
        Assert.Contains("Thiếu chữ ký", rejected.Body, StringComparison.Ordinal);

        var mismatches = AutomaticNotifications.TransactionMismatched(
            work,
            staff,
            [("TX01", "PRODUCT_MISMATCH"), ("TX02", "EMPLOYEE_MISMATCH"), ("TX03", "CUSTOMER_MISMATCH"), ("TX04", "REFERRER_MISMATCH"), ("TX05", "TRANSACTION_BEFORE_HANDOVER"), ("TX06", "METRIC_TYPE_MISMATCH")]);
        Assert.Equal(2, mismatches.Count);
        Assert.Contains("+1 mã khác", mismatches[0].Body, StringComparison.Ordinal);

        var importErr = AutomaticNotifications.ImportCompletedWithErrors(admin.Id, "giao dịch Core Banking", "batch.xlsx", 18, 5, Guid.NewGuid());
        Assert.Equal(NotificationType.ImportCompletedWithErrors, importErr.Type);
        Assert.Equal(NotificationRelatedType.ImportBatch, importErr.RelatedType);

        var risk = new RiskEvent { Id = Guid.NewGuid(), Code = "RSK-0001", Severity = RiskEventSeverity.High, Description = "Sai quy trình", ReversalReason = "Đối chiếu lại hợp lệ" };
        var riskRecorded = AutomaticNotifications.RiskEventRecorded(risk, staff, qa, 10m);
        Assert.Equal(2, riskRecorded.Count);
        Assert.Contains("-10 điểm", riskRecorded[0].Body, StringComparison.Ordinal);

        var riskReversed = AutomaticNotifications.RiskEventReversed(risk, staff, qa);
        Assert.Equal(2, riskReversed.Count);
        Assert.All(riskReversed, d => Assert.Equal(NotificationType.RiskEventReversed, d.Type));

        var flag = new QualityFlag { Id = Guid.NewGuid(), Severity = QualityFlagSeverity.High, Reason = "Thiếu chứng từ gốc" };
        var flagDrafts = AutomaticNotifications.QualityFlagRaised(flag, work, staff, qa);
        Assert.Equal(2, flagDrafts.Count);
        Assert.Equal(NotificationType.QualityFlagRaised, flagDrafts[0].Type);

        var appeal = new Appeal { Id = Guid.NewGuid(), EmployeeId = staff.Id, Reason = "Đã bổ sung chứng từ" };
        var appealFiled = AutomaticNotifications.AppealFiled(appeal, work, staff)!;
        Assert.Equal(NotificationType.AppealFiled, appealFiled.Type);
        Assert.Equal(manager.Id, appealFiled.RecipientEmployeeId);

        var appealAccepted = AutomaticNotifications.AppealDecided(appeal, work, manager, "ACCEPT", "Hợp lệ");
        Assert.Equal(NotificationType.AppealDecided, appealAccepted.Type);
        var appealAdjusted = AutomaticNotifications.AppealDecided(appeal, work, manager, "ADJUST", "Cần thêm bản sao");
        Assert.Contains("Cần thêm bản sao", appealAdjusted.Body, StringComparison.Ordinal);

        var calibSub = AutomaticNotifications.CalibrationSubmitted(dept, period, manager, [admin.Id, manager.Id]);
        Assert.Equal(admin.Id, Assert.Single(calibSub).RecipientEmployeeId);

        var calibDecided = AutomaticNotifications.CalibrationDecided(dept, period, admin, [manager.Id], approved: false, "Vượt hạn ngạch A");
        Assert.Equal(NotificationType.CalibrationDecided, Assert.Single(calibDecided).Type);

        var published = AutomaticNotifications.PeriodResultsPublished(period, admin, [staff.Id, admin.Id]);
        Assert.Equal(NotificationType.ResultPublished, Assert.Single(published).Type);

        var campDecided = AutomaticNotifications.CampaignDecided(campaign, admin, [staff.Id], approved: true, "Triển khai ngay");
        Assert.Equal(NotificationType.CampaignDecided, Assert.Single(campDecided).Type);

        var profile = new EvaluationProfile { Id = Guid.NewGuid(), Code = "EP-01", Name = "Sales Profile", Version = 2 };
        var profDecided = AutomaticNotifications.EvaluationProfileDecided(profile, manager.Id, [admin.Id], approved: false, "Trọng số chưa cân đối");
        Assert.Equal(NotificationType.EvaluationProfileDecided, Assert.Single(profDecided).Type);

        var sysAlerts = AutomaticNotifications.SystemAlertForAdmins([admin.Id], "Cảnh báo lỗi hệ thống", "TraceId 123");
        Assert.Equal(NotificationType.SystemAlert, Assert.Single(sysAlerts).Type);
    }

    [Fact]
    public async Task SystemAlertNotifier_RoutesAlertsToActiveAdminsOnly()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        var recorder = new RecordingNotifier();
        services.AddSingleton<INotifier>(recorder);
        using var provider = services.BuildServiceProvider();

        Guid activeAdminEmpId;
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dept = new Department { Id = Guid.NewGuid(), Code = "HQ", Name = "HQ", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active };
            var pos = new JobPosition { Id = Guid.NewGuid(), Code = "SYS_ADMIN", Title = "Admin", Status = RecordStatus.Active };
            var adminRole = new Role { Id = Guid.NewGuid(), Code = "SYSTEM_ADMIN", Name = "System Admin", Status = RecordStatus.Active };
            var activeAdmin = new Employee { Id = Guid.NewGuid(), EmployeeCode = "ADM1", FullName = "Admin One", Email = "adm1@test", DepartmentId = dept.Id, JobPositionId = pos.Id, Status = EmployeeStatus.Active };
            var lockedAdmin = new Employee { Id = Guid.NewGuid(), EmployeeCode = "ADM2", FullName = "Admin Locked", Email = "adm2@test", DepartmentId = dept.Id, JobPositionId = pos.Id, Status = EmployeeStatus.Active };
            var acc1 = new Account { Id = Guid.NewGuid(), EmployeeId = activeAdmin.Id, Username = "adm1", PasswordHash = "h", IsLocked = false };
            var acc2 = new Account { Id = Guid.NewGuid(), EmployeeId = lockedAdmin.Id, Username = "adm2", PasswordHash = "h", IsLocked = true };
            db.AddRange(dept, pos, adminRole, activeAdmin, lockedAdmin, acc1, acc2);
            db.UserRoles.AddRange(
                new UserRole { Id = Guid.NewGuid(), AccountId = acc1.Id, RoleId = adminRole.Id },
                new UserRole { Id = Guid.NewGuid(), AccountId = acc2.Id, RoleId = adminRole.Id });
            await db.SaveChangesAsync();
            activeAdminEmpId = activeAdmin.Id;
        }

        var alertNotifier = new SystemAlertNotifier(provider.GetRequiredService<IServiceScopeFactory>());
        await alertNotifier.NotifyAdminsAsync("Cảnh báo HTTP 500", "Chi tiết lỗi nội bộ");

        var draft = Assert.Single(recorder.Drafts);
        Assert.Equal(activeAdminEmpId, draft.RecipientEmployeeId);
        Assert.Equal(NotificationType.SystemAlert, draft.Type);
        Assert.Equal(NotificationSeverity.Important, draft.Severity);
    }

    [Fact]
    public async Task GlobalExceptionHandler_NotifiesSystemAdminsOnUnhandled500_ButNotOnAppException()
    {
        var recordingAlerts = new RecordingSystemAlerts();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, recordingAlerts);

        var appCtx = new DefaultHttpContext { TraceIdentifier = "trace-400" };
        appCtx.Request.Method = "POST";
        appCtx.Request.Path = "/notifications";
        appCtx.Response.Body = new MemoryStream();
        await handler.TryHandleAsync(appCtx, new ValidationException("Dữ liệu sai"), CancellationToken.None);
        Assert.Empty(recordingAlerts.Alerts);
        Assert.Equal(StatusCodes.Status400BadRequest, appCtx.Response.StatusCode);

        var crashCtx = new DefaultHttpContext { TraceIdentifier = "trace-500" };
        crashCtx.Request.Method = "GET";
        crashCtx.Request.Path = "/boom";
        crashCtx.Response.Body = new MemoryStream();
        await handler.TryHandleAsync(crashCtx, new InvalidOperationException("Unexpected failure"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, crashCtx.Response.StatusCode);
        var alert = Assert.Single(recordingAlerts.Alerts);
        Assert.Contains("HTTP 500", alert.Title, StringComparison.Ordinal);
        Assert.Contains("trace-500", alert.Body, StringComparison.Ordinal);
        Assert.Contains("/boom", alert.Body, StringComparison.Ordinal);

        // The same failure on the same endpoint is not announced again inside the throttle window.
        var repeatCtx = new DefaultHttpContext { TraceIdentifier = "trace-501" };
        repeatCtx.Request.Method = "GET";
        repeatCtx.Request.Path = "/boom/other-id";
        repeatCtx.Response.Body = new MemoryStream();
        await handler.TryHandleAsync(repeatCtx, new InvalidOperationException("Unexpected failure"), CancellationToken.None);
        Assert.Equal(StatusCodes.Status500InternalServerError, repeatCtx.Response.StatusCode);
        Assert.Single(recordingAlerts.Alerts);

        // A different kind of failure still gets through.
        var otherCtx = new DefaultHttpContext { TraceIdentifier = "trace-502" };
        otherCtx.Request.Method = "GET";
        otherCtx.Request.Path = "/boom";
        otherCtx.Response.Body = new MemoryStream();
        await handler.TryHandleAsync(otherCtx, new NotSupportedException("Other failure"), CancellationToken.None);
        Assert.Equal(2, recordingAlerts.Alerts.Count);
    }

    [Fact]
    public async Task EventBus_WorkSubmitted_PublishesNotificationToDirectManager()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var manager = new Employee { Id = Guid.NewGuid(), EmployeeCode = "MGR", FullName = "Manager", Email = "m@t", DepartmentId = Guid.NewGuid(), JobPositionId = Guid.NewGuid(), Status = EmployeeStatus.Active };
        var staff = new Employee { Id = Guid.NewGuid(), EmployeeCode = "EMP", FullName = "Staff", Email = "s@t", DepartmentId = manager.DepartmentId, JobPositionId = manager.JobPositionId, ManagerId = manager.Id, Status = EmployeeStatus.Active };
        db.Employees.AddRange(manager, staff);
        await db.SaveChangesAsync();

        var notifier = new RecordingNotifier();
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var bus = new EventBus(cache, NullLogger<EventBus>.Instance, db, notifier);

        var subId = Guid.NewGuid();
        await bus.PublishAsync(new WorkSubmittedEvent(subId, Guid.NewGuid(), staff.Id, 1, "WRK-100", "Mở sổ tiết kiệm", Now));

        var draft = Assert.Single(notifier.Drafts);
        Assert.Equal(manager.Id, draft.RecipientEmployeeId);
        Assert.Equal(NotificationType.SubmissionSubmitted, draft.Type);
        Assert.Equal(subId, draft.RelatedId);
    }

    [Fact]
    public async Task WorkTransactionService_NotifiesAssigneeAndManagerOnTransactionMismatch()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var manager = new Employee { Id = Guid.NewGuid(), EmployeeCode = "MGR", FullName = "Manager", Email = "m@t", DepartmentId = Guid.NewGuid(), JobPositionId = Guid.NewGuid(), Status = EmployeeStatus.Active };
        var assignee = new Employee { Id = Guid.NewGuid(), EmployeeCode = "EMP", FullName = "Teller", Email = "e@t", DepartmentId = manager.DepartmentId, JobPositionId = manager.JobPositionId, ManagerId = manager.Id, Status = EmployeeStatus.Active };
        var wt = new WorkType { Id = Guid.NewGuid(), Code = "WT1", Name = "Card", ProductCode = "CARD_GOLD", Status = RecordStatus.Active };
        var work = new Work { Id = Guid.NewGuid(), Code = "WRK-TX", Title = "Card sale", WorkTypeId = wt.Id, AssigneeEmployeeId = assignee.Id, Status = WorkStatus.InProgress, Priority = WorkPriority.Medium };
        var batch = new ImportBatch { Id = Guid.NewGuid(), FileName = "tx.xlsx", TotalRows = 1, ImportedRows = 1, ImportedBy = manager.Id, ImportedAt = Now };
        var imported = new ImportedTransaction { Id = Guid.NewGuid(), ImportBatchId = batch.Id, TransactionCode = "TX-WRONG-PROD", EmployeeId = assignee.Id, ProductCode = "DEPOSIT", TransactionDate = new DateOnly(2026, 11, 10), Amount = 1000m };
        db.AddRange(manager, assignee, wt, work, batch, imported);
        await db.SaveChangesAsync();

        var notifier = new RecordingNotifier();
        var service = new WorkTransactionService(db, TimeProvider.System, Options.Create(new SystemParametersOptions()), notifier);
        await service.AddAsync(work.Id, assignee.Id, new AddWorkTransactionsRequestDto(["TX-WRONG-PROD"]));

        Assert.Equal(2, notifier.Drafts.Count);
        Assert.Contains(notifier.Drafts, d => d.RecipientEmployeeId == assignee.Id && d.Type == NotificationType.TransactionMismatch);
        Assert.Contains(notifier.Drafts, d => d.RecipientEmployeeId == manager.Id && d.Type == NotificationType.TransactionMismatch);
    }

    private sealed class RecordingSystemAlerts : ISystemAlertNotifier
    {
        public List<(string Title, string Body)> Alerts { get; } = [];

        public Task NotifyAdminsAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            Alerts.Add((title, body));
            return Task.CompletedTask;
        }
    }
}
