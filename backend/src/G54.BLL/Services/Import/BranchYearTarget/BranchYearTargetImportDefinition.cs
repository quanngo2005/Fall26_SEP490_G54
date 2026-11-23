using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Template;
using G54.BLL.Common.Validation;
using G54.BLL.Services.Import.BranchYearTarget.Rules;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services.Import.BranchYearTarget;

/// <summary>
/// Branch-Year Target import: template "KPI Năm-Quý" (docs/design/BRANCH_YEAR_KPI_IMPORT_DESIGN.md). Maps the
/// sheet's rows, validates them (V1-V14) and classifies each valid KPI row as CREATE/UPDATE/UNCHANGED. Stateless,
/// like <see cref="EmployeeImportService"/>: preview and commit each re-read the uploaded file and call
/// <see cref="AnalyzeAsync"/>, so commit always validates exactly what it writes.
/// </summary>
public sealed class BranchYearTargetImportDefinition(IUnitOfWork uow, IPermissionService permissions)
{
    private static readonly ImportTemplate<BranchYearTargetRow> Template = new TemplateBuilder<BranchYearTargetRow>()
        .Column(KpiTargetSheetReader.KpiCodeColumn, (r, v) => r.KpiCodeRaw = v)
        .Column(KpiTargetSheetReader.KpiNameColumn, (r, v) => r.KpiNameRaw = v, required: false)
        .Column(KpiTargetSheetReader.UnitColumn, (r, v) => r.UnitRaw = v, required: false)
        .Column(KpiTargetSheetReader.DirectionColumn, (r, v) => r.DirectionRaw = v, required: false)
        .Column(KpiTargetSheetReader.WeightColumn, (r, v) => r.WeightRaw = v, required: false)
        .Column(KpiTargetSheetReader.PreviousYearColumn, (r, v) => r.PreviousYearRaw = v, required: false)
        .Column(KpiTargetSheetReader.YearColumn, (r, v) => r.YearRaw = v)
        .Column(KpiTargetSheetReader.Q1Column, (r, v) => r.Q1Raw = v, required: false)
        .Column(KpiTargetSheetReader.Q2Column, (r, v) => r.Q2Raw = v, required: false)
        .Column(KpiTargetSheetReader.Q3Column, (r, v) => r.Q3Raw = v, required: false)
        .Column(KpiTargetSheetReader.Q4Column, (r, v) => r.Q4Raw = v, required: false)
        .Column(KpiTargetSheetReader.AggregationColumn, (r, v) => r.AggregationRaw = v, required: false)
        .Build();

    public async Task<(List<BranchYearTargetRow> Rows, BranchYearTargetContext Context)> AnalyzeAsync(
        ExcelSheetResult sheet, Guid branchId, int year, Guid userId, CancellationToken cancellationToken)
    {
        var headerIndex = Template.BuildHeaderIndex(sheet.Headers);
        var rows = sheet.Rows
            .Select(raw =>
            {
                var row = Template.MapRow(raw, headerIndex);
                row.RowNumber = raw.RowNumber;
                return row;
            })
            .ToList();

        var context = await LoadContextAsync(rows, branchId, year, userId, cancellationToken);
        Validate(rows, context);
        foreach (var row in rows)
        {
            row.Action = row.Errors.Count > 0 ? "INVALID" : Classify(row, context);
        }

        return (rows, context);
    }

    /// <summary>KPIs that already have a Branch-Year target for this branch+year but are absent from the file
    /// (section 3.5: they are left untouched, only listed so the preview can show them).</summary>
    public static IReadOnlyList<KpiDefinition> KpisNotInFile(IReadOnlyList<BranchYearTargetRow> rows, BranchYearTargetContext context)
    {
        var codesInFile = rows.Select(r => r.KpiCode).Where(c => c is not null).Select(c => c!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return context.CurrentByKpiId.Values
            .Select(t => context.KpiById[t.KpiDefinitionId])
            .Where(kpi => !codesInFile.Contains(kpi.Code))
            .ToList();
    }

    private static void Validate(List<BranchYearTargetRow> rows, BranchYearTargetContext context)
    {
        foreach (var row in rows)
        {
            Require(row, KpiTargetSheetReader.KpiCodeColumn, row.KpiCodeRaw);
            Require(row, KpiTargetSheetReader.YearColumn, row.YearRaw);
            BranchYearTargetFieldFormatRule.Validate(row);
            BranchYearTargetQuarterRule.Validate(row);
            BranchYearTargetCatalogRule.Validate(row, context);
        }

        // Two rows with one KPI code would write the same target twice: flag the whole group.
        foreach (var group in rows.Where(r => r.KpiCode is not null).GroupBy(r => r.KpiCode!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var row in group)
            {
                row.AddError(KpiTargetSheetReader.KpiCodeColumn, ErrorCodes.DataImportDuplicate, $"Mã KPI '{group.Key}' trùng lặp trong file.");
            }
        }
    }

    private static void Require(BranchYearTargetRow row, string column, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            row.AddError(column, ErrorCodes.RequiredField, $"'{column}' là bắt buộc nhập.");
        }
    }

