using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Parsing;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Template;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.ServiceRatingImport;
using G54.BLL.Dtos.TransactionImport;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// Imports customer service ratings (UC32) from <c>07_Danh_gia_dich_vu.xlsx</c>: valid rows are stored, the others are reported
/// and persisted as batch errors. Each 1-star rating alerts the department managers of the rated employee (BR-17).
/// Contract: docs/api/SERVICE_RATING_IMPORT_CONTRACT.md.
/// </summary>
public sealed class ServiceRatingImportService(
    IUnitOfWork uow,
    AppDbContext db,
    TimeProvider clock,
    IExcelSheetReader reader,
    IOptions<SystemParametersOptions> parameters,
    INotifier? notifier = null) : IServiceRatingImportService
{
    // MSG27 (SRS BR-29): the evaluation period of the rating date is locked.
    private const string PeriodLockedMessage =
        "Kỳ đánh giá chứa ngày đánh giá này đã bị khóa; mọi thay đổi phải thực hiện qua bút toán đảo (Reversal Entry) ở kỳ đang mở.";

    private const int MaxTransactionCodeLength = 100;
    private const int MaxCommentLength = 1000;

    public async Task<ImportBatchDto> ImportAsync(string fileName, Stream excel, Guid userId, CancellationToken ct = default)
    {
        var (rows, fields) = ReadRows(excel);
        var importerEmployeeId = await ImportBatchSupport.ResolveEmployeeIdAsync(uow, userId, ct);
        var employees = await LoadEmployeesAsync(rows, ct);
        var lockedPeriods = await ImportBatchSupport.LoadLockedPeriodsAsync(uow, rows.Where(r => r.Date is not null).Select(r => r.Date!.Value).ToList(), ct);

        // A concurrent import of the same codes can win the unique index between our read and our insert:
        // reload what exists and classify the rows again, once, instead of failing the whole request.
        for (var attempt = 0; ; attempt++)
        {
            var existing = await LoadExistingAsync(rows, ct);
            var (batch, errors) = Build(fileName, importerEmployeeId ?? userId, rows, fields, employees, lockedPeriods, existing);
            await uow.ImportBatches.AddAsync(batch, ct);
            try
            {
                await uow.SaveChangesAsync(ct);
                await NotifyAsync(batch, errors, importerEmployeeId, employees, ct);
                return ImportBatchSupport.ToDto(batch, errors);
            }
            catch (DbUpdateException ex) when (attempt < 2 && PostgresConflict.IsUniqueViolation(ex))
            {
                ImportBatchSupport.Detach(uow, batch);
            }
        }
    }

    public Task<PagedResult<ImportBatchDto>> ListBatchesAsync(int page, int pageSize, CancellationToken ct = default) =>
        ImportBatchSupport.ListBatchesAsync(uow, ImportFileType.ServiceRating, page, pageSize, ct);

    public Task<ImportBatchDto> GetBatchAsync(Guid id, CancellationToken ct = default) =>
        ImportBatchSupport.GetBatchAsync(uow, ImportFileType.ServiceRating, id, ct);

    public Task<PagedResult<ImportRowErrorDto>> ListErrorsAsync(Guid batchId, int page, int pageSize, CancellationToken ct = default) =>
        ImportBatchSupport.ListErrorsAsync(uow, ImportFileType.ServiceRating, batchId, page, pageSize, ct);

    public async Task<PagedResult<ServiceRatingDto>> ListRatingsAsync(Guid batchId, int page, int pageSize, CancellationToken ct = default)
    {
        await ImportBatchSupport.EnsureBatchExistsAsync(uow, ImportFileType.ServiceRating, batchId, ct);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = uow.Repository<ServiceRating>().Query().Where(r => r.ImportBatchId == batchId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(r => r.TransactionCode).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new ServiceRatingDto(r.Id, r.TransactionCode, r.Employee.EmployeeCode, r.RatingDate, r.Score, r.CustomerComment))
            .ToListAsync(ct);
        return new PagedResult<ServiceRatingDto>(items, total, page, pageSize);
    }

    // Row order: required/format/score/employee, then locked period (BR-29), then duplicates in the database or the file.
    private (ImportBatch Batch, List<ImportRowErrorDto> Errors) Build(
        string fileName,
        Guid userId,
        List<Row> rows,
        FieldNames fields,
        Dictionary<string, Employee> employees,
        List<(DateOnly Start, DateOnly End)> lockedPeriods,
        Dictionary<string, ServiceRating> existing)
    {
        var errors = new List<ImportRowErrorDto>();
        var now = clock.GetUtcNow();
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            FileType = ImportFileType.ServiceRating,
            FileName = fileName.Length > 255 ? fileName[..255] : fileName,
            TotalRows = rows.Count,
            ImportedBy = userId,
            ImportedAt = now,
        };
        var accepted = new Dictionary<string, ServiceRating>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;
        foreach (var row in rows)
        {
            var error = Validate(row, fields, employees);
            if (error is null && ImportBatchSupport.IsLocked(lockedPeriods, row.Date!.Value))
            {
                error = (fields.RatingDate, ImportBatchSupport.PeriodLockedCode, PeriodLockedMessage);
            }

            if (error is not null)
            {
                errors.Add(ImportBatchSupport.RowError(row.Number, error.Value.Field, error.Value.Code, error.Value.Message));
                continue;
            }

            var rating = new ServiceRating
            {
                Id = Guid.NewGuid(),
                ImportBatchId = batch.Id,
                EmployeeId = employees[row.EmployeeCode].Id,
                TransactionCode = row.TransactionCode,
                RatingDate = row.Date!.Value,
                Score = row.Score!.Value,
                CustomerComment = row.CustomerComment,
                CreatedAt = now,
            };
            var (current, place) = existing.TryGetValue(row.TransactionCode, out var stored)
                ? (stored, "trên hệ thống")
                : accepted.TryGetValue(row.TransactionCode, out var earlier) ? (earlier, "ở dòng trước trong file") : (null, null);
            if (current is null)
            {
                accepted[row.TransactionCode] = rating;
                batch.ServiceRatings.Add(rating);
            }
            else if (SameValues(current, rating))
            {
                duplicates++;
            }
            else
            {
                errors.Add(ImportBatchSupport.RowError(
                    row.Number,
                    fields.TransactionCode,
                    ImportBatchSupport.ConflictingDuplicateCode,
                    $"Mã giao dịch '{row.TransactionCode}' đã được đánh giá {place} với thông tin khác."));
            }
        }

        ImportBatchSupport.Complete(batch, batch.ServiceRatings.Count, duplicates, errors);
        return (batch, errors);
    }

    private static bool SameValues(ServiceRating current, ServiceRating candidate) =>
        current.EmployeeId == candidate.EmployeeId
        && current.RatingDate == candidate.RatingDate
        && current.Score == candidate.Score
        && string.Equals(current.CustomerComment, candidate.CustomerComment, StringComparison.Ordinal);

    /// <summary>BR-17 alerts and the importer's error notice. Best effort: the batch is already saved.</summary>
    private async Task NotifyAsync(
        ImportBatch batch,
        List<ImportRowErrorDto> errors,
        Guid? importerEmployeeId,
        Dictionary<string, Employee> employees,
        CancellationToken ct)
    {
        if (notifier is null)
        {
            return;
        }

        var drafts = new List<NotificationDraft>();
        if (errors.Count > 0 && importerEmployeeId is { } recipientId)
        {
            drafts.Add(AutomaticNotifications.ImportCompletedWithErrors(recipientId, "đánh giá dịch vụ", batch.FileName, batch.ImportedRows, batch.ErrorRows, batch.Id));
        }

        var employeesById = employees.Values.ToDictionary(e => e.Id);
        var managersByDepartment = new Dictionary<Guid, List<Guid>>();
        DepartmentHierarchy? hierarchy = null;
        foreach (var rating in batch.ServiceRatings.Where(r => r.Score == 1))
        {
            var employee = employeesById[rating.EmployeeId];
            if (!managersByDepartment.TryGetValue(employee.DepartmentId, out var managerIds))
            {
                hierarchy ??= await DepartmentHierarchy.LoadAsync(db.Departments, ct);
                managerIds = await NearestDepartmentManagersAsync(hierarchy, employee.DepartmentId, ct);
                managersByDepartment[employee.DepartmentId] = managerIds;
            }

            drafts.AddRange(AutomaticNotifications.ServiceRatedOneStar(rating, employee, managerIds));
        }

        if (drafts.Count > 0)
        {
            await notifier.TryPublishAsync(drafts, ct);
        }
    }

    /// <summary>
    /// Managers of the employee's department or, when it has none (a team), of the nearest ancestor that has one.
    /// </summary>
    private async Task<List<Guid>> NearestDepartmentManagersAsync(DepartmentHierarchy hierarchy, Guid departmentId, CancellationToken ct)
    {
        var chain = hierarchy.Nodes.ContainsKey(departmentId)
            ? hierarchy.Ancestors(departmentId).Select(a => a.Id).Reverse().Prepend(departmentId)
            : [departmentId];
        foreach (var id in chain)
        {
            var managerIds = await NotificationRecipients.ActiveDepartmentManagersAsync(db, id, ct);
            if (managerIds.Count > 0)
            {
                return managerIds;
            }
        }

        return [];
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "Translated to PostgreSQL upper(), using database collation rather than CLR culture.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Npgsql translates only the parameterless ToUpper() to SQL upper().")]
    private async Task<Dictionary<string, Employee>> LoadEmployeesAsync(List<Row> rows, CancellationToken ct)
    {
        var codes = rows
            .Select(r => r.EmployeeCode)
            .Where(c => c.Length > 0)
            .Select(c => c.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var employees = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in codes.Chunk(ImportBatchSupport.LookupChunk))
        {
            var upperChunk = chunk.ToList();
            var found = await uow.Employees.Query().AsNoTracking()
                .Where(e => upperChunk.Contains(e.EmployeeCode.ToUpper()))
                .ToListAsync(ct);
            foreach (var e in found)
            {
                employees[e.EmployeeCode] = e;
            }
        }

        return employees;
    }

    // Codes in the file are upper-cased; compare in upper case like the transaction import.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "Translated to PostgreSQL upper().")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Npgsql translates only the parameterless ToUpper() to SQL upper().")]
    private async Task<Dictionary<string, ServiceRating>> LoadExistingAsync(List<Row> rows, CancellationToken ct)
    {
        var existing = new Dictionary<string, ServiceRating>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in rows.Select(r => r.TransactionCode).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(ImportBatchSupport.LookupChunk))
        {
            var found = await uow.Repository<ServiceRating>().Query().AsNoTracking()
                .Where(r => chunk.Contains(r.TransactionCode.ToUpper()))
                .ToListAsync(ct);
            foreach (var r in found)
            {
                existing[r.TransactionCode] = r;
            }
        }

        return existing;
    }

    private static (string Field, string Code, string Message)? Validate(
        Row row,
        FieldNames fields,
        Dictionary<string, Employee> employees)
    {
        if (row.RawDate.Length == 0)
        {
            return Missing(fields.RatingDate);
        }

        if (row.EmployeeCode.Length == 0)
        {
            return Missing(fields.EmployeeCode);
        }

        if (row.TransactionCode.Length == 0)
        {
            return Missing(fields.TransactionCode);
        }

        if (row.RawScore.Length == 0)
        {
            return Missing(fields.Score);
        }

        if (row.Date is null)
        {
            return (fields.RatingDate, "INVALID_VALUE", $"'{fields.RatingDate}' không đúng định dạng ngày (dd/MM/yyyy).");
        }

        if (row.TransactionCode.Length > MaxTransactionCodeLength)
        {
            return (fields.TransactionCode, "INVALID_VALUE", $"'{fields.TransactionCode}' không được vượt quá {MaxTransactionCodeLength} ký tự.");
        }

        if (row.Score is null)
        {
            return (fields.Score, "INVALID_VALUE", $"'{fields.Score}' phải là số nguyên từ 1 đến 5.");
        }

        if (row.CustomerComment is { Length: > MaxCommentLength })
        {
            return (fields.CustomerComment, "INVALID_VALUE", $"'{fields.CustomerComment}' không được vượt quá {MaxCommentLength} ký tự.");
        }

        if (!employees.ContainsKey(row.EmployeeCode))
        {
            return (fields.EmployeeCode, "EMPLOYEE_NOT_FOUND", $"Mã nhân viên '{row.EmployeeCode}' không tồn tại.");
        }

        return null;

        static (string, string, string) Missing(string field) =>
            (field, "REQUIRED_FIELD_MISSING", $"'{field}' là bắt buộc nhập.");
    }

    private (List<Row> Rows, FieldNames Fields) ReadRows(Stream excel) => ImportBatchSupport.ReadRows(
        reader,
        excel,
        parameters.Value.ImportMaxRows,
        ServiceRatingImportTemplateProvider.Template,
        (sheet, headerIndex) =>
        {
            var template = ServiceRatingImportTemplateProvider.Template;
            var fields = new FieldNames(
                ImportHeaderNormalizer.GetMatchedHeader(ServiceRatingImportTemplateProvider.RatingDateHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(ServiceRatingImportTemplateProvider.EmployeeCodeHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(ServiceRatingImportTemplateProvider.TransactionCodeHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(ServiceRatingImportTemplateProvider.ScoreHeader, sheet.Headers, headerIndex),
                ImportHeaderNormalizer.GetMatchedHeader(ServiceRatingImportTemplateProvider.CustomerCommentHeader, sheet.Headers, headerIndex));

            var rows = new List<Row>(sheet.Rows.Count);
            foreach (var rawRow in sheet.Rows)
            {
                var mapped = template.MapRow(rawRow, headerIndex);
                var rawDate = mapped.RatingDate ?? string.Empty;
                var rawScore = mapped.Score ?? string.Empty;
                rows.Add(new Row(
                    rawRow.RowNumber,
                    rawDate,
                    CellParsers.TryParseDate(rawDate, out var date) ? date : null,
                    mapped.EmployeeCode ?? string.Empty,
                    WorkTransactionService.NormalizeCode(mapped.TransactionCode),
                    rawScore,
                    ParseScore(rawScore),
                    string.IsNullOrWhiteSpace(mapped.CustomerComment) ? null : mapped.CustomerComment));
            }

            return (rows, fields);
        });

    private static short? ParseScore(string raw) =>
        CellParsers.TryParseAmount(raw, out var value) && decimal.IsInteger(value) && value is >= 1 and <= 5 ? (short)value : null;

    private sealed record FieldNames(
        string RatingDate,
        string EmployeeCode,
        string TransactionCode,
        string Score,
        string CustomerComment);

    private sealed record Row(
        int Number,
        string RawDate,
        DateOnly? Date,
        string EmployeeCode,
        string TransactionCode,
        string RawScore,
        short? Score,
        string? CustomerComment);
}
