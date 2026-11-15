using ClosedXML.Excel;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TransactionImport;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class TransactionImportService(IUnitOfWork uow, TimeProvider clock) : ITransactionImportService
{
    private static readonly string[] Headers =
        ["transaction_code", "employee_code", "product_code", "transaction_date", "amount", "denominator", "refer_employee_code"];

    // MSG27 (SRS BR-29): the evaluation period of the transaction date is locked.
    private const string PeriodLockedMessage =
        "Kỳ đánh giá của ngày giao dịch đã bị khóa; mọi thay đổi phải thực hiện qua bút toán đảo (Reversal Entry) ở kỳ đang mở. " +
        "/ The evaluation period of this transaction date is locked; changes require a Reversal Entry in the active period.";

    private const string PeriodLockedCode = "PERIOD_LOCKED";
    private const string ConflictingDuplicateCode = "CONFLICTING_DUPLICATE";
    private const int RequiredColumns = 5;
    private const int LookupChunk = 1000;
    private const int MaxMessageLength = 500;

    public async Task<ImportBatchDto> ImportAsync(string fileName, Stream excel, Guid userId, CancellationToken ct = default)
    {
        var rows = ReadRows(excel);
        var employees = await LoadEmployeesAsync(rows, ct);
        var lockedPeriods = await LoadLockedPeriodsAsync(rows, ct);

        // A concurrent import of the same codes can win the unique index between our read and our insert:
        // reload what exists and classify the rows again, once, instead of failing the whole request.
        for (var attempt = 0; ; attempt++)
        {
            var existing = await LoadExistingAsync(rows, ct);
            var (batch, errors) = Build(fileName, userId, rows, employees, lockedPeriods, existing);
            await uow.ImportBatches.AddAsync(batch, ct);
            try
            {
                await uow.SaveChangesAsync(ct);
                return ToDto(batch, errors);
            }
            catch (DbUpdateException ex) when (attempt < 2 && PostgresConflict.IsUniqueViolation(ex))
            {
                uow.ImportBatches.Remove(batch);
            }
        }
    }

    public async Task<PagedResult<ImportBatchDto>> ListBatchesAsync(int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var result = await uow.ImportBatches.GetPagedAsync(page, pageSize, null, q => q.OrderByDescending(b => b.ImportedAt), ct);
        return new PagedResult<ImportBatchDto>(result.Items.Select(b => ToDto(b, null)).ToList(), result.TotalCount, result.PageIndex, result.PageSize);
    }

    public async Task<ImportBatchDto> GetBatchAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await uow.ImportBatches.GetByIdAsync(id, ct) ?? throw new NotFoundException("Import batch not found.");
        return ToDto(batch, null);
    }

    public async Task<PagedResult<ImportRowErrorDto>> ListErrorsAsync(
        Guid batchId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!await uow.ImportBatches.AnyAsync(b => b.Id == batchId, ct))
            throw new NotFoundException("Import batch not found.");

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = uow.Repository<ImportBatchError>().Query().Where(e => e.ImportBatchId == batchId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(e => e.RowNumber).ThenBy(e => e.Code).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new ImportRowErrorDto(e.RowNumber, e.Field, e.Code, e.Message))
            .ToListAsync(ct);
        return new PagedResult<ImportRowErrorDto>(items, total, page, pageSize);
    }

    public async Task<PagedResult<ImportedTransactionDto>> ListTransactionsAsync(
        Guid batchId, string? employeeCode, string? productCode, int page, int pageSize, CancellationToken ct = default)
    {
        if (!await uow.ImportBatches.AnyAsync(b => b.Id == batchId, ct))
            throw new NotFoundException("Import batch not found.");

        var query = uow.ImportedTransactions.Query().Where(t => t.ImportBatchId == batchId);
        if (!string.IsNullOrWhiteSpace(employeeCode)) query = query.Where(t => t.Employee.EmployeeCode == employeeCode.Trim());
        if (!string.IsNullOrWhiteSpace(productCode)) query = query.Where(t => t.ProductCode == productCode.Trim());

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(t => t.TransactionCode).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new ImportedTransactionDto(t.Id, t.TransactionCode, t.Employee.EmployeeCode, t.ProductCode,
                t.TransactionDate, t.Amount, t.Denominator, t.ReferEmployeeCode))
            .ToListAsync(ct);
        return new PagedResult<ImportedTransactionDto>(items, total, page, pageSize);
    }

    // Row order: required/format and unknown employee, then locked period (BR-29), then duplicates (BR-08).
    private (ImportBatch Batch, List<ImportRowErrorDto> Errors) Build(
        string fileName, Guid userId, List<Row> rows, Dictionary<string, Guid> employees,
        List<(DateOnly Start, DateOnly End)> lockedPeriods, Dictionary<string, ImportedTransaction> existing)
    {
        var errors = new List<ImportRowErrorDto>();
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            FileName = fileName.Length > 255 ? fileName[..255] : fileName,
            TotalRows = rows.Count,
            ImportedBy = userId,
            ImportedAt = clock.GetUtcNow(),
        };
        var seen = new HashSet<string>();
        var duplicates = 0;
        foreach (var row in rows)
        {
            var error = Validate(row, employees);
            if (error is null && lockedPeriods.Any(p => p.Start <= row.Date!.Value && row.Date.Value <= p.End))
                error = ("transaction_date", PeriodLockedCode, PeriodLockedMessage);
            if (error is not null)
            {
                errors.Add(new ImportRowErrorDto(row.Number, error.Value.Field, error.Value.Code, error.Value.Message));
                continue;
            }
            if (existing.TryGetValue(row.TransactionCode, out var current))
            {
                if (SameValues(current, row, employees[row.EmployeeCode]))
                {
                    duplicates++;
                }
                else
                {
                    errors.Add(new ImportRowErrorDto(row.Number, "transaction_code", ConflictingDuplicateCode,
                        $"transaction_code '{row.TransactionCode}' was already imported with different values."));
                }
                continue;
            }
            if (!seen.Add(row.TransactionCode))
            {
                errors.Add(new ImportRowErrorDto(row.Number, "transaction_code", "DUPLICATE_IN_FILE",
                    "transaction_code appears more than once in the file."));
                continue;
            }
            batch.Transactions.Add(new ImportedTransaction
            {
                Id = Guid.NewGuid(),
                ImportBatchId = batch.Id,
                TransactionCode = row.TransactionCode,
                EmployeeId = employees[row.EmployeeCode],
                ProductCode = row.ProductCode,
                TransactionDate = row.Date!.Value,
                Amount = row.Amount!.Value,
                Denominator = row.Denominator,
                ReferEmployeeCode = row.ReferEmployeeCode,
            });
        }

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
        batch.ImportedRows = batch.Transactions.Count;
        batch.DuplicateRows = duplicates;
        batch.ErrorRows = errors.Count;
        return (batch, errors);
    }

    private static bool SameValues(ImportedTransaction current, Row row, Guid employeeId) =>
        current.EmployeeId == employeeId
        && current.ProductCode == row.ProductCode
        && current.TransactionDate == row.Date
        && current.Amount == row.Amount
        && current.Denominator == row.Denominator
        && current.ReferEmployeeCode == row.ReferEmployeeCode;

    private async Task<Dictionary<string, Guid>> LoadEmployeesAsync(List<Row> rows, CancellationToken ct)
    {
        var employees = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in rows.Select(r => r.EmployeeCode).Where(c => c.Length > 0).Distinct().Chunk(LookupChunk))
        {
            var found = await uow.Employees.Query().Where(e => chunk.Contains(e.EmployeeCode))
                .Select(e => new { e.EmployeeCode, e.Id }).ToListAsync(ct);
            foreach (var e in found) employees[e.EmployeeCode] = e.Id;
        }
        return employees;
    }

    private async Task<Dictionary<string, ImportedTransaction>> LoadExistingAsync(List<Row> rows, CancellationToken ct)
    {
        var existing = new Dictionary<string, ImportedTransaction>();
        foreach (var chunk in rows.Select(r => r.TransactionCode).Where(c => c.Length > 0).Distinct().Chunk(LookupChunk))
        {
            var found = await uow.ImportedTransactions.Query().AsNoTracking()
                .Where(t => chunk.Contains(t.TransactionCode)).ToListAsync(ct);
            foreach (var t in found) existing[t.TransactionCode] = t;
        }
        return existing;
    }

    // BR-29: evaluation periods with status LOCKED or CLOSED reject new data (docs/api/PERIOD_LOCK_CONTRACT.md).
    private async Task<List<(DateOnly Start, DateOnly End)>> LoadLockedPeriodsAsync(List<Row> rows, CancellationToken ct)
    {
        var dates = rows.Where(r => r.Date is not null).Select(r => r.Date!.Value).ToList();
        if (dates.Count == 0) return [];
        var min = dates.Min();
        var max = dates.Max();
        var periods = await uow.EvaluationPeriods.Query().AsNoTracking()
            .Where(p => (p.Status == PeriodStatus.Locked || p.Status == PeriodStatus.Closed) && p.StartDate <= max && p.EndDate >= min)
            .Select(p => new { p.StartDate, p.EndDate }).ToListAsync(ct);
        return periods.Select(p => (p.StartDate, p.EndDate)).ToList();
    }

    private static ImportBatchDto ToDto(ImportBatch b, List<ImportRowErrorDto>? errors) =>
        new(b.Id, b.FileName, "COMPLETED", b.TotalRows, b.ImportedRows, b.DuplicateRows, b.ErrorRows, errors, b.ImportedAt, b.ImportedBy);

    private static (string Field, string Code, string Message)? Validate(Row row, Dictionary<string, Guid> employees)
    {
        if (row.TransactionCode.Length == 0) return Missing("transaction_code");
        if (row.EmployeeCode.Length == 0) return Missing("employee_code");
        if (row.ProductCode.Length == 0) return Missing("product_code");
        if (row.RawDate.Length == 0) return Missing("transaction_date");
        if (row.RawAmount.Length == 0) return Missing("amount");
        if (row.TransactionCode.Length > 100)
            return ("transaction_code", "INVALID_VALUE", "transaction_code is longer than 100 characters.");
        if (row.ProductCode.Length > 100)
            return ("product_code", "INVALID_VALUE", "product_code is longer than 100 characters.");
        if (row.Date is null) return ("transaction_date", "INVALID_VALUE", "transaction_date is not a valid date.");
        if (row.Amount is null or < 0) return ("amount", "INVALID_VALUE", "amount must be a number >= 0.");
        if (row.RawDenominator.Length > 0 && row.Denominator is null or <= 0)
            return ("denominator", "INVALID_VALUE", "denominator must be a number > 0.");
        if (row.ReferEmployeeCode is { Length: > 50 })
            return ("refer_employee_code", "INVALID_VALUE", "refer_employee_code is longer than 50 characters.");
        if (!employees.ContainsKey(row.EmployeeCode))
            return ("employee_code", "EMPLOYEE_NOT_FOUND", $"Employee '{row.EmployeeCode}' does not exist.");
        return null;

        static (string, string, string) Missing(string field) => (field, "REQUIRED_FIELD_MISSING", $"{field} is required.");
    }

    private static List<Row> ReadRows(Stream excel)
    {
        try
        {
            using var workbook = new XLWorkbook(excel);
            var sheet = workbook.Worksheets.First();
            var header = sheet.Row(1);
            for (var c = 0; c < Headers.Length; c++)
            {
                var name = header.Cell(c + 1).GetString().Trim();
                if (!string.Equals(name, Headers[c], StringComparison.OrdinalIgnoreCase) && (c < RequiredColumns || name.Length > 0))
                    throw new AppException($"Header column {c + 1} must be '{Headers[c]}'.", ErrorCodes.ImportInvalidFile, 400);
            }

            var rows = new List<Row>();
            var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
            for (var n = 2; n <= last; n++)
            {
                var r = sheet.Row(n);
                if (r.IsEmpty()) continue;
                var rawDate = Text(r, 4);
                var rawAmount = Text(r, 5);
                var rawDenominator = Text(r, 6);
                rows.Add(new Row(n, Text(r, 1), Text(r, 2), Text(r, 3), rawDate, rawAmount, rawDenominator,
                    ParseDate(r.Cell(4)), ParseDecimal(rawAmount), ParseDecimal(rawDenominator),
                    Text(r, 7) is { Length: > 0 } refer ? refer : null));
            }
            if (rows.Count == 0)
                throw new AppException("The file has no data rows.", ErrorCodes.ImportInvalidFile, 400);
            return rows;
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AppException("The file is not a valid .xlsx workbook.", ErrorCodes.ImportInvalidFile, 400);
        }
    }

    private static string Text(IXLRow row, int column) => row.Cell(column).GetString().Trim();

    private static decimal? ParseDecimal(string raw) =>
        decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateOnly? ParseDate(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());
        return DateOnly.TryParse(cell.GetString().Trim(), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private sealed record Row(
        int Number, string TransactionCode, string EmployeeCode, string ProductCode,
        string RawDate, string RawAmount, string RawDenominator,
        DateOnly? Date, decimal? Amount, decimal? Denominator, string? ReferEmployeeCode);
}
