using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Template;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TransactionImport;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// What the transaction and service rating imports share: reading the workbook, the importer, locked periods (BR-29), the
/// persisted row errors and the batch history, each history limited to its own <see cref="ImportFileType"/>.
/// </summary>
internal static class ImportBatchSupport
{
    public const string PeriodLockedCode = "PERIOD_LOCKED";
    public const string ConflictingDuplicateCode = "CONFLICTING_DUPLICATE";
    public const int LookupChunk = 1000;
    private const int MaxMessageLength = 500;
    private const int MaxFieldLength = 50;

    /// <summary>Reads the first sheet and maps its rows; any unreadable file, missing column or empty sheet is <c>IMPORT_INVALID_FILE</c>.</summary>
    public static TResult ReadRows<TRow, TResult>(
        IExcelSheetReader reader,
        Stream excel,
        int maxRows,
        ImportTemplate<TRow> template,
        Func<ExcelSheetResult, Dictionary<string, int>, TResult> map)
        where TRow : new()
    {
        try
        {
            var sheet = reader.Read(excel, maxRows);
            var headerIndex = template.BuildHeaderIndex(sheet.Headers, ErrorCodes.ImportInvalidFile);
            if (sheet.Rows.Count == 0)
            {
                throw new AppException("File không có dòng dữ liệu nào.", ErrorCodes.ImportInvalidFile, 400);
            }

            return map(sheet, headerIndex);
        }
        catch (AppException ex) when (ex.GetType() == typeof(AppException) && ex.ErrorCode == ErrorCodes.ImportInvalidFile)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AppException(
                ex is AppException appEx ? appEx.Message : "File không phải là workbook .xlsx hợp lệ.",
                ErrorCodes.ImportInvalidFile,
                400);
        }
    }

    public static async Task<Guid?> ResolveEmployeeIdAsync(IUnitOfWork uow, Guid userId, CancellationToken ct) =>
        await uow.Employees.Query().AsNoTracking()
            .Where(e => e.Id == userId || (e.Account != null && e.Account.Id == userId))
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

    // BR-29: evaluation periods with status LOCKED or CLOSED reject new data (docs/api/PERIOD_LOCK_CONTRACT.md).
    public static async Task<List<(DateOnly Start, DateOnly End)>> LoadLockedPeriodsAsync(
        IUnitOfWork uow, IReadOnlyCollection<DateOnly> dates, CancellationToken ct)
    {
        if (dates.Count == 0)
        {
            return [];
        }

        var min = dates.Min();
        var max = dates.Max();
        var periods = await uow.EvaluationPeriods.Query().AsNoTracking()
            .Where(p => (p.Status == PeriodStatus.Locked || p.Status == PeriodStatus.Closed) && p.StartDate <= max && p.EndDate >= min)
            .Select(p => new { p.StartDate, p.EndDate })
            .ToListAsync(ct);
        return periods.Select(p => (p.StartDate, p.EndDate)).ToList();
    }

    public static bool IsLocked(List<(DateOnly Start, DateOnly End)> lockedPeriods, DateOnly date) =>
        lockedPeriods.Any(p => p.Start <= date && date <= p.End);

    public static ImportRowErrorDto RowError(int row, string field, string code, string message) =>
        new(row, field.Length > MaxFieldLength ? field[..MaxFieldLength] : field, code, message);

    /// <summary>Stores the row errors on the batch and sets its counters.</summary>
    public static void Complete(ImportBatch batch, int importedRows, int duplicateRows, List<ImportRowErrorDto> errors)
    {
        foreach (var e in errors)
        {
            batch.Errors.Add(new ImportBatchError
            {
                Id = Guid.NewGuid(),
                ImportBatchId = batch.Id,
                RowNumber = e.Row,
                Field = e.Field,
                Code = e.Code,
                Message = e.Message.Length > MaxMessageLength ? e.Message[..MaxMessageLength] : e.Message,
            });
        }

        batch.ImportedRows = importedRows;
        batch.DuplicateRows = duplicateRows;
        batch.ErrorRows = errors.Count;
    }

    /// <summary>
    /// Detaches the whole failed graph: removing only the batch leaves its rows tracked as Added, and the next SaveChanges
    /// would insert them again (pointing to a batch that no longer exists).
    /// </summary>
    public static void Detach(IUnitOfWork uow, ImportBatch batch)
    {
        uow.ImportedTransactions.RemoveRange(batch.Transactions.ToList());
        uow.Repository<ServiceRating>().RemoveRange(batch.ServiceRatings.ToList());
        uow.Repository<ImportBatchError>().RemoveRange(batch.Errors.ToList());
        uow.ImportBatches.Remove(batch);
    }

    public static async Task<PagedResult<ImportBatchDto>> ListBatchesAsync(
        IUnitOfWork uow, ImportFileType fileType, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var result = await uow.ImportBatches.GetPagedAsync(page, pageSize, b => b.FileType == fileType, q => q.OrderByDescending(b => b.ImportedAt), ct);
        return new PagedResult<ImportBatchDto>(result.Items.Select(b => ToDto(b, null)).ToList(), result.TotalCount, result.PageIndex, result.PageSize);
    }

    public static async Task<ImportBatchDto> GetBatchAsync(IUnitOfWork uow, ImportFileType fileType, Guid id, CancellationToken ct)
    {
        var batch = await uow.ImportBatches.FirstOrDefaultAsync(b => b.Id == id && b.FileType == fileType, ct)
            ?? throw new NotFoundException("Import batch not found.");
        return ToDto(batch, null);
    }

    public static async Task EnsureBatchExistsAsync(IUnitOfWork uow, ImportFileType fileType, Guid id, CancellationToken ct)
    {
        if (!await uow.ImportBatches.AnyAsync(b => b.Id == id && b.FileType == fileType, ct))
        {
            throw new NotFoundException("Import batch not found.");
        }
    }

    public static async Task<PagedResult<ImportRowErrorDto>> ListErrorsAsync(
        IUnitOfWork uow, ImportFileType fileType, Guid batchId, int page, int pageSize, CancellationToken ct)
    {
        await EnsureBatchExistsAsync(uow, fileType, batchId, ct);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = uow.Repository<ImportBatchError>().Query().Where(e => e.ImportBatchId == batchId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(e => e.RowNumber).ThenBy(e => e.Code).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new ImportRowErrorDto(e.RowNumber, e.Field, e.Code, e.Message))
            .ToListAsync(ct);
        return new PagedResult<ImportRowErrorDto>(items, total, page, pageSize);
    }

    public static ImportBatchDto ToDto(ImportBatch b, List<ImportRowErrorDto>? errors) =>
        new(b.Id, b.FileName, "COMPLETED", b.TotalRows, b.ImportedRows, b.DuplicateRows, b.ErrorRows, errors, b.ImportedAt, b.ImportedBy);
}
