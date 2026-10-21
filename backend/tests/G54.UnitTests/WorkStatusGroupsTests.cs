using G54.BLL.Common.Constants;
using G54.BLL.Common.Workflow;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class WorkStatusGroupsTests
{
    [Fact]
    public void OpenAndCompleted_AreDisjointAndExcludeCancelled()
    {
        Assert.Empty(WorkStatusGroups.Open.Intersect(WorkStatusGroups.Completed));
        Assert.DoesNotContain(WorkStatus.Cancelled, WorkStatusGroups.Open);
        Assert.DoesNotContain(WorkStatus.Cancelled, WorkStatusGroups.Completed);
    }

    [Fact]
    public void EveryStatusIsOpenCompletedOrCancelled()
    {
        foreach (var status in Enum.GetValues<WorkStatus>())
        {
            var covered = WorkStatusGroups.Open.Contains(status)
                || WorkStatusGroups.Completed.Contains(status)
                || status == WorkStatus.Cancelled;
            Assert.True(covered, $"{status} is not classified");
        }
    }

    [Fact]
    public void OverdueCountsAsOpenInExecutionAndLoad()
    {
        Assert.Contains(WorkStatus.Overdue, WorkStatusGroups.Open);
        Assert.Contains(WorkStatus.Overdue, WorkStatusGroups.InExecution);
        Assert.Contains(WorkStatus.Overdue, WorkStatusGroups.ActiveLoad);
    }

    [Fact]
    public void OverdueEligibleWork_IsAlwaysOpenAndCanBecomeOverdue()
    {
        foreach (var status in WorkStatusGroups.OverdueEligible)
        {
            Assert.Contains(status, WorkStatusGroups.Open);
            Assert.True(WorkStateMachine.TryGetNextStatus(status, WorkAction.MarkOverdue, out var next));
            Assert.Equal(WorkStatus.Overdue, next);
        }
    }

    [Fact]
    public void OnlyOverdueEligibleStatusesAcceptTheMarkOverdueAction()
    {
        foreach (var status in Enum.GetValues<WorkStatus>())
        {
            var allowed = WorkStateMachine.TryGetNextStatus(status, WorkAction.MarkOverdue, out _);

            Assert.Equal(WorkStatusGroups.OverdueEligible.Contains(status), allowed);
        }
    }

    [Fact]
    public async Task Groups_TranslateToQueriesOverTheDatabase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new AppDbContext(options);

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "EMP200",
            FullName = "Employee 200",
            Email = "emp200@bank.com",
            Status = EmployeeStatus.Active,
        };
        context.Employees.Add(employee);
        foreach (var status in Enum.GetValues<WorkStatus>())
        {
            context.Works.Add(new Work
            {
                Id = Guid.NewGuid(),
                Code = $"W-{status}",
                Title = $"Work {status}",
                AssigneeEmployeeId = employee.Id,
                Priority = WorkPriority.Medium,
                Status = status,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await context.SaveChangesAsync();

        Assert.Equal(WorkStatusGroups.Open.Length, await context.Works.CountAsync(w => WorkStatusGroups.Open.Contains(w.Status)));
        Assert.Equal(WorkStatusGroups.Completed.Length, await context.Works.CountAsync(w => WorkStatusGroups.Completed.Contains(w.Status)));
        Assert.Equal(WorkStatusGroups.InExecution.Length, await context.Works.CountAsync(w => WorkStatusGroups.InExecution.Contains(w.Status)));
    }
}
