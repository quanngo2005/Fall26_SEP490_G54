using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Parsing;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Template;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TransactionImport;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

public sealed class TransactionImportService(
    IUnitOfWork uow,
    TimeProvider clock,
    IExcelSheetReader reader,
    IOptions<SystemParametersOptions> parameters) : ITransactionImportService
{
    // MSG27 (SRS BR-29): the evaluation period of the transaction date is locked.
    private const string PeriodLockedMessage =
        "Kỳ đánh giá của ngày giao dịch đã bị khóa; mọi thay đổi phải thực hiện qua bút toán đảo (Reversal Entry) ở kỳ đang mở.";

    private const string PeriodLockedCode = "PERIOD_LOCKED";
    private const string ConflictingDuplicateCode = "CONFLICTING_DUPLICATE";
    private const int LookupChunk = 1000;
    private const int MaxMessageLength = 500;
    private const int MaxFieldLength = 50;


    public async Task<ImportBatchDto> ImportAsync(string fileName, Stream excel, Guid userId, CancellationToken ct = default)
    {
        var (rows, fields) = ReadRows(excel);
        var employees = await LoadEmployeesAsync(rows, ct);
        var products = await LoadProductCodesAsync(ct);
        var lockedPeriods = await LoadLockedPeriodsAsync(rows, ct);

        // A concurrent import of the same codes can win the unique index between our read and our insert:
        // reload what exists and classify the rows again, once, instead of failing the whole request.
        for (var attempt = 0; ; attempt++)
        {
            var existing = await LoadExistingAsync(rows, ct);
            var (batch, errors) = Build(fileName, userId, rows, fields, employees, products, lockedPeriods, existing);
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
        {
            throw new NotFoundException("Import batch not found.");
        }

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
        {
            throw new NotFoundException("Import batch not found.");
        }

        var query = uow.ImportedTransactions.Query().Where(t => t.ImportBatchId == batchId);
        if (!string.IsNullOrWhiteSpace(employeeCode))
        {
            query = query.Where(t => t.Employee.EmployeeCode == employeeCode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(productCode))
        {
            query = query.Where(t => t.ProductCode == productCode.Trim());
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(t => t.TransactionCode).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new ImportedTransactionDto(
                t.Id,
                t.TransactionCode,
                t.Employee.EmployeeCode,
                t.ProductCode,
                t.TransactionDate,
                t.Amount,
                t.Denominator,
                t.ReferEmployeeCode,
                t.CustomerCode))
            .ToListAsync(ct);
        return new PagedResult<ImportedTransactionDto>(items, total, page, pageSize);
    }

    // Row order: required/format/employee/product/refer, then locked period (BR-29), then duplicates (BR-08).
    private (ImportBatch Batch, List<ImportRowErrorDto> Errors) Build(
        string fileName,
        Guid userId,
        List<Row> rows,
        FieldNames fields,
        Dictionary<string, EmployeeLookup> employees,
        HashSet<string> products,
        List<(DateOnly Start, DateOnly End)> lockedPeriods,
        Dictionary<string, ImportedTransaction> existing)
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;
        foreach (var row in rows)
        {
            var error = Validate(row, fields, employees, products);
            if (error is null && lockedPeriods.Any(p => p.Start <= row.Date!.Value && row.Date.Value <= p.End))
            {
                error = (fields.TransactionDate, PeriodLockedCode, PeriodLockedMessage);
            }

            if (error is not null)
            {
                errors.Add(new ImportRowErrorDto(row.Number, ClampField(error.Value.Field), error.Value.Code, error.Value.Message));
                continue;
            }

            var employee = employees[row.EmployeeCode];
            if (existing.TryGetValue(row.TransactionCode, out var current))
            {
                if (SameValues(current, row, employee.Id))
                {
                    duplicates++;
                }
                else
                {
                    errors.Add(new ImportRowErrorDto(
                        row.Number,
                        ClampField(fields.TransactionCode),
                        ConflictingDuplicateCode,
                        $"Mã giao dịch '{row.TransactionCode}' đã tồn tại trên hệ thống với thông tin khác."));
                }

                continue;
            }

            if (!seen.Add(row.TransactionCode))
            {
                errors.Add(new ImportRowErrorDto(
                    row.Number,
                    ClampField(fields.TransactionCode),
                    "DUPLICATE_IN_FILE",
                    $"Mã giao dịch '{row.TransactionCode}' bị trùng lặp trong file."));
                continue;
            }

            batch.Transactions.Add(new ImportedTransaction
            {
                Id = Guid.NewGuid(),
                ImportBatchId = batch.Id,
                TransactionCode = row.TransactionCode,
                CustomerCode = row.CustomerCode,
                EmployeeId = employee.Id,
                ProductCode = products.TryGetValue(row.ProductCode, out var canonicalProduct) ? canonicalProduct : row.ProductCode,
                TransactionDate = row.Date!.Value,
                Amount = row.Amount!.Value,
                Denominator = row.Denominator,
                ReferEmployeeCode = row.ReferEmployeeCode is not null && employees.TryGetValue(row.ReferEmployeeCode, out var refer)
                    ? refer.Code
                    : row.ReferEmployeeCode,
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
        && string.Equals(current.ProductCode, row.ProductCode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(current.CustomerCode, row.CustomerCode, StringComparison.OrdinalIgnoreCase)
        && current.TransactionDate == row.Date
        && current.Amount == row.Amount
        && current.Denominator == row.Denominator
        && string.Equals(current.ReferEmployeeCode, row.ReferEmployeeCode, StringComparison.OrdinalIgnoreCase);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "Translated to PostgreSQL upper(), using database collation rather than CLR culture.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Npgsql translates only the parameterless ToUpper() to SQL upper().")]
    private async Task<Dictionary<string, EmployeeLookup>> LoadEmployeesAsync(List<Row> rows, CancellationToken ct)
    {
        var codes = rows
            .SelectMany(r => new[] { r.EmployeeCode, r.ReferEmployeeCode })
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var employees = new Dictionary<string, EmployeeLookup>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in codes.Chunk(LookupChunk))
        {
            var upperChunk = chunk.ToList();
            var found = await uow.Employees.Query().AsNoTracking()
                .Where(e => upperChunk.Contains(e.EmployeeCode.ToUpper()))
                .Select(e => new EmployeeLookup(e.Id, e.EmployeeCode, e.Status))
                .ToListAsync(ct);
            foreach (var e in found)
            {
                employees[e.Code] = e;
            }
        }

        return employees;
    }

    private async Task<HashSet<string>> LoadProductCodesAsync(CancellationToken ct)
    {
        var productCodes = await uow.Repository<WorkType>().Query().AsNoTracking()
            .Where(w => w.ProductCode != null && w.ProductCode != string.Empty)
            .Select(w => w.ProductCode!)
            .Distinct()
            .ToListAsync(ct);
        return new HashSet<string>(productCodes, StringComparer.OrdinalIgnoreCase);
    }

    // Codes in the file are already upper-cased; rows imported before that may be stored in another case.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "Translated to PostgreSQL upper().")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Npgsql translates only the parameterless ToUpper() to SQL upper().")]
    private async Task<Dictionary<string, ImportedTransaction>> LoadExistingAsync(List<Row> rows, CancellationToken ct)
    {
        var existing = new Dictionary<string, ImportedTransaction>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in rows.Select(r => r.TransactionCode).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(LookupChunk))
        {
            var found = await uow.ImportedTransactions.Query().AsNoTracking()
                .Where(t => chunk.Contains(t.TransactionCode.ToUpper()))
                .ToListAsync(ct);
            foreach (var t in found)
            {
                existing[t.TransactionCode] = t;
            }
        }

        return existing;
    }

    // BR-29: evaluation periods with status LOCKED or CLOSED reject new data (docs/api/PERIOD_LOCK_CONTRACT.md).
    private async Task<List<(DateOnly Start, DateOnly End)>> LoadLockedPeriodsAsync(List<Row> rows, CancellationToken ct)
    {
        var dates = rows.Where(r => r.Date is not null).Select(r => r.Date!.Value).ToList();
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

    private static ImportBatchDto ToDto(ImportBatch b, List<ImportRowErrorDto>? errors) =>
        new(b.Id, b.FileName, "COMPLETED", b.TotalRows, b.ImportedRows, b.DuplicateRows, b.ErrorRows, errors, b.ImportedAt, b.ImportedBy);

    private static (string Field, string Code, string Message)? Validate(
        Row row,
        FieldNames fields,
        Dictionary<string, EmployeeLookup> employees,
        HashSet<string> products)
    {
        if (row.TransactionCode.Length == 0)
        {
            return Missing(fields.TransactionCode);
        }

        if (fields.HasCustomerCode && string.IsNullOrEmpty(row.CustomerCode))
        {
            return Missing(fields.CustomerCode);
        }

        if (row.EmployeeCode.Length == 0)
        {
            return Missing(fields.EmployeeCode);
        }

        if (row.ProductCode.Length == 0)
        {
            return Missing(fields.ProductCode);
        }

        if (row.RawDate.Length == 0)
        {
            return Missing(fields.TransactionDate);
        }

        if (row.RawAmount.Length == 0)
        {
            return Missing(fields.Amount);
        }

        if (row.TransactionCode.Length > 100)
        {
            return (fields.TransactionCode, "INVALID_VALUE", $"'{fields.TransactionCode}' không được vượt quá 100 ký tự.");
        }

        if (row.CustomerCode is { Length: > 50 })
        {
            return (fields.CustomerCode, "INVALID_VALUE", $"'{fields.CustomerCode}' không được vượt quá 50 ký tự.");
        }

        if (row.ProductCode.Length > 100)
        {
            return (fields.ProductCode, "INVALID_VALUE", $"'{fields.ProductCode}' không được vượt quá 100 ký tự.");
        }

        if (row.Date is null)
        {
            return (fields.TransactionDate, "INVALID_VALUE", $"'{fields.TransactionDate}' không đúng định dạng ngày (dd/MM/yyyy hoặc yyyy-MM-dd).");
        }

        if (row.Amount is null or < 0)
        {
            return (fields.Amount, "INVALID_VALUE", $"'{fields.Amount}' phải là số không âm (>= 0).");
        }

        if (row.RawDenominator.Length > 0 && row.Denominator is null or <= 0)
        {
            return (fields.Denominator, "INVALID_VALUE", $"'{fields.Denominator}' phải là số lớn hơn 0.");
        }

        if (row.ReferEmployeeCode is { Length: > 50 })
        {
            return (fields.ReferEmployeeCode, "INVALID_VALUE", $"'{fields.ReferEmployeeCode}' không được vượt quá 50 ký tự.");
        }

        if (!employees.TryGetValue(row.EmployeeCode, out var employee))
        {
            return (fields.EmployeeCode, "EMPLOYEE_NOT_FOUND", $"Mã nhân viên '{row.EmployeeCode}' không tồn tại.");
        }

        if (employee.Status == EmployeeStatus.Terminated)
        {
            return (fields.EmployeeCode, "EMPLOYEE_TERMINATED", $"Nhân viên '{row.EmployeeCode}' đã nghỉ việc (TERMINATED).");
        }

        if (!products.Contains(row.ProductCode))
        {
            return (fields.ProductCode, "PRODUCT_NOT_FOUND", $"Mã sản phẩm '{row.ProductCode}' không thuộc danh mục sản phẩm.");
        }

        if (row.ReferEmployeeCode is not null && !employees.ContainsKey(row.ReferEmployeeCode))
        {
            return (fields.ReferEmployeeCode, "REFER_EMPLOYEE_NOT_FOUND", $"Mã nhân viên giới thiệu '{row.ReferEmployeeCode}' không tồn tại.");
        }

        return null;

        static (string, string, string) Missing(string field) =>
            (field, "REQUIRED_FIELD_MISSING", $"'{field}' là bắt buộc nhập.");
    }

    private (List<Row> Rows, FieldNames Fields) ReadRows(Stream excel)
    {
        try
        {
            var sheet = reader.Read(excel, parameters.Value.ImportMaxRows);
            var template = TransactionImportTemplateProvider.Template;
            var headerIndex = template.BuildHeaderIndex(sheet.Headers, ErrorCodes.ImportInvalidFile);

            if (sheet.Rows.Count == 0)
            {
                throw new AppException("File không có dòng dữ liệu nào.", ErrorCodes.ImportInvalidFile, 400);
            }

            var fields = new FieldNames(
                ImportHeaderNormalizer.GetMatchedHeader(TransactionImportTemplateProvider.TransactionCodeHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(TransactionImportTemplateProvider.TransactionDateHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(TransactionImportTemplateProvider.CustomerCodeHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.HasColumn(TransactionImportTemplateProvider.CustomerCodeHeader, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(TransactionImportTemplateProvider.EmployeeCodeHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(TransactionImportTemplateProvider.ProductCodeHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(TransactionImportTemplateProvider.AmountHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(TransactionImportTemplateProvider.DenominatorHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(TransactionImportTemplateProvider.ReferEmployeeCodeHeader, sheet.Headers, headerIndex));

            var rows = new List<Row>(sheet.Rows.Count);
            foreach (var rawRow in sheet.Rows)
            {
                var mapped = template.MapRow(rawRow, headerIndex);
                var rawDate = mapped.TransactionDate ?? string.Empty;
                var rawAmount = mapped.Amount ?? string.Empty;
                var rawDenominator = mapped.Denominator ?? string.Empty;

                DateOnly? parsedDate = CellParsers.TryParseDate(rawDate, out var date) ? date : null;
                decimal? parsedAmount = CellParsers.TryParseAmount(rawAmount, out var amount) ? amount : null;
                decimal? parsedDenominator = CellParsers.TryParseAmount(rawDenominator, out var denominator) ? denominator : null;

                rows.Add(new Row(
                    rawRow.RowNumber,
                    WorkTransactionService.NormalizeCode(mapped.TransactionCode),
                    string.IsNullOrWhiteSpace(mapped.CustomerCode) ? null : mapped.CustomerCode,
                    mapped.EmployeeCode ?? string.Empty,
                    mapped.ProductCode ?? string.Empty,
                    rawDate,
                    rawAmount,
                    rawDenominator,
                    parsedDate,
                    parsedAmount,
                    parsedDenominator,
                    string.IsNullOrWhiteSpace(mapped.ReferEmployeeCode) ? null : mapped.ReferEmployeeCode));
            }

            return (rows, fields);
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

    private static string ClampField(string field) =>
        field.Length > MaxFieldLength ? field[..MaxFieldLength] : field;

    private sealed record EmployeeLookup(Guid Id, string Code, EmployeeStatus Status);

    private sealed record FieldNames(
        string TransactionCode,
        string TransactionDate,
        string CustomerCode,
        bool HasCustomerCode,
        string EmployeeCode,
        string ProductCode,
        string Amount,
        string Denominator,
        string ReferEmployeeCode);

    private sealed record Row(
        int Number,
        string TransactionCode,
        string? CustomerCode,
        string EmployeeCode,
        string ProductCode,
        string RawDate,
        string RawAmount,
        string RawDenominator,
        DateOnly? Date,
        decimal? Amount,
        decimal? Denominator,
        string? ReferEmployeeCode);
}
