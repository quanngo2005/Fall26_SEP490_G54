using ClosedXML.Excel;
using G54.BLL.Common.Import.Reading;

namespace G54.BLL.Services.Import.BranchYearTarget;

/// <summary>
/// Generates the downloadable template for <c>GET /target-assignments/import/template?year</c> (plan task
/// 1.7): same layout as the real sample file (docs/excels/KPI_Chi_nhanh_2025_chuan_hoa.xlsx) - an optional
/// branch-code cell, a quarter-ratio row, then the header row <see cref="KpiTargetSheetReader"/> looks for.
/// </summary>
public static class BranchYearTargetTemplateWriter
{
    private static readonly string[] Headers =
    [
        "Mã KPI", "Tên KPI", "Đơn vị", "Chiều", "Trọng số", null!, null!, "Q1", "Q2", "Q3", "Q4", "Tổng hợp",
    ];

    public static byte[] Write(int year)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("KPI Năm-Quý");

        worksheet.Cell(1, 1).Value = "Mã chi nhánh:";
        worksheet.Cell(1, 1).Style.Font.Bold = true;

        worksheet.Cell(2, 2).Value = "Tỷ trọng phân bổ quý:";
        for (var quarter = 0; quarter < 4; quarter++)
        {
            worksheet.Cell(2, 8 + quarter).Value = 0.25;
        }

        for (var col = 0; col < Headers.Length; col++)
        {
            var header = Headers[col] switch
            {
                null => col == 5 ? $"Năm {year - 1}" : $"Năm {year}",
                var text => text,
            };
            var cell = worksheet.Cell(4, col + 1);
            cell.Value = header;
            cell.Style.Font.Bold = true;
        }

        worksheet.Columns(1, Headers.Length).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
