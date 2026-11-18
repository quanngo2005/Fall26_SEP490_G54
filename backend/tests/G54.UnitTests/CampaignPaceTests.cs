using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

/// <summary>Campaign pace figures of the list (docs/api/BRANCH_DASHBOARD_CONTRACT.md §3).</summary>
public sealed class CampaignPaceTests
{
    // 2026-10-17 17:30 UTC is already 2026-10-18 in Vietnam (UTC+7), the business day the pace uses.
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 17, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(4, 1, 25)]
    [InlineData(3, 2, 66.7)]
    public void ProgressIsTheShareOfCompletedWorks(int total, int completed, decimal expected)
    {
        Assert.Equal(expected, CampaignService.ProgressPercent(total, completed));
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-10", "2026-09-30", 0)]
    [InlineData("2026-10-01", "2026-10-10", "2026-10-01", 10)]
    [InlineData("2026-10-01", "2026-10-10", "2026-10-05", 50)]
    [InlineData("2026-10-01", "2026-10-10", "2026-10-20", 100)]
    [InlineData("2026-10-01", "2026-10-03", "2026-10-02", 66.7)]
    public void TimeElapsedCountsStartedDaysAndIsClamped(string start, string end, string today, decimal expected)
    {
        Assert.Equal(
            expected,
            CampaignService.TimeElapsedPercent(DateOnly.Parse(start, null), DateOnly.Parse(end, null), DateOnly.Parse(today, null)));
    }

    [Fact]
    public async Task ListAddsPaceKpiAndCountsSlowCampaignsOverEveryPage()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var seed = await SeedAsync(db);
        var service = new CampaignService(db, NullLogger<CampaignService>.Instance, new FixedTime(Now));

        var result = await service.GetCampaignsAsync(null, "RUNNING", null, 1, 1);

        Assert.Equal(2, result.Campaigns.TotalCount);
        Assert.Equal(1, result.Metrics.SlowCampaignCount); // counted over both pages
        var slow = (await service.GetCampaignsAsync(null, "RUNNING", null, 1, 10)).Campaigns.Items
            .Single(c => c.Code == "SLOW");
        Assert.Equal(25m, slow.ProgressPercent); // 1 of 4 works done
        Assert.Equal(60m, slow.TimeElapsedPercent); // day 6 of 10
        Assert.Equal(-35m, slow.PaceGap);
        Assert.Equal("HDV", slow.KpiCode);
        Assert.Equal("Huy động vốn", slow.KpiName);
        Assert.Equal("tỷ", slow.TargetUnit);
        Assert.Equal(40m, slow.TargetValue); // 4 works × 10
        Assert.Equal(7m, slow.ActualValue); // the valid result only
        Assert.Equal(seed.SlowId, slow.Id);

        var fast = (await service.GetCampaignsAsync(null, "RUNNING", null, 1, 10)).Campaigns.Items
            .Single(c => c.Code == "FAST");
        Assert.True(fast.PaceGap >= 0);
        Assert.Null(fast.KpiName); // its work types map to no KPI
        Assert.Null(fast.TargetValue);
        Assert.Null(fast.ActualValue);
    }

    private static async Task<(Guid SlowId, Guid FastId)> SeedAsync(AppDbContext db)
    {
        var stamp = Now;
        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ",
            Name = "OBJ",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = stamp,
        };
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "G", UpdatedAt = stamp, Status = KpiStatus.Active };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "HDV",
            Name = "Huy động vốn",
            Unit = "tỷ",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            UpdatedAt = stamp,
            Status = KpiStatus.Active,
        };
        var measured = new WorkType { Id = Guid.NewGuid(), Code = "WT1", Name = "WT1", Status = RecordStatus.Active, UpdatedAt = stamp };
        var unmeasured = new WorkType { Id = Guid.NewGuid(), Code = "WT2", Name = "WT2", Status = RecordStatus.Active, UpdatedAt = stamp };
        var employeeId = Guid.NewGuid();

        var slow = Campaign(objective, "SLOW", new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 22), stamp);
        var fast = Campaign(objective, "FAST", new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), stamp);
        db.AddRange(objective, group, kpi, measured, unmeasured, slow, fast);
        db.WorkTypeKpis.Add(new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = measured.Id,
            KpiDefinitionId = kpi.Id,
            IsRequired = true,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            UpdatedAt = stamp,
        });
        db.CampaignWorkTypes.AddRange(
            new CampaignWorkType { Id = Guid.NewGuid(), BusinessCampaignId = slow.Id, WorkTypeId = measured.Id, UpdatedAt = stamp },
            new CampaignWorkType { Id = Guid.NewGuid(), BusinessCampaignId = fast.Id, WorkTypeId = unmeasured.Id, UpdatedAt = stamp });

        var slowWorks = Enumerable.Range(1, 4)
            .Select(i => Work(slow.Id, $"S{i}", i == 1 ? WorkStatus.Done : WorkStatus.InProgress, employeeId, stamp))
            .ToList();
        db.Works.AddRange(slowWorks);
        db.Works.Add(Work(fast.Id, "F1", WorkStatus.Done, employeeId, stamp));

        var target = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpi.Id,
            EmployeeId = employeeId,
            EvaluationPeriodId = Guid.NewGuid(),
            Version = 1,
            TargetValue = 40,
            Status = TargetAssignmentStatus.Active,
            CreatedAt = stamp,
            UpdatedAt = stamp,
        };
        var progress = new ActualProgress
        {
            Id = Guid.NewGuid(),
            TargetAssignmentId = target.Id,
            ProgressDate = new DateOnly(2026, 10, 15),
            Value = 9,
            Status = ActualProgressStatus.Valid,
            UpdatedAt = stamp,
        };
        db.AddRange(target, progress);
        AddResult(db, slowWorks[0], employeeId, progress, 7m, VerifiedResultStatus.Valid, stamp);
        AddResult(db, slowWorks[1], employeeId, progress, 2m, VerifiedResultStatus.Invalid, stamp);

        await db.SaveChangesAsync();
        return (slow.Id, fast.Id);
    }

    private static BusinessCampaign Campaign(BusinessObjective objective, string code, DateOnly start, DateOnly end, DateTimeOffset stamp) => new()
    {
        Id = Guid.NewGuid(),
        BusinessObjectiveId = objective.Id,
        Code = code,
        Name = code,
        StartDate = start,
        EndDate = end,
        Status = BusinessCampaignStatus.Running,
        UpdatedAt = stamp,
    };

    private static Work Work(Guid campaignId, string code, WorkStatus status, Guid employeeId, DateTimeOffset stamp) => new()
    {
        Id = Guid.NewGuid(),
        BusinessCampaignId = campaignId,
        Code = code,
        Title = code,
        AssigneeEmployeeId = employeeId,
        CreatedBy = employeeId,
        Status = status,
        Priority = WorkPriority.Medium,
        TargetValue = 10m,
        UpdatedAt = stamp,
    };

    private static void AddResult(
        AppDbContext db, Work work, Guid employeeId, ActualProgress progress, decimal value, VerifiedResultStatus status, DateTimeOffset stamp)
    {
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = employeeId,
            SubmissionNo = 1,
            SubmittedAt = stamp,
            UpdatedAt = stamp,
            Status = SubmissionStatus.Approved,
        };
        var verification = new Verification
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            VerifierEmployeeId = employeeId,
            RoundNo = 1,
            UpdatedAt = stamp,
            Decision = VerificationDecision.Approved,
        };
        var verified = new VerifiedWorkResult
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            VerifiedAt = stamp,
            UpdatedAt = stamp,
            Status = status,
        };
        db.AddRange(submission, verification, verified);
        db.WorkResultItems.Add(new WorkResultItem
        {
            Id = Guid.NewGuid(),
            VerifiedWorkResultId = verified.Id,
            ActualProgressId = progress.Id,
            EmployeeId = employeeId,
            Value = value,
            RawValue = value,
            UpdatedAt = stamp,
        });
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
