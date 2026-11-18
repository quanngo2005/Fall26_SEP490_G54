using ClosedXML.Excel;

namespace G54.UnitTests.Import;

/// <summary>
/// A synthetic workbook with the same layout as the business sample <c>06_Giao_dich.xlsx</c> (8 columns, 23 data rows,
/// text dates, numeric amounts) but made-up transaction codes (<c>TXS…</c>), customers (<c>KHS…</c>) and amounts: the
/// real sample is confidential and is never copied into the repository. Rows 2-19 are valid; rows 20-24 are invalid:
/// 20 code repeated from row 3, 21 in the locked September 2026, 22 unknown employee, 23 negative amount, 24 terminated
/// employee (NV006) with an unknown product.
/// </summary>
internal static class TransactionSampleWorkbook
{
    public const string CodePrefix = "TXS";

    private static readonly string[] Headers =
        ["Mã giao dịch*", "Ngày GD*", "Mã khách hàng*", "Mã NV*", "Mã sản phẩm*", "Tên sản phẩm", "Số tiền (VND)*", "Ghi chú (demo)"];

    private static readonly (string Code, string Date, string Customer, string Employee, string Product, decimal Amount, string? Note)[] Rows =
    [
        ("TXS0001", "01/10/2026", "KHS001", "NV001", "DEPOSIT", 1000000m, null),
        ("TXS0002", "01/10/2026", "KHS002", "NV001", "TRANSFER", 200000m, null),
        ("TXS0003", "02/10/2026", "KHS003", "NV002", "ACCOUNT_OPEN", 0m, null),
        ("TXS0004", "02/10/2026", "KHS004", "NV003", "CARD_DEBIT_STD", 0m, null),
        ("TXS0005", "03/10/2026", "KHS005", "NV003", "CARD_CREDIT_PLAT", 0m, null),
        ("TXS0006", "03/10/2026", "KHS006", "NV004", "TERM_DEPOSIT", 60000000m, null),
        ("TXS0007", "04/10/2026", "KHS007", "NV004", "EBANKING", 0m, null),
        ("TXS0008", "04/10/2026", "KHS008", "NV005", "WITHDRAWAL", 300000m, null),
        ("TXS0009", "05/10/2026", "KHS009", "NV002", "DEPOSIT", 4000000m, null),
        ("TXS0010", "06/10/2026", "KHS010", "NV009", "CARD_DEBIT_GOLD", 0m, null),
        ("TXS0011", "06/10/2026", "KHS011", "NV009", "SMS_BANKING", 0m, null),
        ("TXS0012", "07/10/2026", "KHS012", "NV012", "QR_MERCHANT", 0m, null),
        ("TXS0013", "07/10/2026", "KHS013", "NV012", "INSURANCE", 500000m, null),
        ("TXS0014", "08/10/2026", "KHS014", "NV012", "CARD_CREDIT_STD", 0m, null),
        ("TXS0015", "08/10/2026", "KHS015", "NV013", "ACCOUNT_OPEN", 0m, null),
        ("TXS0016", "09/10/2026", "KHS015", "NV013", "EBANKING", 0m, "Cùng khách hàng với dòng trên"),
        ("TXS0017", "09/10/2026", "KHS016", "NV005", "DEPOSIT", 700000m, null),
        ("TXS0018", "10/10/2026", "KHS017", "NV003", "TERM_DEPOSIT", 90000000m, null),
        ("TXS0002", "11/10/2026", "KHS018", "NV001", "TRANSFER", 100000m, "LỖI: trùng mã giao dịch"),
        ("TXS9001", "15/09/2026", "KHS019", "NV003", "EBANKING", 0m, "LỖI: thuộc kỳ đã khóa"),
        ("TXS0019", "12/10/2026", "KHS020", "NV999", "DEPOSIT", 100000m, "LỖI: mã NV không tồn tại"),
        ("TXS0020", "12/10/2026", "KHS021", "NV005", "DEPOSIT", -100000m, "LỖI: số tiền âm"),
        ("TXS0021", "13/10/2026", "KHS022", "NV006", "LOAN", 100000m, "LỖI: NV đã nghỉ; sản phẩm không có trong danh mục"),
    ];

    public static byte[] Build()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Giao dịch");
        for (var col = 0; col < Headers.Length; col++)
        {
            sheet.Cell(1, col + 1).Value = Headers[col];
        }

        for (var i = 0; i < Rows.Length; i++)
        {
            var (code, date, customer, employee, product, amount, note) = Rows[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = code;
            sheet.Cell(row, 2).Value = date; // dd/MM/yyyy text, like the business sample
            sheet.Cell(row, 3).Value = customer;
            sheet.Cell(row, 4).Value = employee;
            sheet.Cell(row, 5).Value = product;
            sheet.Cell(row, 6).Value = product;
            sheet.Cell(row, 7).Value = amount;
            if (note is not null)
            {
                sheet.Cell(row, 8).Value = note;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
