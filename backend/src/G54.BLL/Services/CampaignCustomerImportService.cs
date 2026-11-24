using System.Text.RegularExpressions;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Template;
using G54.BLL.Dtos.DataImport;
using G54.BLL.Services.Import.CampaignCustomer;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// Imports a campaign's customer list (CIF, name, phone, note): read -> map -> validate -> upsert in one
/// transaction (committing valid rows and skipping invalid ones). An existing CIF updates name/phone/note only;
/// the assignee and contact status are kept. Contract: docs/api/CAMPAIGN_CONTRACT.md §5.
/// </summary>
public sealed partial class CampaignCustomerImportService(
    IUnitOfWork uow,
    AppDbContext db,
    IExcelSheetReader reader,
    IOptions<SystemParametersOptions> parameters) : ICampaignCustomerImportService
{
    private const string CifHeader = "Customer CIF*";
    private const string NameHeader = "Tên khách hàng*";
    private const string PhoneHeader = "Số điện thoại";
    private const string NoteHeader = "Ghi chú";

    private static readonly ImportTemplate<CampaignCustomerRow> Template = new TemplateBuilder<CampaignCustomerRow>()
        .Column(CifHeader, (r, v) => r.CustomerCif = Upper(v))
        .Column(NameHeader, (r, v) => r.CustomerName = v)
        .Column(PhoneHeader, (r, v) => r.Phone = string.IsNullOrWhiteSpace(v) ? null : v.Trim(), required: false)
        .Column(NoteHeader, (r, v) => r.Note = string.IsNullOrWhiteSpace(v) ? null : v, required: false)
        .Build();

    public byte[] GetTemplate() => TemplateWorkbookWriter.Write(Template);

    public async Task<ImportPreviewDto> PreviewAsync(
        Guid campaignId, string fileName, Stream file, CancellationToken cancellationToken)
    {
        await EnsureCampaignIsOpenAsync(campaignId, cancellationToken);
        var (rows, _) = await AnalyzeAsync(campaignId, file, cancellationToken);

        var invalid = rows.Count(r => r.Errors.Count > 0);
        var create = rows.Count(r => r.Errors.Count == 0 && !r.IsUpdate);
        var update = rows.Count(r => r.Errors.Count == 0 && r.IsUpdate);

        return new ImportPreviewDto(
            fileName,
            new ImportSummaryDto(rows.Count, rows.Count - invalid, invalid, create, update),
            rows.Select(ToPreviewRow).ToList());
    }

    public async Task<ImportCommitResultDto> CommitAsync(
        Guid campaignId, Stream file, Guid currentUserId, CancellationToken cancellationToken)
    {
        await EnsureCampaignIsOpenAsync(campaignId, cancellationToken);
        var (rows, ctx) = await AnalyzeAsync(campaignId, file, cancellationToken);
        var validRows = rows.Where(r => r.Errors.Count == 0).ToList();
        if (validRows.Count == 0)
        {
            throw new BusinessRuleException(ErrorCodes.DataImportHasErrors, null, 422);
        }

        var skippedRows = rows
            .Where(r => r.Errors.Count > 0)
            .Select(r => new ImportSkippedRowDto(
                r.RowNumber,
                r.Errors.Select(e => new ImportErrorDto(e.Column, e.Code, e.Message)).ToList()))
            .ToList();

        try
        {
            return await uow.ExecuteInTransactionAsync(
                ct => WriteAsync(campaignId, validRows, skippedRows, ctx, currentUserId, ct),
                cancellationToken);
        }
        catch (Exception ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            // The unique index does not say which row collided, so the message is file-level.
            throw new ConflictException(
                "Mã CIF đã tồn tại trong chiến dịch. Không có dữ liệu nào được ghi.",
                ErrorCodes.DataImportConflict);
        }
    }

    // ---- read + validate -------------------------------------------------------------------------------

    private async Task EnsureCampaignIsOpenAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.BusinessCampaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
        {
            throw new NotFoundException("Chiến dịch", campaignId);
        }

        if (campaign.Status is not (BusinessCampaignStatus.Planned or BusinessCampaignStatus.Running))
        {
            throw new BusinessRuleException(ErrorCodes.CampaignNotRunning, null, 409);
        }
    }

    private async Task<(List<CampaignCustomerRow> Rows, ImportContext Context)> AnalyzeAsync(
        Guid campaignId, Stream file, CancellationToken ct)
    {
        var sheet = reader.Read(file, parameters.Value.ImportMaxRows);
        var headerIndex = Template.BuildHeaderIndex(sheet.Headers);

        if (sheet.Rows.Count == 0)
        {
            throw new ValidationException("File không có dòng dữ liệu nào (chỉ có dòng tiêu đề hoặc để trống).");
        }

        var rows = sheet.Rows
            .Select(raw =>
            {
                var row = Template.MapRow(raw, headerIndex);
                row.RowNumber = raw.RowNumber;
                return row;
            })
            .ToList();

        var context = await LoadContextAsync(campaignId, rows, ct);
        Validate(rows);
        foreach (var row in rows.Where(r => r.Errors.Count == 0))
        {
            if (context.ExistingByCif.TryGetValue(row.CustomerCif!, out var existing))
            {
                row.IsUpdate = true;
                row.ExistingId = existing.Id;
            }
        }

        return (rows, context);
    }

    private static void Validate(List<CampaignCustomerRow> rows)
    {
        foreach (var row in rows)
        {
            Require(row, CifHeader, row.CustomerCif);
            Require(row, NameHeader, row.CustomerName);

            if (row.CustomerCif is { Length: > 50 })
            {
                row.AddError(CifHeader, ErrorCodes.DataImportInvalidValue, $"'{CifHeader}' tối đa 50 ký tự.");
            }

            if (row.CustomerName is { Length: > 200 })
            {
                row.AddError(NameHeader, ErrorCodes.DataImportInvalidValue, $"'{NameHeader}' tối đa 200 ký tự.");
            }

            if (row.Phone is not null && !IsValidPhone(row.Phone))
            {
                row.AddError(PhoneHeader, ErrorCodes.DataImportInvalidValue, $"'{PhoneHeader}' không hợp lệ.");
            }

            if (row.Note is { Length: > 1000 })
            {
                row.AddError(NoteHeader, ErrorCodes.DataImportInvalidValue, $"'{NoteHeader}' tối đa 1000 ký tự.");
            }
        }

        // Duplicate CIFs within the file: keep the first occurrence and flag subsequent rows.
        foreach (var group in rows
            .Where(r => r.CustomerCif is not null)
            .GroupBy(r => r.CustomerCif!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            var first = group.First();
            foreach (var row in group.Skip(1))
            {
                row.AddError(CifHeader, ErrorCodes.DataImportDuplicate, $"CIF '{first.CustomerCif}' trùng với dòng {first.RowNumber}.");
            }
        }
    }

    private static bool IsValidPhone(string phone)
    {
        if (!PhoneCharacters().IsMatch(phone))
        {
            return false;
        }

        var digitCount = phone.Count(char.IsDigit);
        return digitCount is >= 9 and <= 15;
    }

    private static void Require(CampaignCustomerRow row, string column, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            row.AddError(column, ErrorCodes.RequiredField, $"'{column}' là bắt buộc nhập.");
        }
    }

    private static string? Upper(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static ImportPreviewRowDto ToPreviewRow(CampaignCustomerRow r) => new(
        r.RowNumber,
        r.Errors.Count > 0 ? "INVALID" : r.IsUpdate ? "UPDATE" : "CREATE",
        new Dictionary<string, string?>
        {
            [CifHeader] = r.CustomerCif,
            [NameHeader] = r.CustomerName,
            [PhoneHeader] = r.Phone,
            [NoteHeader] = r.Note,
        },
        r.Errors.Select(e => new ImportErrorDto(e.Column, e.Code, e.Message)).ToList());

    // ---- load existing data (one batch, no N+1) --------------------------------------------------------

    private sealed class ImportContext
    {
        public required Dictionary<string, G54.DAL.Entities.CampaignCustomer> ExistingByCif { get; init; }
    }

    private async Task<ImportContext> LoadContextAsync(Guid campaignId, List<CampaignCustomerRow> rows, CancellationToken ct)
    {
        var cifs = rows
            .Select(r => r.CustomerCif)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Tracked on purpose: upserts mutate these entities (WriteAsync updates them directly).
        var existing = await db.CampaignCustomers
            .Where(c => c.BusinessCampaignId == campaignId && cifs.Contains(c.CustomerCif))
            .ToListAsync(ct);

        return new ImportContext
        {
            ExistingByCif = existing.ToDictionary(c => c.CustomerCif, StringComparer.OrdinalIgnoreCase),
        };
    }

    // ---- write -----------------------------------------------------------------------------------------

    private async Task<ImportCommitResultDto> WriteAsync(
        Guid campaignId,
        List<CampaignCustomerRow> rows,
        List<ImportSkippedRowDto> skippedRows,
        ImportContext ctx,
        Guid currentUserId,
        CancellationToken ct)
    {
        var created = 0;
        var updated = 0;

        foreach (var row in rows)
        {
            if (ctx.ExistingByCif.TryGetValue(row.CustomerCif!, out var customer))
            {
                // Update only: assignee and contact status are kept.
                updated++;
            }
            else
            {
                customer = new G54.DAL.Entities.CampaignCustomer
                {
                    Id = Guid.NewGuid(),
                    BusinessCampaignId = campaignId,
                    CustomerCif = row.CustomerCif!,
                };
                db.CampaignCustomers.Add(customer);
                ctx.ExistingByCif[row.CustomerCif!] = customer;
                created++;
            }

            customer.CustomerName = row.CustomerName!;
            customer.Phone = row.Phone;
            customer.Note = row.Note;
            customer.UpdatedAt = DateTimeOffset.UtcNow;
            customer.UpdatedBy = currentUserId;
        }

        await db.SaveChangesAsync(ct);
        return new ImportCommitResultDto(created, updated, skippedRows.Count, [], skippedRows);
    }

    [GeneratedRegex(@"^[0-9+ ]+$")]
    private static partial Regex PhoneCharacters();
}
