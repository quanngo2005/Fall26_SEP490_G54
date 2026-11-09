using ClosedXML.Excel;
using G54.BLL.Common.Import.Template;

namespace G54.BLL.Services;

/// <summary>
/// Declares and builds the Core Banking transaction import template.
/// Contract: docs/api/TRANSACTION_IMPORT_CONTRACT.md (SRS UC29).
/// </summary>
public sealed class TransactionImportTemplateProvider : ITransactionImportTemplateProvider
{
    public sealed class Row
    {
        public string? TransactionCode { get; set; }
        public string? EmployeeCode { get; set; }
        public string? ProductCode { get; set; }
        public string? TransactionDate { get; set; }
        public string? Amount { get; set; }
        public string? Denominator { get; set; }
        public string? ReferEmployeeCode { get; set; }
    }

    public static readonly ImportTemplate<Row> Template = new TemplateBuilder<Row>()
        .Column("transaction_code", (r, v) => r.TransactionCode = v, required: true)
        .Column("employee_code", (r, v) => r.EmployeeCode = v, required: true)
        .Column("product_code", (r, v) => r.ProductCode = v, required: true)
        .Column("transaction_date", (r, v) => r.TransactionDate = v, required: true)
        .Column("amount", (r, v) => r.Amount = v, required: true)
        .Column("denominator", (r, v) => r.Denominator = v, required: false)
        .Column("refer_employee_code", (r, v) => r.ReferEmployeeCode = v, required: false)
        .Build();

    private static readonly (string Column, string Required, string Type, string Description)[] Notes =
    [
        ("transaction_code", "Có", "Text (tối đa 100 ký tự)", "Mã giao dịch Core Banking duy nhất trên toàn hệ thống"),
        ("employee_code", "Có", "Text", "Mã nhân viên thực hiện giao dịch (phải tồn tại trong hệ thống)"),
        ("product_code", "Có", "Text (tối đa 100 ký tự)", "Mã sản phẩm tương ứng với loại công việc WorkType.ProductCode"),
        ("transaction_date", "Có", "Date (YYYY-MM-DD)", "Ngày phát sinh giao dịch"),
        ("amount", "Có", "Số thực (>= 0)", "Giá trị hoặc số tiền giao dịch"),
        ("denominator", "Không", "Số thực (> 0)", "Mẫu số cho KPI dạng tỷ lệ (RATIO) hoặc trung bình (AVG)"),
        ("refer_employee_code", "Không", "Text (tối đa 50 ký tự)", "Mã nhân viên giới thiệu (nếu có)")
    ];

    public byte[] GetTemplate() => TemplateWorkbookWriter.Write(
        Template,
        worksheet =>
        {
            worksheet.Cell(2, 1).Value = "TX-SAMPLE-0001";
            worksheet.Cell(2, 2).Value = "NV001";
            worksheet.Cell(2, 3).Value = "TD_CLASSIC";
            worksheet.Cell(2, 4).Value = "2026-11-01";
            worksheet.Cell(2, 5).Value = 10000000m;
            worksheet.Cell(2, 6).Value = 1m;
            worksheet.Cell(2, 7).Value = "NV002";
        },
        Notes);
}
