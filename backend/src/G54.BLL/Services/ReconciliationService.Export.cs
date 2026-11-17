using System.Globalization;
using ClosedXML.Excel;
using G54.BLL.Dtos.Reconciliation;
using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>The Excel export of the reconciliation list (the same rows and filters, every page).</summary>
public sealed partial class ReconciliationService
{
    private static readonly string[] ExportHeaders =
    [
        "Mã việc", "Tên công việc", "Mã nhân viên", "Nhân viên", "Vị trí", "Phòng ban", "Mã giao dịch", "Mã khách hàng (CIF)",
        "Mã người giới thiệu", "Sản phẩm", "Số tiền (VND)", "Ngày giao dịch", "Nộp lúc", "Trạng thái", "Lý do",
    ];

    public async Task<ReconciliationExportDto> ExportAsync(
        Guid userId,
        ReconciliationFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var status = ParseStatus(filter.Status);
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var filtered = await FilteredAsync(filter, scope.Departments, cancellationToken);
        var query = status is null ? filtered : filtered.Where(t => t.Status == status);
        var items = await LoadItemsAsync(query, 0, MaxExportRows, cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Đối soát");
        sheet.Cell(1, 1).Value = "Kết quả đối soát giao dịch";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value =
            $"Xuất lúc {time.GetUtcNow().ToOffset(BusinessOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} · {items.Count} dòng · Nguồn: file giao dịch Core Banking đã nhập vào hệ thống";
        sheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#6B7280");

        const int headerRow = 4;
        for (var c = 0; c < ExportHeaders.Length; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = ExportHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
        }

        for (var r = 0; r < items.Count; r++)
        {
            var item = items[r];
            var row = headerRow + 1 + r;
            sheet.Cell(row, 1).Value = item.WorkCode;
            sheet.Cell(row, 2).Value = item.WorkTitle;
            sheet.Cell(row, 3).Value = item.EmployeeCode;
            sheet.Cell(row, 4).Value = item.EmployeeName;
            sheet.Cell(row, 5).Value = item.JobTitle ?? string.Empty;
            sheet.Cell(row, 6).Value = item.DepartmentName ?? string.Empty;
            sheet.Cell(row, 7).Value = item.TransactionCode;
            sheet.Cell(row, 8).Value = item.CustomerCif ?? string.Empty;
            sheet.Cell(row, 9).Value = item.ReferEmployeeCode ?? string.Empty;
            sheet.Cell(row, 10).Value = item.ProductCode ?? string.Empty;
            if (item.Amount is { } amount)
            {
                sheet.Cell(row, 11).Value = amount;
                sheet.Cell(row, 11).Style.NumberFormat.Format = "#,##0";
            }

            sheet.Cell(row, 12).Value = item.TransactionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
            sheet.Cell(row, 13).Value = item.SubmittedAt.ToOffset(BusinessOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            sheet.Cell(row, 14).Value = StatusLabel(item.Status);
            sheet.Cell(row, 15).Value = ReasonLabel(item.Reason);
            if (item.Status == WorkTransactionStatus.NotMatched)
            {
                sheet.Range(row, 14, row, 15).Style.Font.FontColor = XLColor.FromHtml("#C00000");
            }
        }

        sheet.Range(headerRow, 1, headerRow + Math.Max(items.Count, 1), ExportHeaders.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns().AdjustToContents(headerRow, headerRow + Math.Min(items.Count, 200) + 1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var stamp = time.GetUtcNow().ToOffset(BusinessOffset).ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        return new ReconciliationExportDto(stream.ToArray(), $"doi-soat-{stamp}.xlsx");
    }

    private static string StatusLabel(string status) => status switch
    {
        WorkTransactionStatus.Matched => "Đã khớp",
        WorkTransactionStatus.NotMatched => "Không khớp",
        _ => "Chờ đối chiếu",
    };

    private static string ReasonLabel(string? reason) => reason switch
    {
        null or "" => string.Empty,
        "EMPLOYEE_MISMATCH" => "Sai mã nhân viên",
        "PRODUCT_MISMATCH" => "Sai sản phẩm",
        "CUSTOMER_MISMATCH" => "Sai mã CIF khách hàng",
        "REFERRER_MISMATCH" => "Sai mã người giới thiệu",
        "TRANSACTION_BEFORE_HANDOVER" => "Giao dịch trước thời điểm bàn giao",
        "METRIC_TYPE_MISMATCH" => "Loại chỉ số không phù hợp",
        NoImportedTransaction => "Chưa có trong file giao dịch",
        "KPI_TARGET_NOT_ASSIGNED" => "Nhân viên chưa được giao chỉ tiêu KPI",
        "KPI_AGGREGATION_NOT_SUPPORTED" => "KPI này chưa hỗ trợ tính từ giao dịch",
        "PERIOD_LOCKED" => "Kỳ đánh giá đã khóa",
        "REFERRER_NOT_CREDITED" => "Đã khớp, người giới thiệu chưa có chỉ tiêu KPI",
        _ => reason,
    };
}
