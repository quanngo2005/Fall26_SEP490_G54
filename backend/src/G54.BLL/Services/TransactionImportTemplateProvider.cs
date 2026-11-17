using G54.BLL.Common.Import.Template;

namespace G54.BLL.Services;

/// <summary>
/// Declares and builds the Core Banking transaction import template.
/// Contract: docs/api/TRANSACTION_IMPORT_CONTRACT.md (SRS UC29).
/// </summary>
public sealed class TransactionImportTemplateProvider : ITransactionImportTemplateProvider
{
    public const string TransactionCodeHeader = "Mã giao dịch*";
    public const string TransactionDateHeader = "Ngày GD*";
    public const string CustomerCodeHeader = "Mã khách hàng*";
    public const string EmployeeCodeHeader = "Mã NV*";
    public const string ProductCodeHeader = "Mã sản phẩm*";
    public const string ProductNameHeader = "Tên sản phẩm";
    public const string AmountHeader = "Số tiền (VND)*";
    public const string DenominatorHeader = "Mẫu số";
    public const string ReferEmployeeCodeHeader = "Refer ID";
    public const string NoteHeader = "Ghi chú (demo)";

    public sealed class Row
    {
        public string? TransactionCode { get; set; }
        public string? TransactionDate { get; set; }
        public string? CustomerCode { get; set; }
        public string? EmployeeCode { get; set; }
        public string? ProductCode { get; set; }
        public string? Amount { get; set; }
        public string? Denominator { get; set; }
        public string? ReferEmployeeCode { get; set; }
    }

    public static readonly ImportTemplate<Row> Template = new TemplateBuilder<Row>()
        .Column(TransactionCodeHeader, (r, v) => r.TransactionCode = v, required: true, aliases: ["transaction_code"])
        .Column(TransactionDateHeader, (r, v) => r.TransactionDate = v, required: true, aliases: ["transaction_date", "Ngày giao dịch"])
        .Column(CustomerCodeHeader, (r, v) => r.CustomerCode = v, required: false, aliases: ["customer_code"])
        .Column(EmployeeCodeHeader, (r, v) => r.EmployeeCode = v, required: true, aliases: ["employee_code", "Mã nhân viên"])
        .Column(ProductCodeHeader, (r, v) => r.ProductCode = v, required: true, aliases: ["product_code"])
        .Column(ProductNameHeader, (_, _) => { }, required: false, aliases: ["product_name"])
        .Column(AmountHeader, (r, v) => r.Amount = v, required: true, aliases: ["amount", "Số tiền"])
        .Column(DenominatorHeader, (r, v) => r.Denominator = v, required: false, aliases: ["denominator"])
        .Column(ReferEmployeeCodeHeader, (r, v) => r.ReferEmployeeCode = v, required: false, aliases: ["refer_employee_code", "Mã NV giới thiệu"])
        .Column(NoteHeader, (_, _) => { }, required: false, aliases: ["note"])
        .Build();

    private static readonly (string Column, string Required, string Type, string Description)[] Notes =
    [
        (TransactionCodeHeader, "Có", "Text (tối đa 100 ký tự)", "Mã giao dịch Core Banking duy nhất trên toàn hệ thống (alias: transaction_code)"),
        (TransactionDateHeader, "Có", "Date (dd/MM/yyyy hoặc yyyy-MM-dd)", "Ngày phát sinh giao dịch (alias: transaction_date)"),
        (CustomerCodeHeader, "Có (khi có cột)", "Text (tối đa 50 ký tự)", "Mã khách hàng CIF trên Core Banking (alias: customer_code)"),
        (EmployeeCodeHeader, "Có", "Text", "Mã nhân viên thực hiện giao dịch, trạng thái đang làm việc (alias: employee_code)"),
        (ProductCodeHeader, "Có", "Text (tối đa 100 ký tự)", "Mã sản phẩm thuộc danh mục WorkType.ProductCode (alias: product_code)"),
        (ProductNameHeader, "Không", "Text", "Tên sản phẩm tham khảo (bỏ qua khi import)"),
        (AmountHeader, "Có", "Số thực (>= 0)", "Giá trị hoặc số tiền giao dịch VND, hỗ trợ dấu phân cách nghìn (alias: amount)"),
        (DenominatorHeader, "Không", "Số thực (> 0)", "Mẫu số cho KPI dạng tỷ lệ (RATIO) hoặc trung bình (AVG) (alias: denominator)"),
        (ReferEmployeeCodeHeader, "Không", "Text (tối đa 50 ký tự)", "Mã nhân viên giới thiệu BR-07 (alias: refer_employee_code, Mã NV giới thiệu)"),
        (NoteHeader, "Không", "Text", "Ghi chú minh họa (bỏ qua khi import)")
    ];

    public byte[] GetTemplate() => TemplateWorkbookWriter.Write(
        Template,
        worksheet =>
        {
            worksheet.Cell(2, 1).Value = "TX-SAMPLE-0001";
            worksheet.Cell(2, 2).Value = "05/10/2026";
            worksheet.Cell(2, 3).Value = "KH10001";
            worksheet.Cell(2, 4).Value = "NV001";
            worksheet.Cell(2, 5).Value = "TD_CLASSIC";
            worksheet.Cell(2, 6).Value = "Mở thẻ tín dụng Classic";
            worksheet.Cell(2, 7).Value = 10000000m;
            worksheet.Cell(2, 8).Value = 1m;
            worksheet.Cell(2, 9).Value = "NV002";
            worksheet.Cell(2, 10).Value = string.Empty;
        },
        Notes);
}
