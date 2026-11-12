using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Quality;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class QaDashboardServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 15, 2, TimeSpan.FromHours(7));
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private Department _branch = null!;
    private Department _tellerDept = null!;
    private Department _salesDept = null!;
    private JobPosition _tellerPos = null!;
    private JobPosition _salesPos = null!;
    private Employee _teller1 = null!;
    private Employee _teller2 = null!;
    private Employee _sales1 = null!;
    private EvaluationPeriod _period = null!;

    public void Dispose() => _db.Dispose();

    private QaDashboardService CreateService(DateTimeOffset? testNow = null) =>
        new(_db, new FixedTime(testNow ?? Now));

    private async Task SeedDataAsync()
    {
        _branch = new Department
        {
            Id = Guid.NewGuid(),
            Code = "BR01",
            Name = "Chi nhánh Tân Bình",
            LevelType = DepartmentLevelType.Branch,
            Status = RecordStatus.Active,
            LastSyncAt = Now,
        };
        _tellerDept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "D01",
            Name = "Phòng Dịch vụ Khách hàng",
            LevelType = DepartmentLevelType.Department,
            ParentDepartmentId = _branch.Id,
            Status = RecordStatus.Active,
            LastSyncAt = Now,
        };
        _salesDept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "D02",
            Name = "Phòng Khách hàng Cá nhân",
            LevelType = DepartmentLevelType.Department,
            ParentDepartmentId = _branch.Id,
            Status = RecordStatus.Active,
            LastSyncAt = Now,
        };
        _db.Departments.AddRange(_branch, _tellerDept, _salesDept);

        _tellerPos = new JobPosition { Id = Guid.NewGuid(), Code = "TELLER", Title = "Giao dịch viên", Status = RecordStatus.Active, LastSyncAt = Now };
        _salesPos = new JobPosition { Id = Guid.NewGuid(), Code = "SALES", Title = "Chuyên viên Khách hàng cá nhân", Status = RecordStatus.Active, LastSyncAt = Now };
        _db.JobPositions.AddRange(_tellerPos, _salesPos);

        _teller1 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0231",
            FullName = "Nguyễn Minh Anh",
            Email = "nv0231@progest.bank",
            DepartmentId = _tellerDept.Id,
            JobPositionId = _tellerPos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
        };
        _teller2 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0244",
            FullName = "Lê Thu Hoa",
            Email = "nv0244@progest.bank",
            DepartmentId = _tellerDept.Id,
            JobPositionId = _tellerPos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
        };
        _sales1 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0310",
            FullName = "Trần Quốc Bảo",
            Email = "nv0310@progest.bank",
            DepartmentId = _salesDept.Id,
            JobPositionId = _salesPos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
        };
        _db.Employees.AddRange(_teller1, _teller2, _sales1);

        _period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "PER-2026-10",
            Name = "10/2026",
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            PeriodType = PeriodType.Month,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(_period);

        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetDashboardAsync_MatchesMockupMetrics_CalculatesCardsAndBanner()
    {
        await SeedDataAsync();

        // 7 NotMatched transactions (oldest 4 days ago)
        var work1 = CreateWork(_teller1, "WRK-001");
        var work2 = CreateWork(_sales1, "WRK-002");
        var work3 = CreateWork(_teller2, "WRK-003");
        _db.Works.AddRange(work1, work2, work3);

        var fourDaysAgo = Now.AddDays(-4);

        // 3 NotMatched for NV0231
        AddWorkTx(work1, "TX-01", WorkTransactionStatus.NotMatched, "EMPLOYEE_MISMATCH", fourDaysAgo);
        AddWorkTx(work1, "TX-02", WorkTransactionStatus.NotMatched, "EMPLOYEE_MISMATCH", Now.AddDays(-3));
        AddWorkTx(work1, "TX-03", WorkTransactionStatus.NotMatched, "EMPLOYEE_MISMATCH", Now.AddDays(-2));

        // 2 NotMatched for NV0310
        AddWorkTx(work2, "TX-04", WorkTransactionStatus.NotMatched, "PRODUCT_MISMATCH", Now.AddDays(-2));
        AddWorkTx(work2, "TX-05", WorkTransactionStatus.NotMatched, "PRODUCT_MISMATCH", Now.AddDays(-1));

        // 1 NotMatched for NV0244
        AddWorkTx(work3, "TX-06", WorkTransactionStatus.NotMatched, "REFER_ID_MISSING", Now.AddDays(-1));

        // 1 NotMatched (amount mismatch)
        AddWorkTx(work1, "TX-07", WorkTransactionStatus.NotMatched, "AMOUNT_MISMATCH", Now.AddHours(-10));

        // 12 InReview transactions
        for (var i = 1; i <= 12; i++)
        {
            AddWorkTx(work1, $"TX-REV-{i}", WorkTransactionStatus.InReview, null, Now.AddDays(-1));
        }

        // Matched transactions
        AddWorkTx(work1, "TX-MATCH-1", WorkTransactionStatus.Matched, null, Now.AddDays(-1));
        AddWorkTx(work2, "TX-MATCH-2", WorkTransactionStatus.Matched, null, Now.AddDays(-1));

        // QA Queue: 5 samples, 4 flags
        for (var i = 1; i <= 5; i++)
        {
            _db.QaSamples.Add(new QaSample
            {
                Id = Guid.NewGuid(),
                CodeNo = i,
                ResultSubmissionId = Guid.NewGuid(),
                SubmitterEmployeeId = _teller1.Id,
                Status = QaSampleStatus.Pending,
                SampledAt = Now.AddDays(-1),
            });
        }

        for (var i = 1; i <= 4; i++)
        {
            _db.QualityFlags.Add(new QualityFlag
            {
                Id = Guid.NewGuid(),
                CodeNo = 100 + i,
                ResultSubmissionId = Guid.NewGuid(),
                EmployeeId = i <= 2 ? _sales1.Id : _teller1.Id,
                FlaggedByEmployeeId = _teller2.Id,
                Status = QualityFlagStatus.Open,
                Severity = QualityFlagSeverity.Medium,
                Reason = "Sai chứng từ đính kèm",
                CreatedAt = Now.AddDays(-2),
                UpdatedAt = Now.AddDays(-2),
            });
        }

        // 3 Risk events, penalty points: 3 + 3 + 2 = 8
        _db.RiskEvents.Add(new RiskEvent
        {
            Id = Guid.NewGuid(),
            Code = "RSK-01",
            CodeNo = 1,
            EmployeeId = _teller1.Id,
            RecordedBy = _teller2.Id,
            EventType = "VIOLATION",
            Description = "Lỗi nghiệp vụ",
            Severity = RiskEventSeverity.Low,
            PenaltyPoints = 3m,
            OccurredAt = Now.AddDays(-5),
            RecordedAt = Now.AddDays(-5),
            UpdatedAt = Now.AddDays(-5),
        });
        _db.RiskEvents.Add(new RiskEvent
        {
            Id = Guid.NewGuid(),
            Code = "RSK-02",
            CodeNo = 2,
            EmployeeId = _sales1.Id,
            RecordedBy = _teller2.Id,
            EventType = "VIOLATION",
            Description = "Vi phạm quy trình",
            Severity = RiskEventSeverity.Low,
            PenaltyPoints = 3m,
            OccurredAt = Now.AddDays(-3),
            RecordedAt = Now.AddDays(-3),
            UpdatedAt = Now.AddDays(-3),
        });
        _db.RiskEvents.Add(new RiskEvent
        {
            Id = Guid.NewGuid(),
            Code = "RSK-03",
            CodeNo = 3,
            EmployeeId = _teller2.Id,
            RecordedBy = _teller1.Id,
            EventType = "VIOLATION",
            Description = "Chậm xử lý",
            Severity = RiskEventSeverity.Low,
            PenaltyPoints = null,
            OccurredAt = Now.AddDays(-1),
            RecordedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1),
        });

        await _db.SaveChangesAsync();

        var service = CreateService();

        // Act
        var result = await service.GetDashboardAsync(new QaDashboardFilterDto());

        // Assert - Period & Banner
        Assert.Equal("10/2026", result.Period.Name);
        Assert.False(result.Period.IsLocked);
        Assert.Equal(12, result.Banner.InReviewCount);
        Assert.True(result.Banner.HasUnmatchedInReview);
        Assert.Contains("Còn 12 kết quả In Review", result.Banner.Message, StringComparison.Ordinal);
        Assert.Contains("BR-29", result.Banner.Warning, StringComparison.Ordinal);

        // Assert - Cards
        Assert.Equal(7, result.KpiCards.NotMatched.Count);
        Assert.Equal(4, result.KpiCards.NotMatched.OldestDays);
        Assert.Contains("cũ nhất 4 ngày", result.KpiCards.NotMatched.Subtext, StringComparison.Ordinal);

        Assert.Equal(12, result.KpiCards.InReview.Count);
        Assert.Equal("chờ GD import (MSG13)", result.KpiCards.InReview.Subtext);

        Assert.Equal(9, result.KpiCards.QaQueue.Total);
        Assert.Equal(5, result.KpiCards.QaQueue.SampleCount);
        Assert.Equal(4, result.KpiCards.QaQueue.FlagCount);
        Assert.Equal("5 mẫu · 4 cờ", result.KpiCards.QaQueue.Subtext);

        Assert.Equal(3, result.KpiCards.RiskEvents.Count);
        Assert.Equal(8.0m, result.KpiCards.RiskEvents.TotalPenaltyPoints);
        Assert.Equal("tổng phạt -8 điểm", result.KpiCards.RiskEvents.Subtext);

        // Assert - Chart
        Assert.Equal(8, result.MatchedRateChart.Weeks.Count);
        Assert.NotNull(result.MatchedRateChart.CurrentWeekLabel);

        // Assert - Top discrepancy employees
        Assert.True(result.TopDiscrepancyEmployees.Count >= 3);
        var firstEmp = result.TopDiscrepancyEmployees[0];
        Assert.Equal("NV0231", firstEmp.EmployeeCode);
        Assert.Equal(4, firstEmp.DiscrepancyCount);

        var secondEmp = result.TopDiscrepancyEmployees[1];
        Assert.Equal("NV0310", secondEmp.EmployeeCode);
        Assert.Equal(2, secondEmp.DiscrepancyCount);
        Assert.Equal(2, secondEmp.FlagCount);

        // Assert - Common reasons
        Assert.Contains(result.CommonDiscrepancyReasons, r => r.ReasonCode == "EMPLOYEE_MISMATCH" && r.Count == 3);
        Assert.Contains(result.CommonDiscrepancyReasons, r => r.ReasonCode == "PRODUCT_MISMATCH" && r.Count == 2);
        Assert.Contains(result.CommonDiscrepancyReasons, r => r.ReasonCode == "NO_IMPORTED_TRANSACTION" && r.Count == 12);
    }

    [Fact]
    public async Task GetDashboardAsync_FilterByDepartment_OnlyIncludesDepartmentMetrics()
    {
        await SeedDataAsync();

        var workTeller = CreateWork(_teller1, "WRK-T1");
        var workSales = CreateWork(_sales1, "WRK-S1");
        _db.Works.AddRange(workTeller, workSales);

        AddWorkTx(workTeller, "TX-T1", WorkTransactionStatus.NotMatched, "EMPLOYEE_MISMATCH", Now.AddDays(-1));
        AddWorkTx(workSales, "TX-S1", WorkTransactionStatus.NotMatched, "PRODUCT_MISMATCH", Now.AddDays(-1));

        await _db.SaveChangesAsync();

        var service = CreateService();

        // Filter by Teller Department
        var result = await service.GetDashboardAsync(new QaDashboardFilterDto(DepartmentId: _tellerDept.Id));

        Assert.Equal(1, result.KpiCards.NotMatched.Count);
        Assert.Single(result.TopDiscrepancyEmployees);
        Assert.Equal("NV0231", result.TopDiscrepancyEmployees[0].EmployeeCode);
    }

    [Fact]
    public async Task GetDashboardAsync_UnknownPeriod_ThrowsNotFoundException()
    {
        await SeedDataAsync();
        var service = CreateService();

        var missingId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetDashboardAsync(new QaDashboardFilterDto(PeriodId: missingId)));
    }

    [Fact]
    public async Task GetReconciliationsAsync_FiltersAndPaginatesSuccessfully()
    {
        await SeedDataAsync();

        var work = CreateWork(_teller1, "WRK-REC-01");
        _db.Works.Add(work);

        for (var i = 1; i <= 25; i++)
        {
            AddWorkTx(work, $"TX-PAGED-{i:D2}", i <= 15 ? WorkTransactionStatus.NotMatched : WorkTransactionStatus.Matched, "EMPLOYEE_MISMATCH", Now.AddDays(-1));
        }

        await _db.SaveChangesAsync();

        var service = CreateService();

        // Page 1
        var paged = await service.GetReconciliationsAsync(new QaReconciliationFilterDto(
            Status: "NOT_MATCHED",
            Page: 1,
            PageSize: 10));

        Assert.Equal(15, paged.TotalCount);
        Assert.Equal(10, paged.Items.Count);
        Assert.All(paged.Items, i => Assert.Equal("NOT_MATCHED", i.Status));

        // Page 2
        var page2 = await service.GetReconciliationsAsync(new QaReconciliationFilterDto(
            Status: "NOT_MATCHED",
            Page: 2,
            PageSize: 10));

        Assert.Equal(5, page2.Items.Count);
    }

    [Fact]
    public async Task GetFilterOptionsAsync_ReturnsPopulatedOptions()
    {
        await SeedDataAsync();
        var service = CreateService();

        var options = await service.GetFilterOptionsAsync();

        Assert.NotEmpty(options.Periods);
        Assert.NotEmpty(options.Departments);
        Assert.NotEmpty(options.JobPositions);
        Assert.Contains(options.Periods, p => p.Code == "PER-2026-10");
        Assert.Contains(options.Departments, d => d.Code == "D01");
        Assert.Contains(options.JobPositions, j => j.Code == "TELLER");
    }

    private static Work CreateWork(Employee employee, string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Title = $"Công việc {code}",
        AssigneeEmployeeId = employee.Id,
        Status = WorkStatus.Submitted,
        UpdatedAt = Now.AddDays(-1),
    };

    private void AddWorkTx(Work work, string txCode, string status, string? reason, DateTimeOffset createdAt)
    {
        _db.WorkTransactions.Add(new WorkTransaction
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            TransactionCode = txCode,
            Status = status,
            Reason = reason,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
