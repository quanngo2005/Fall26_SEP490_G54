using G54.BLL.Common.Import.Template;

namespace G54.BLL.Services;

/// <summary>
/// Declares and builds the customer service rating import template (sheet "Đánh giá dịch vụ").
/// Contract: docs/api/SERVICE_RATING_IMPORT_CONTRACT.md (SRS UC29).
/// </summary>
public sealed class ServiceRatingImportTemplateProvider : IServiceRatingImportTemplateProvider
{
    public const string SheetName = "Đánh giá dịch vụ";
    public const string RatingDateHeader = "Ngày đánh giá*";
    public const string EmployeeCodeHeader = "Mã NV*";
    public const string TransactionCodeHeader = "Mã giao dịch*";
    public const string ScoreHeader = "Điểm (1–5)*";
    public const string CustomerCommentHeader = "Nhận xét KH";
    public const string NoteHeader = "Ghi chú (demo)";

    public sealed class Row
    {
        public string? RatingDate { get; set; }
        public string? EmployeeCode { get; set; }
        public string? TransactionCode { get; set; }
        public string? Score { get; set; }
        public string? CustomerComment { get; set; }
    }

    public static readonly ImportTemplate<Row> Template = new TemplateBuilder<Row>()
        .Column(RatingDateHeader, (r, v) => r.RatingDate = v, required: true, aliases: ["rating_date"])
        .Column(EmployeeCodeHeader, (r, v) => r.EmployeeCode = v, required: true, aliases: ["employee_code", "Mã nhân viên"])
        .Column(TransactionCodeHeader, (r, v) => r.TransactionCode = v, required: true, aliases: ["transaction_code"])
        .Column(ScoreHeader, (r, v) => r.Score = v, required: true, aliases: ["score"])
        .Column(CustomerCommentHeader, (r, v) => r.CustomerComment = v, required: false, aliases: ["customer_comment"])
        .Column(NoteHeader, (_, _) => { }, required: false, aliases: ["note"])
        .Build();

    private static readonly (string Column, string Required, string Type, string Description)[] Notes =
    [
        (RatingDateHeader, "Có", "Date (dd/MM/yyyy)", "Ngày khách hàng đánh giá; không thuộc kỳ đánh giá đã khóa (alias: rating_date)"),
        (EmployeeCodeHeader, "Có", "Text", "Mã nhân viên được đánh giá (alias: employee_code)"),
        (TransactionCodeHeader, "Có", "Text (tối đa 100 ký tự)", "Mã giao dịch được đánh giá, mỗi giao dịch một đánh giá (alias: transaction_code)"),
        (ScoreHeader, "Có", "Số nguyên 1–5", "Số sao; đánh giá 1 sao được báo cho trưởng phòng của nhân viên (BR-17) (alias: score)"),
        (CustomerCommentHeader, "Không", "Text (tối đa 1000 ký tự)", "Nhận xét của khách hàng (alias: customer_comment)"),
        (NoteHeader, "Không", "Text", "Ghi chú minh họa (bỏ qua khi import)")
    ];

    public byte[] GetTemplate() => TemplateWorkbookWriter.Write(
        Template,
        worksheet =>
        {
            worksheet.Cell(2, 1).Value = "05/10/2026";
            worksheet.Cell(2, 2).Value = "NV001";
            worksheet.Cell(2, 3).Value = "TX-SAMPLE-0001";
            worksheet.Cell(2, 4).Value = 5;
            worksheet.Cell(2, 5).Value = "Tư vấn rõ ràng";
            worksheet.Cell(2, 6).Value = string.Empty;
        },
        Notes,
        SheetName);
}
