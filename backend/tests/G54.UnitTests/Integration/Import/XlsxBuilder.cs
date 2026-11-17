using ClosedXML.Excel;

namespace G54.UnitTests.Integration.Import;

/// <summary>Builds a one-sheet .xlsx in memory: row 1 headers, then data rows. A cell value keeps its type
/// (<see cref="DateTime"/> becomes a real Excel date cell, numbers stay numeric, strings stay text).</summary>
public static class XlsxBuilder
{
    public static byte[] Build(IReadOnlyList<string> headers, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        for (var c = 0; c < headers.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                switch (rows[r][c])
                {
                    case null:
                        break;
                    case DateTime date:
                        cell.Value = date;
                        break;
                    case decimal number:
                        cell.Value = number;
                        break;
                    case int number:
                        cell.Value = number;
                        break;
                    case string text:
                        // Keep text as text (e.g. "05/10/2026", "15.000.000") instead of letting Excel convert it.
                        cell.Value = text;
                        cell.Style.NumberFormat.Format = "@";
                        break;
                    default:
                        cell.Value = rows[r][c]!.ToString();
                        break;
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Header row of <c>06_Giao_dich.xlsx</c> plus <c>Refer ID</c>.</summary>
    public static readonly string[] TransactionHeaders =
        ["Mã giao dịch*", "Ngày GD*", "Mã khách hàng*", "Mã NV*", "Mã sản phẩm*", "Tên sản phẩm", "Số tiền (VND)*", "Refer ID"];

    /// <summary>One transaction row in <see cref="TransactionHeaders"/> order.</summary>
    public static object?[] Tx(string code, object date, string employee, string product, object amount, string customer = "KH1", string? refer = null) =>
        [code, date, customer, employee, product, null, amount, refer];

    public static byte[] Transactions(params object?[][] rows) => Build(TransactionHeaders, rows);

    /// <summary>Header row of <c>02_Nhan_su_To_chuc.xlsx</c> (without the demo note column).</summary>
    public static readonly string[] EmployeeHeaders =
        ["Mã chi nhánh*", "Tên chi nhánh*", "Mã CN cha", "Mã phòng ban*", "Tên phòng ban*", "Đơn vị cha", "Mã NV*", "Họ tên*", "Email*", "Vị trí*", "Mã quản lý trực tiếp", "Trạng thái*"];

    public static object?[] Emp(string code, string name, string email, string position = "TELLER", string? manager = null, string status = "ACTIVE",
        string branch = "ITB", string dept = "IT-DEPT") =>
        [branch, "CN Integration", null, dept, "Phòng Integration", branch, code, name, email, position, manager, status];

    public static byte[] Employees(params object?[][] rows) => Build(EmployeeHeaders, rows);
}
