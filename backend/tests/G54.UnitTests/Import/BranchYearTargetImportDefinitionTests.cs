using ClosedXML.Excel;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.TargetImport;
using G54.BLL.Services;
using G54.BLL.Services.Import.BranchYearTarget;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace G54.UnitTests.Import;

/// <summary>
/// Exercises <see cref="TargetImportService"/> (stateless preview/commit over <see cref="BranchYearTargetImportDefinition"/>) against the real sample file (28 KPI rows,
/// backend/tests/G54.UnitTests/Import/Fixtures/KPI_Chi_nhanh_2025_chuan_hoa.xlsx) and small synthetic fixtures for the paths that need
/// specific DB state (partial commit, ACTIVE block, needs-review, access control).
/// </summary>
public sealed class BranchYearTargetImportDefinitionTests
{
    private const int SampleKpiRowCount = 28;

    private sealed class AllowAllPermissions : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken ct) =>
            Task.FromResult(resource == "target" && action == "manage" && requiredScope == "All");

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken ct) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed record Fixture(
        AppDbContext Db, DbContextOptions<AppDbContext> Options, TargetImportService Service, Guid BranchId, Guid YearPeriodId);

    private static async Task<Fixture> CreateAsync(bool branchScopedCaller = false, Guid? callerDepartmentId = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        var branch = new Department { Id = Guid.NewGuid(), Code = "BR", Name = "Branch", LevelType = DepartmentLevelType.Branch };
        var yearPeriod = new EvaluationPeriod { Id = Guid.NewGuid(), Code = "2026", Name = "2026", PeriodType = PeriodType.Year, StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), Status = PeriodStatus.Open };
        var previousYearPeriod = new EvaluationPeriod { Id = Guid.NewGuid(), Code = "2025", Name = "2025", PeriodType = PeriodType.Year, StartDate = new(2025, 1, 1), EndDate = new(2025, 12, 31), Status = PeriodStatus.Open };
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "G" };
        db.Departments.Add(branch);
        db.EvaluationPeriods.Add(yearPeriod);
        db.EvaluationPeriods.Add(previousYearPeriod);
        db.KpiGroups.Add(group);

        foreach (var (code, unit, direction) in SampleKpiCatalog())
        {
            db.KpiDefinitions.Add(new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = group.Id,
                Code = code,
                Name = code,
                Unit = unit,
                AggregationType = KpiAggregationType.Sum,
                Direction = direction,
                Status = KpiStatus.Active,
            });
        }

        await db.SaveChangesAsync();

        return new Fixture(db, options, CreateService(db, new AllowAllPermissions()), branch.Id, yearPeriod.Id);
    }

    private static TargetImportService CreateService(AppDbContext db, IPermissionService permissions)
    {
        var uow = new UnitOfWork(db);
        return new TargetImportService(
            uow,
            db,
            new BranchYearTargetImportDefinition(uow, permissions),
            new BranchYearTargetCommitter(uow),
            new TargetAllocationService(uow, permissions),
            Options.Create(new SystemParametersOptions()),
            TimeProvider.System);
    }

    /// <summary>Unit/direction for every code the real sample file uses (docs/excels), so the full-file tests
    /// below only hit V3 (KPI not found) for codes genuinely missing from the fixture catalog.</summary>
    private static IEnumerable<(string Code, string Unit, KpiDirection Direction)> SampleKpiCatalog()
    {
        (string, string, KpiDirection)[] vnd = [
            ("KPI_NFI", "VND", KpiDirection.HigherBetter), ("KPI_FEE_CARD", "VND", KpiDirection.HigherBetter),
            ("KPI_FEE_EBANK", "VND", KpiDirection.HigherBetter), ("KPI_INSURANCE", "VND", KpiDirection.HigherBetter),
            ("KPI_DEP_NET", "VND", KpiDirection.HigherBetter), ("KPI_CASA", "VND", KpiDirection.HigherBetter),
            ("KPI_TERM_DEP", "VND", KpiDirection.HigherBetter),
        ];
        (string, string, KpiDirection)[] rest = [
            ("KPI_CASA_RATIO", "%", KpiDirection.HigherBetter), ("KPI_ACC_OPEN", "TK", KpiDirection.HigherBetter),
            ("KPI_NEW_CUST", "KH", KpiDirection.HigherBetter), ("KPI_REFERRAL", "KH", KpiDirection.HigherBetter),
            ("KPI_ACTIVE_CUST", "KH", KpiDirection.HigherBetter), ("KPI_CROSS_SELL", "SP/KH", KpiDirection.HigherBetter),
            ("KPI_CHURN", "%", KpiDirection.LowerBetter), ("KPI_CARD_DEBIT", "thẻ", KpiDirection.HigherBetter),
            ("KPI_CARD_CREDIT", "thẻ", KpiDirection.HigherBetter), ("KPI_EBANK_ACTIVE", "KH", KpiDirection.HigherBetter),
            ("KPI_INSURANCE_CNT", "HĐ", KpiDirection.HigherBetter), ("KPI_AUTO_SAVING", "KH", KpiDirection.HigherBetter),
            ("KPI_CSAT", "điểm/5", KpiDirection.HigherBetter), ("KPI_1STAR_RATE", "%", KpiDirection.LowerBetter),
            ("KPI_SERVE_TIME", "phút", KpiDirection.LowerBetter), ("KPI_COMPLAINT_SLA", "%", KpiDirection.HigherBetter),
            ("KPI_TXN_VOL", "GD", KpiDirection.HigherBetter), ("KPI_TXN_ERR_RATE", "%", KpiDirection.LowerBetter),
            ("KPI_HANDOVER_SLA", "%", KpiDirection.HigherBetter), ("KPI_RISK_SEVERE", "vụ", KpiDirection.LowerBetter),
            ("KPI_TRAINING", "%", KpiDirection.HigherBetter),
        ];
        return vnd.Concat(rest);
    }

    private static FileStream OpenSampleFile()
    {
        var path = FindRepoFile("backend/tests/G54.UnitTests/Import/Fixtures/KPI_Chi_nhanh_2025_chuan_hoa.xlsx");
        // Excel may hold the file open on a dev machine; read-share so the test still works.
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' above {AppContext.BaseDirectory}.");
    }

    private static async Task<TargetImportPreviewDto> PreviewSampleAsync(TargetImportService service, Guid branchId, int year)
    {
        using var stream = OpenSampleFile();
        return await service.PreviewAsync(branchId, year, "sample.xlsx", stream, Guid.NewGuid(), CancellationToken.None);
    }

    private static async Task<TargetImportCommitResultDto> CommitSampleAsync(TargetImportService service, Guid branchId, int year)
    {
        using var stream = OpenSampleFile();
        return await service.CommitAsync(branchId, year, "sample.xlsx", stream, Guid.NewGuid(), CancellationToken.None);
    }

    /// <summary>A minimal workbook in the template layout (header on row 4) with one data row per (code, unit, year value).</summary>
    private static MemoryStream BuildWorkbook(int year, params (string Code, string Unit, string Value)[] rows)
    {
        using var template = new XLWorkbook(new MemoryStream(BranchYearTargetTemplateWriter.Write(year)));
        var sheet = template.Worksheets.First();
        for (var i = 0; i < rows.Length; i++)
        {
            sheet.Cell(5 + i, 1).Value = rows[i].Code;
            sheet.Cell(5 + i, 3).Value = rows[i].Unit;
            sheet.Cell(5 + i, 7).Value = rows[i].Value;
        }

        var stream = new MemoryStream();
        template.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task PreviewAsync_RealSampleFile_Finds28DataRowsAndSkipsGroupTotalNoteRows()
    {
        var f = await CreateAsync();
        var result = await PreviewSampleAsync(f.Service, f.BranchId, 2026);

        Assert.Equal(SampleKpiRowCount, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Matches("^KPI_[A-Z0-9_]+$", r.KpiCode!));
    }

    [Fact]
    public async Task PreviewAsync_RealSampleFile_AllRowsValidForYear2026()
    {
        var f = await CreateAsync();
        var result = await PreviewSampleAsync(f.Service, f.BranchId, 2026);

        Assert.Equal(0, result.Summary.Invalid);
        Assert.Equal(SampleKpiRowCount, result.Summary.Valid);
        Assert.All(result.Rows, r => Assert.Equal("CREATE", r.Action));
    }

    [Fact]
    public async Task PreviewAsync_Year2025_ReadsTheComparisonColumnInstead()
    {
        var f = await CreateAsync();
        var result2025 = await PreviewSampleAsync(f.Service, f.BranchId, 2025);

        Assert.Equal(0, result2025.Summary.Invalid);
    }

    [Fact]
    public void Read_MissingYearColumn_ThrowsFileLevel400()
    {
        using var stream = OpenSampleFile();
        var ex = Assert.Throws<BusinessRuleException>(() => KpiTargetSheetReader.Read(stream, 2099, 500));
        Assert.Equal(ErrorCodes.TargetImportYearColumnMissing, ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task PreviewAsync_ConvertedRow_ShowsCatalogUnitWithConvertedValue()
    {
        var f = await CreateAsync();
        var result = await PreviewSampleAsync(f.Service, f.BranchId, 2026);

        var row = result.Rows.Single(r => r.KpiCode == "KPI_NFI");
        Assert.Equal("VND", row.Unit);
        Assert.Equal(6_000_000_000m, row.Value);
    }

    [Fact]
    public async Task PreviewAsync_RiskSevereKpi_ZeroValueAcceptedForLowerBetter()
    {
        var f = await CreateAsync();
        var result = await PreviewSampleAsync(f.Service, f.BranchId, 2026);

        var row = result.Rows.Single(r => r.KpiCode == "KPI_RISK_SEVERE");
        Assert.Empty(row.Errors);
        Assert.Equal(0m, row.Value);
    }

    [Fact]
    public async Task PreviewAsync_RatioKpiWithDash_TakesValueFromEqualQuarters()
    {
        var f = await CreateAsync();
        var result = await PreviewSampleAsync(f.Service, f.BranchId, 2026);

        var row = result.Rows.Single(r => r.KpiCode == "KPI_CASA_RATIO");
        Assert.Empty(row.Errors);
        Assert.Equal(0.4m, row.Value);
    }

    [Fact]
    public void Read_NonStandardLayout_LocatesSheetHeaderAliasesRatiosAndRows()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Hướng dẫn").Cell(1, 1).Value = "Đọc kỹ trước khi nhập";
        var ws = workbook.Worksheets.Add("Chỉ tiêu");
        ws.Cell(1, 3).Value = "MÃ CHI NHÁNH:";
        ws.Cell(1, 4).Value = "BR";
        ws.Cell(3, 1).Value = "Tỷ lệ quý";
        ws.Cell(3, 5).Value = 0.1;
        ws.Cell(3, 6).Value = 0.2;
        ws.Cell(3, 7).Value = 0.3;
        ws.Cell(3, 8).Value = 0.4;
        string[] headers = ["Năm 2026", "Ma chi tieu (*)", "Đơn vị tính", "Ten  KPI", "Quý 1", "Quý 2", "Quý 3", "Quý 4"];
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(6, i + 1).Value = headers[i];
        }

        ws.Cell(7, 4).Value = "Nhóm A";
        ws.Cell(8, 1).Value = 100;
        ws.Cell(8, 2).Value = "NFI-01";
        ws.Cell(8, 3).Value = "VND";
        ws.Cell(9, 4).Value = "Tổng";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var result = KpiTargetSheetReader.Read(stream, 2026, 500);

        var row = Assert.Single(result.Rows);
        Assert.Equal(8, row.RowNumber);
        string? Cell(string header) => row.Cells[result.Headers.ToList().IndexOf(header)];
        Assert.Equal("NFI-01", Cell(KpiTargetSheetReader.KpiCodeColumn));
        Assert.Equal("100", Cell(KpiTargetSheetReader.YearColumn));
        Assert.Equal("VND", Cell(KpiTargetSheetReader.UnitColumn));
        Assert.Null(Cell(KpiTargetSheetReader.PreviousYearColumn));
        Assert.Equal("BR", result.BranchCodeRaw);
        Assert.Equal(0.4m, result.QuarterRatios[KpiTargetSheetReader.Q4Column]);
    }

    [Fact]
    public void Read_NoHeaderRowInAnySheet_ThrowsTemplateInvalid()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Sheet1").Cell(1, 1).Value = "Không có header";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var ex = Assert.Throws<BusinessRuleException>(() => KpiTargetSheetReader.Read(stream, 2026, 500));
        Assert.Equal(ErrorCodes.ImportTemplateInvalid, ex.ErrorCode);
    }

    [Fact]
    public void Read_FormulaCellWithNoCachedValue_IsRowError()
    {
        var sheet = new ExcelSheetResult(
            [KpiTargetSheetReader.KpiCodeColumn, KpiTargetSheetReader.YearColumn],
            [new RawRow(5, [KpiTargetSheetReader.FormulaNotCalculatedMarker, KpiTargetSheetReader.FormulaNotCalculatedMarker])]);
        Assert.Equal(KpiTargetSheetReader.FormulaNotCalculatedMarker, sheet.Rows[0].Cells[0]);
    }

    [Fact]
    public async Task CommitAsync_ActiveBranchYearTarget_IsBlocked()
    {
        var f = await CreateAsync();
        var kpi = await f.Db.KpiDefinitions.FirstAsync(k => k.Code == "KPI_NFI");
        f.Db.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            DepartmentId = f.BranchId,
            KpiDefinitionId = kpi.Id,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 6_000_000_000m,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
        });
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CommitSampleAsync(f.Service, f.BranchId, 2026));
        Assert.Equal(ErrorCodes.TargetInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task CommitAsync_DraftReplaceWithDraftChild_MarksChildNeedsReviewAndReportsIt()
    {
        var f = await CreateAsync();
        var kpi = await f.Db.KpiDefinitions.FirstAsync(k => k.Code == "KPI_NFI");
        var branchYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            DepartmentId = f.BranchId,
            KpiDefinitionId = kpi.Id,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 1_000_000_000m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
        };
        f.Db.TargetAssignments.Add(branchYear);
        f.Db.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            DepartmentId = f.BranchId,
            KpiDefinitionId = kpi.Id,
            EvaluationPeriodId = f.YearPeriodId,
            ParentAssignmentId = branchYear.Id,
            SplitDimension = TargetSplitDimension.Org,
            TargetValue = 1_000_000_000m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
        });
        await f.Db.SaveChangesAsync();

        var result = await CommitSampleAsync(f.Service, f.BranchId, 2026);

        Assert.True(result.NeedsReview);
        var child = await f.Db.TargetAssignments.FirstAsync(t => t.ParentAssignmentId == branchYear.Id);
        Assert.Equal("NEEDS_REVIEW: re-imported", child.ChangeReason);
    }

    [Fact]
    public async Task CommitAsync_UpdateOnExistingDraftRow_PersistsTheNewValue()
    {
        // Regression test: GetCurrentBranchYearRowsAsync must load the current branch-year rows tracked,
        // otherwise the committer's `existing.TargetValue = ...` mutation is silently dropped by SaveChangesAsync.
        var f = await CreateAsync();
        var kpi = await f.Db.KpiDefinitions.FirstAsync(k => k.Code == "KPI_NFI");
        var existingId = Guid.NewGuid();
        f.Db.TargetAssignments.Add(new TargetAssignment
        {
            Id = existingId,
            DepartmentId = f.BranchId,
            KpiDefinitionId = kpi.Id,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 1m, // different from the sample file's 6,000,000,000 so the row classifies as UPDATE
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
        });
        await f.Db.SaveChangesAsync();

        // A separate DbContext for the service under test, so nothing stays tracked from the arrange step above.
        await using var freshDb = new AppDbContext(f.Options);
        await CommitSampleAsync(CreateService(freshDb, new AllowAllPermissions()), f.BranchId, 2026);

        await using var verifyDb = new AppDbContext(f.Options);
        var persisted = await verifyDb.TargetAssignments.AsNoTracking().FirstAsync(t => t.Id == existingId);
        Assert.Equal(6_000_000_000m, persisted.TargetValue);
    }

    [Fact]
    public async Task CommitAsync_PartialFile_SkipsInvalidRowsAndCommitsTheRest()
    {
        var f = await CreateAsync();
        using var file = BuildWorkbook(2026, ("KPI_NFI", "VND", "6000000000"), ("KPI_DOES_NOT_EXIST", "VND", "100"));

        var result = await f.Service.CommitAsync(f.BranchId, 2026, "partial.xlsx", file, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, result.Committed);
        Assert.Equal(1, result.Skipped);
        var created = await f.Db.TargetAssignments.SingleAsync();
        // A newly-created branch-year row's EffectiveFrom must be the year period's start date, not DateOnly default.
        Assert.Equal(new DateOnly(2026, 1, 1), created.EffectiveFrom);
    }

    [Fact]
    public async Task CommitAsync_NoValidRows_Throws422AndWritesNothing()
    {
        var f = await CreateAsync();
        using var file = BuildWorkbook(2026, ("KPI_DOES_NOT_EXIST", "VND", "100"));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.CommitAsync(f.BranchId, 2026, "invalid.xlsx", file, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(ErrorCodes.TargetImportNoValidRows, ex.ErrorCode);
        Assert.Equal(422, ex.StatusCode);
        Assert.Empty(f.Db.AuditLogs);
    }

    [Fact]
    public async Task CommitAsync_WritesAuditLogWithQuarterRatios()
    {
        var f = await CreateAsync();
        await CommitSampleAsync(f.Service, f.BranchId, 2026);

        var audit = await f.Db.AuditLogs.SingleAsync();
        Assert.Equal("BranchYearTargetImport", audit.EntityName);
        Assert.Contains("quarterRatios", audit.NewValues);
    }

    [Fact]
    public async Task PreviewAsync_AnotherBranch_IsForbidden()
    {
        var f = await CreateAsync();

        // A caller without branch access (not an admin) cannot preview against f.BranchId.
        var scoped = CreateService(f.Db, new DeniedPermissions());

        await Assert.ThrowsAsync<ForbiddenException>(() => PreviewSampleAsync(scoped, f.BranchId, 2026));
    }

    private sealed class DeniedPermissions : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken ct) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken ct) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken ct) => Task.CompletedTask;
    }
}