    private static string Classify(BranchYearTargetRow row, BranchYearTargetContext context)
    {
        var kpi = context.KpiByCode[row.KpiCode!];
        if (!context.CurrentByKpiId.TryGetValue(kpi.Id, out var existing))
        {
            return "CREATE";
        }

        return existing.TargetValue == row.YearValue ? "UNCHANGED" : "UPDATE";
    }

    private async Task<BranchYearTargetContext> LoadContextAsync(
        IReadOnlyList<BranchYearTargetRow> rows, Guid branchId, int year, Guid userId, CancellationToken cancellationToken)
    {
        var branch = await uow.Departments.GetByIdAsync(branchId, cancellationToken);
        if (branch?.LevelType != DepartmentLevelType.Branch)
        {
            throw new NotFoundException("Branch not found or invalid type.");
        }

        await DepartmentAccessGuard.EnsureDepartmentAccessAsync(uow, permissions, userId, branchId, cancellationToken);

        var yearPeriod = await uow.EvaluationPeriods.GetYearAsync(year, cancellationToken)
            ?? throw new NotFoundException($"Year period {year} not found.");
        if (yearPeriod.Status != PeriodStatus.Open)
        {
            throw new ConflictException("Evaluation period is not open.", ErrorCodes.TargetPeriodNotOpen);
        }

        // Tracked: the committer mutates these entities directly (existing.TargetValue = ...) and relies on
        // SaveChangesAsync to pick up the change, so an AsNoTracking() read here would silently drop updates.
        var currentByKpiId = await GetCurrentBranchYearRowsAsync(branchId, yearPeriod.Id, tracked: true, cancellationToken);

        // G6/D5: re-importing over a branch year that already went ACTIVE (published) is blocked;
        // revising a published plan goes through the allocation screens.
        if (currentByKpiId.Values.Any(t => t.Status == TargetAssignmentStatus.Active))
        {
            throw new ConflictException(
                "Năm nay đã có chỉ tiêu được phát hành; hãy chỉnh sửa qua màn phân bổ.",
                ErrorCodes.TargetInvalidState);
        }

        var previousYearPeriod = await uow.EvaluationPeriods.GetYearAsync(year - 1, cancellationToken);
        var previousYearByKpiId = previousYearPeriod is null
            ? []
            : await GetCurrentBranchYearRowsAsync(branchId, previousYearPeriod.Id, tracked: false, cancellationToken);

        var codes = rows
            .Select(r => r.KpiCodeRaw?.Trim().ToUpperInvariant())
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var referencedIds = currentByKpiId.Keys.Concat(previousYearByKpiId.Keys).Distinct().ToList();
        var kpis = await uow.KpiDefinitions.ListAsync(k => codes.Contains(k.Code) || referencedIds.Contains(k.Id), cancellationToken);

        return new BranchYearTargetContext
        {
            Branch = branch,
            YearPeriod = yearPeriod,
            KpiByCode = kpis.ToDictionary(k => k.Code, StringComparer.OrdinalIgnoreCase),
            KpiById = kpis.ToDictionary(k => k.Id),
            CurrentByKpiId = currentByKpiId,
            PreviousYearByKpiId = previousYearByKpiId,
        };
    }

    /// <summary>The current (not Superseded/Closed) Branch-Year row per KPI for one branch+period, one batch
    /// query covering every KPI in the file. <paramref name="tracked"/> must be true for the rows the committer
    /// will mutate in place - the repository's own <c>ListAsync</c> always reads with <c>AsNoTracking()</c>.</summary>
    private async Task<Dictionary<Guid, TargetAssignment>> GetCurrentBranchYearRowsAsync(
        Guid branchId, Guid periodId, bool tracked, CancellationToken ct)
    {
        var rows = await uow.TargetAssignments.Query(asNoTracking: !tracked)
            .Where(t => t.DepartmentId == branchId && t.EvaluationPeriodId == periodId && t.ParentAssignmentId == null)
            .ToListAsync(ct);

        return rows
            .Where(t => t.Status is TargetAssignmentStatus.Draft or TargetAssignmentStatus.Active)
            .GroupBy(t => t.KpiDefinitionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.Version).First());
    }
}
