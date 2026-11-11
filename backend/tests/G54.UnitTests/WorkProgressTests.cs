using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Work;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary><c>PATCH /works/{id}/progress</c> (GB-779): the assignee reports how far the work is; a cross-role transfer reads it.</summary>
public sealed class WorkProgressTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T08:00:00+00:00", CultureInfo.InvariantCulture);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public void Dispose() => _db.Dispose();

    private WorkService Service => new(
        new UnitOfWork(_db),
        NullLogger<WorkService>.Instance,
        new FixedTimeProvider(),
        new FakePermissionService("Own"),
        new RecordingNotifier());

    private async Task<(Employee Assignee, Work Work)> SeedAsync(WorkStatus status = WorkStatus.InProgress, string code = "EMP-1")
    {
        var assignee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Employee {code}",
            Email = $"{code.ToLowerInvariant()}@bank.com",
            Status = EmployeeStatus.Active,
        };
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"W-{code}",
            Title = "Work",
            AssigneeEmployeeId = assignee.Id,
            Priority = WorkPriority.Medium,
            Status = status,
            UpdatedAt = Now.AddDays(-1),
        };
        _db.Employees.Add(assignee);
        _db.Works.Add(work);
        await _db.SaveChangesAsync();
        return (assignee, work);
    }

    [Fact]
    public async Task UpdateProgress_ByTheAssignee_SavesItRoundedAndReturnsItInTheDetail()
    {
        var (assignee, work) = await SeedAsync();

        var detail = await Service.UpdateProgressAsync(work.Id, assignee.Id, new UpdateWorkProgressDto { ProgressPercent = 92.456m });

        Assert.Equal(92.46m, detail.ProgressPercent);
        var stored = await _db.Works.SingleAsync();
        Assert.Equal((92.46m, Now), (stored.ProgressPercent, stored.UpdatedAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task UpdateProgress_AtTheBounds_IsAccepted(double percent)
    {
        var (assignee, work) = await SeedAsync();

        var detail = await Service.UpdateProgressAsync(work.Id, assignee.Id, new UpdateWorkProgressDto { ProgressPercent = (decimal)percent });

        Assert.Equal((decimal)percent, detail.ProgressPercent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public async Task UpdateProgress_OutsideZeroToHundred_Returns400AndKeepsTheOldValue(double? percent)
    {
        var (assignee, work) = await SeedAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Service.UpdateProgressAsync(work.Id, assignee.Id, new UpdateWorkProgressDto { ProgressPercent = (decimal?)percent }));

        Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
        Assert.Equal(0m, (await _db.Works.SingleAsync()).ProgressPercent);
    }

    [Fact]
    public async Task UpdateProgress_ByAnotherEmployee_Returns403()
    {
        var (_, work) = await SeedAsync();
        var (other, _) = await SeedAsync(code: "EMP-2");

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service.UpdateProgressAsync(work.Id, other.Id, new UpdateWorkProgressDto { ProgressPercent = 50m }));

        Assert.Equal(ErrorCodes.Forbidden, ex.ErrorCode);
        Assert.Equal(0m, (await _db.Works.SingleAsync(w => w.Id == work.Id)).ProgressPercent);
    }

    [Fact]
    public async Task UpdateProgress_OfAnUnknownWork_Returns404()
    {
        var (assignee, _) = await SeedAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            Service.UpdateProgressAsync(Guid.NewGuid(), assignee.Id, new UpdateWorkProgressDto { ProgressPercent = 50m }));

        Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
    }

    [Theory]
    [InlineData(WorkStatus.New)]
    [InlineData(WorkStatus.InProgress)]
    [InlineData(WorkStatus.HandedOver)]
    public async Task UpdateProgress_OfWorkStillBeingDone_IsAllowed(WorkStatus status)
    {
        var (assignee, work) = await SeedAsync(status);

        var detail = await Service.UpdateProgressAsync(work.Id, assignee.Id, new UpdateWorkProgressDto { ProgressPercent = 40m });

        Assert.Equal(40m, detail.ProgressPercent);
    }

    [Theory]
    [InlineData(WorkStatus.Submitted)]
    [InlineData(WorkStatus.Verified)]
    [InlineData(WorkStatus.Done)]
    [InlineData(WorkStatus.Cancelled)]
    [InlineData(WorkStatus.Overdue)]
    public async Task UpdateProgress_OfFinishedOrLateWork_Returns409(WorkStatus status)
    {
        var (assignee, work) = await SeedAsync(status);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Service.UpdateProgressAsync(work.Id, assignee.Id, new UpdateWorkProgressDto { ProgressPercent = 40m }));

        Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
        Assert.Equal(0m, (await _db.Works.SingleAsync()).ProgressPercent);
    }

    [Fact]
    public async Task UpdateProgress_ResolvesTheCallerByAccountId()
    {
        var (assignee, work) = await SeedAsync();
        var account = new Account { Id = Guid.NewGuid(), EmployeeId = assignee.Id, Username = "u", PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();

        var detail = await Service.UpdateProgressAsync(work.Id, account.Id, new UpdateWorkProgressDto { ProgressPercent = 75m });

        Assert.Equal(75m, detail.ProgressPercent);
    }

    [Fact]
    public async Task GetById_ReturnsTheStoredProgress()
    {
        var (assignee, work) = await SeedAsync();
        var account = new Account { Id = Guid.NewGuid(), EmployeeId = assignee.Id, Username = "u", PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now };
        _db.Accounts.Add(account);
        work.ProgressPercent = 33.5m;
        await _db.SaveChangesAsync();

        var detail = await Service.GetByIdAsync(work.Id, account.Id);

        Assert.Equal(33.5m, detail!.ProgressPercent);
    }
    [Fact]
    public async Task Controller_ForwardsTheWorkIdBodyAndCallerAndWrapsTheDetail()
    {
        var userId = Guid.NewGuid();
        var service = new RecordingWorkService();
        var body = new UpdateWorkProgressDto { ProgressPercent = 70m };
        var controller = new WorksController(service)
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
        var workId = Guid.NewGuid();

        var result = await controller.UpdateProgress(workId, body, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((workId, userId), (service.Id, service.UserId));
        Assert.Same(body, service.Body);
        Assert.Equal(70m, Assert.IsType<ApiResponse<WorkDetailDto>>(ok.Value).Data!.ProgressPercent);
    }

    [Fact]
    public void Controller_RoutesAPatchOnProgressAndRequiresWorkRead()
    {
        var method = typeof(WorksController).GetMethod(nameof(WorksController.UpdateProgress))!;

        Assert.Equal("{id:guid}/progress", method.GetCustomAttribute<HttpPatchAttribute>()!.Template);
        var permission = method.GetCustomAttribute<HasPermissionAttribute>()!;
        Assert.Equal(("work", "read", "Own"), (permission.Resource, permission.Action, permission.Scope));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingWorkService : IWorkService
    {
        public Guid Id { get; private set; }

        public Guid UserId { get; private set; }

        public UpdateWorkProgressDto? Body { get; private set; }

        public Task<WorkDetailDto> UpdateProgressAsync(Guid id, Guid userId, UpdateWorkProgressDto dto, CancellationToken cancellationToken = default)
        {
            (Id, UserId, Body) = (id, userId, dto);
            return Task.FromResult(new WorkDetailDto(
                id, "W-1", "Work", null, null, null, null, null, Guid.NewGuid(), null, null, "Medium", "InProgress", null, null, null, Now, dto.ProgressPercent!.Value));
        }

        public Task<WorkDetailDto> CreateWorkAsync(CreateWorkRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<WorkListItemDto>> GetWorksAsync(Guid accountId, WorkFilterDto filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkDetailDto?> GetByIdAsync(Guid id, Guid accountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkDetailDto> ChangeStatusAsync(Guid id, Guid userId, WorkStatusDto dto, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> MarkOverdueAsync(DateOnly today, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
