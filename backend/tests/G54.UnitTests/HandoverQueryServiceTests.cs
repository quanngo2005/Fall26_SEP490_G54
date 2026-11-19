using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Handover;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class HandoverQueryServiceTests : IDisposable
{
    private readonly AppDbContext db;
    private readonly Guid deptId = Guid.NewGuid();
    private readonly Guid tellerPositionId = Guid.NewGuid();
    private readonly Guid salesPositionId = Guid.NewGuid();
    private readonly Guid managerId = Guid.NewGuid();
    private readonly Guid tellerId = Guid.NewGuid();
    private readonly Guid salesId = Guid.NewGuid();
    private readonly Guid outsiderId = Guid.NewGuid();
    private readonly Guid workTypeId = Guid.NewGuid();

    public HandoverQueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        db = new AppDbContext(options);
        Seed();
    }

    public void Dispose() => db.Dispose();

    [Fact]
    public async Task ListHandovers_Incoming_ReturnsRecipientRecords_WithKpiStatus()
    {
        var service = CreateService();

        var result = await service.ListHandoversAsync(salesId, new HandoverListQueryDto { Direction = "INCOMING" });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, x => x.WorkCode == "WK-101" && x.KpiStatus == "RECORDED" && x.IsReadOnly);
        Assert.Contains(result.Items, x => x.WorkCode == "WK-102" && x.KpiStatus == "PENDING" && !x.IsReadOnly);
    }

    [Fact]
    public async Task ListHandovers_FiltersByKeyword_KpiStatus_And_Direction()
    {
        var service = CreateService();

        var recordedOnly = await service.ListHandoversAsync(
            salesId,
            new HandoverListQueryDto { Direction = "INCOMING", KpiStatus = "RECORDED" });
        Assert.Single(recordedOnly.Items);
        Assert.Equal("WK-101", recordedOnly.Items[0].WorkCode);

        var byCif = await service.ListHandoversAsync(
            tellerId,
            new HandoverListQueryDto { Direction = "OUTGOING", Keyword = "CIF-999" });
        Assert.Single(byCif.Items);
        Assert.Equal("WK-102", byCif.Items[0].WorkCode);
    }

    [Fact]
    public async Task ListHandovers_InvalidDirectionOrStatus_ThrowsValidation()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListHandoversAsync(salesId, new HandoverListQueryDto { Direction = "SIDEWAYS" }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListHandoversAsync(salesId, new HandoverListQueryDto { Status = "UNKNOWN" }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListHandoversAsync(salesId, new HandoverListQueryDto { KpiStatus = "DONE" }));
    }

    [Fact]
    public async Task GetHandover_AllowsParticipantsAndManager_BlocksOutsider()
    {
        var service = CreateService();
        var handoverId = await db.TaskHandovers.Select(h => h.Id).FirstAsync();

        var asRecipient = await service.GetHandoverAsync(handoverId, salesId);
        Assert.Equal(handoverId, asRecipient.Id);

        var asManager = await service.GetHandoverAsync(handoverId, managerId);
        Assert.Equal(handoverId, asManager.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetHandoverAsync(handoverId, outsiderId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetHandoverAsync(Guid.NewGuid(), salesId));
    }

    private HandoverService CreateService() => new(
        db,
        new StubPermissionService(),
        new NullNotifier(),
        Options.Create(new SystemParametersOptions()),
        NullLogger<HandoverService>.Instance,
        TimeProvider.System);

    private void Seed()
    {
        var now = DateTimeOffset.UtcNow;
        db.Departments.Add(new Department
        {
            Id = deptId,
            Code = "PGD1",
            Name = "Phòng Giao dịch 1",
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        });
        db.JobPositions.AddRange(
            new JobPosition { Id = tellerPositionId, Code = "TELLER", Title = "Giao dịch viên", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now },
            new JobPosition { Id = salesPositionId, Code = "SALES", Title = "CV bán hàng", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now });
        db.Employees.AddRange(
            new Employee
            {
                Id = managerId,
                EmployeeCode = "MGR1",
                FullName = "Quản lý",
                Email = "mgr@bpms.local",
                DepartmentId = deptId,
                JobPositionId = salesPositionId,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            },
            new Employee
            {
                Id = tellerId,
                EmployeeCode = "NV01",
                FullName = "Nguyễn Văn A",
                Email = "nva@bpms.local",
                DepartmentId = deptId,
                JobPositionId = tellerPositionId,
                ManagerId = managerId,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            },
            new Employee
            {
                Id = salesId,
                EmployeeCode = "NV02",
                FullName = "Phạm Văn D",
                Email = "pvd@bpms.local",
                DepartmentId = deptId,
                JobPositionId = salesPositionId,
                ManagerId = managerId,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            },
            new Employee
            {
                Id = outsiderId,
                EmployeeCode = "NV99",
                FullName = "Người ngoài",
                Email = "other@bpms.local",
                DepartmentId = deptId,
                JobPositionId = salesPositionId,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            });
        db.WorkTypes.Add(new WorkType
        {
            Id = workTypeId,
            Code = "WT1",
            Name = "Mở thẻ tín dụng",
            Status = RecordStatus.Active,
            UpdatedAt = now,
        });

        var w1 = new Work
        {
            Id = Guid.NewGuid(),
            WorkTypeId = workTypeId,
            AssigneeEmployeeId = salesId,
            CreatedBy = tellerId,
            Code = "WK-101",
            Title = "Mở thẻ tín dụng",
            CustomerName = "Trần Thị B",
            CustomerCif = "CIF-001",
            Priority = WorkPriority.Medium,
            Status = WorkStatus.HandedOver,
            AssignedAt = now,
            UpdatedAt = now,
        };
        var w2 = new Work
        {
            Id = Guid.NewGuid(),
            WorkTypeId = workTypeId,
            AssigneeEmployeeId = tellerId,
            CreatedBy = tellerId,
            Code = "WK-102",
            Title = "Hồ sơ vay vốn",
            CustomerName = "Lê Văn C",
            CustomerCif = "CIF-999",
            Priority = WorkPriority.High,
            Status = WorkStatus.InProgress,
            AssignedAt = now,
            UpdatedAt = now,
        };
        db.Works.AddRange(w1, w2);

        var sub1 = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = w1.Id,
            SubmitterEmployeeId = salesId,
            SubmissionNo = 1,
            SubmittedAt = now,
            UpdatedAt = now,
            Status = SubmissionStatus.Approved,
        };
        var ver1 = new Verification
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = sub1.Id,
            VerifierEmployeeId = managerId,
            Decision = VerificationDecision.Approved,
            VerifiedAt = now,
            UpdatedAt = now,
        };
        db.ResultSubmissions.Add(sub1);
        db.Verifications.Add(ver1);
        db.VerifiedWorkResults.Add(new VerifiedWorkResult
        {
            Id = Guid.NewGuid(),
            VerificationId = ver1.Id,
            VerifiedAt = now,
            UpdatedAt = now,
            Status = VerifiedResultStatus.Valid,
        });

        db.TaskHandovers.AddRange(
            new TaskHandover
            {
                Id = Guid.NewGuid(),
                WorkId = w1.Id,
                FromEmployeeId = tellerId,
                ToEmployeeId = salesId,
                Reason = "Khách hàng VIP cần tư vấn chuyên sâu",
                ReferrerSharePercent = 30m,
                Status = HandoverStatus.Accepted,
                HandoverAt = now.AddHours(-2),
                AcceptedAt = now.AddHours(-1),
                UpdatedAt = now.AddHours(-1),
            },
            new TaskHandover
            {
                Id = Guid.NewGuid(),
                WorkId = w2.Id,
                FromEmployeeId = tellerId,
                ToEmployeeId = salesId,
                Reason = "Đề nghị hỗ trợ xử lý",
                ReferrerSharePercent = 0m,
                Status = HandoverStatus.Pending,
                HandoverAt = now,
                UpdatedAt = now,
            });
        db.SaveChanges();
    }

    private sealed class StubPermissionService : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NullNotifier : INotifier
    {
        public Task PublishAsync(IReadOnlyCollection<NotificationDraft> drafts, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task TryPublishAsync(IReadOnlyCollection<NotificationDraft> drafts, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
