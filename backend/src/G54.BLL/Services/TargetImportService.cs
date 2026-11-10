using System.Globalization;
using System.Text.Json;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Dtos.TargetImport;
using G54.BLL.Services.Import.BranchYearTarget;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// Implements <see cref="ITargetImportService"/>: reads the "KPI Năm-Quý" sheet, runs
/// <see cref="BranchYearTargetImportDefinition"/> on it, and on commit writes the valid rows plus the AuditLog
/// entry (D10) in one transaction, then reuses <see cref="ITargetAllocationService"/> for the returned tree.
/// </summary>
public sealed class TargetImportService(
    IUnitOfWork uow,
    AppDbContext db,
    BranchYearTargetImportDefinition definition,
    BranchYearTargetCommitter committer,
    ITargetAllocationService targetAllocationService,
    IOptions<SystemParametersOptions> parameters,
    TimeProvider time) : ITargetImportService
{
    private const string AuditEntityName = "BranchYearTargetImport";

    public byte[] BuildTemplate(int year) => BranchYearTargetTemplateWriter.Write(year);

    public async Task<TargetImportPreviewDto> PreviewAsync(
        Guid branchId, int year, string fileName, Stream file, Guid userId, CancellationToken cancellationToken)
    {
        var (sheet, rows, context) = await AnalyzeAsync(branchId, year, file, userId, cancellationToken);

        var kpisNotInFile = BranchYearTargetImportDefinition.KpisNotInFile(rows, context)
            .Select(k => new TargetImportKpiNotInFileDto(k.Code, k.Name))
            .ToList();

        var ratios = sheet.QuarterRatios ?? new Dictionary<string, decimal>();
        decimal? Ratio(string quarter) => ratios.TryGetValue(quarter, out var r) ? r : null;
        var quarterRatios = new TargetImportQuarterRatiosDto(
            Ratio(KpiTargetSheetReader.Q1Column), Ratio(KpiTargetSheetReader.Q2Column),
            Ratio(KpiTargetSheetReader.Q3Column), Ratio(KpiTargetSheetReader.Q4Column));

        var invalid = rows.Count(r => r.Action == "INVALID");
        var summary = new TargetImportSummaryDto(
            rows.Count,
            rows.Count - invalid,
            invalid,
            rows.Count(r => r.Action == "CREATE"),
            rows.Count(r => r.Action == "UPDATE"),
            rows.Count(r => r.Action == "UNCHANGED"));

        return new TargetImportPreviewDto(
            fileName, branchId, year, summary, quarterRatios, kpisNotInFile, rows.Select(ToRowDto).ToList());
    }

    public async Task<TargetImportCommitResultDto> CommitAsync(
        Guid branchId, int year, string fileName, Stream file, Guid userId, CancellationToken cancellationToken)
    {
        var (sheet, rows, context) = await AnalyzeAsync(branchId, year, file, userId, cancellationToken);

        // D9: partial import - the valid rows are written, the invalid ones are reported as skipped.
        var valid = rows.Where(r => r.Errors.Count == 0).ToList();
        if (valid.Count == 0)
        {
            throw new BusinessRuleException(ErrorCodes.TargetImportNoValidRows, null, 422);
        }

        var result = await uow.ExecuteInTransactionAsync(async ct =>
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                EntityName = AuditEntityName,
                EntityId = AuditEntityId(branchId, year),
                Action = "Import",
                NewValues = JsonSerializer.Serialize(new
                {
                    fileName,
                    rowCount = rows.Count,
                    quarterRatios = sheet.QuarterRatios,
                }),
                ActorId = userId,
                OccurredAt = time.GetUtcNow(),
            });

            return await committer.WriteAsync(valid, context, ct);
        }, cancellationToken);

        var tree = await targetAllocationService.GetTreeAsync(branchId, year, null, userId, cancellationToken);
        var committed = result.Created + result.Updated + result.Unchanged;
        return new TargetImportCommitResultDto(branchId, year, tree, committed, rows.Count - valid.Count, result.NeedsReview);
    }

    private async Task<(KpiTargetSheetResult Sheet, List<BranchYearTargetRow> Rows, BranchYearTargetContext Context)> AnalyzeAsync(
        Guid branchId, int year, Stream file, Guid userId, CancellationToken ct)
    {
        var branch = await GetBranchAsync(branchId, ct);
        var sheet = KpiTargetSheetReader.Read(file, year, parameters.Value.ImportMaxRows);

        // G2: the branch-code cell is optional (not present in the current sample file); only reject when it
        // is there and disagrees with the branch the caller picked.
        if (sheet.BranchCodeRaw is not null && !string.Equals(sheet.BranchCodeRaw, branch.Code, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(ErrorCodes.TargetImportBranchMismatch, null, 400);
        }

        var (rows, context) = await definition.AnalyzeAsync(new ExcelSheetResult(sheet.Headers, sheet.Rows), branchId, year, userId, ct);
        return (sheet, rows, context);
    }

    private async Task<Department> GetBranchAsync(Guid branchId, CancellationToken ct)
    {
        var branch = await uow.Departments.GetByIdAsync(branchId, ct);
        if (branch?.LevelType != DepartmentLevelType.Branch)
        {
            throw new NotFoundException("Branch not found or invalid type.");
        }

        return branch;
    }

    private static string AuditEntityId(Guid branchId, int year) => $"{branchId}:{year}";

    private static TargetImportRowDto ToRowDto(BranchYearTargetRow row)
    {
        // The values are already in the catalog unit, so show that unit rather than the file's (e.g. VND, not "tỷ VND").
        var value = row.YearValue ?? ParseDecimal(row.YearRaw);
        var previousYearValue = row.PreviousYearValue ?? ParseDecimal(row.PreviousYearRaw);
        decimal? delta = value is decimal v && previousYearValue is decimal p ? v - p : null;
        decimal? deltaPercent = delta is decimal d && previousYearValue is decimal pv && pv != 0 ? d / pv : null;

        return new TargetImportRowDto(
            row.RowNumber,
            row.KpiCodeRaw,
            row.KpiNameRaw,
            row.StoredUnit ?? row.UnitRaw,
            value,
            previousYearValue,
            delta,
            deltaPercent,
            row.Action,
            row.Errors.Select(e => new TargetImportErrorDto(e.Column, e.Code, e.Message)).ToList(),
            row.Warnings.Select(e => new TargetImportErrorDto(e.Column, e.Code, e.Message)).ToList());
    }

    private static decimal? ParseDecimal(string? text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
