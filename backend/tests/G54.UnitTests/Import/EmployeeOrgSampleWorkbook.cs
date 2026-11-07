using ClosedXML.Excel;

namespace G54.UnitTests.Import;

/// <summary>
/// Builds, with ClosedXML, a workbook mirroring the real "02_Nhan_su_To_chuc.xlsx" template and sample
/// data described in the Data Import plan: branch HN01, 5 departments under it, and employees with 5
/// intentionally invalid rows (9, 19, 20, 21, 22). Tests never read a file from disk.
/// </summary>
internal static class EmployeeOrgSampleWorkbook
{
    private static readonly string[] Headers =
    [
        "Mã chi nhánh*", "Tên chi nhánh*", "Mã CN cha", "Mã phòng ban*", "Tên phòng ban*", "Đơn vị cha",
        "Mã NV*", "Họ tên*", "Email*", "Vị trí*", "Mã quản lý trực tiếp", "Trạng thái*", "Ghi chú (demo)",
    ];

    private sealed record Row(
        string? EmployeeCode, string? FullName, string? Email, string? Position,
        string? ManagerCode, string? Status,
        string DeptCode = "PGD-HK", string DeptName = "Phòng giao dịch Hoàng Kiếm");

    /// <summary>Row numbers (1-based, matching the Excel row) that the sample intentionally makes invalid.</summary>
    public static readonly int[] InvalidRowNumbers = [9, 19, 20, 21, 22];

    public static readonly string[] DepartmentCodes = ["PGD-HK", "PGD-BD", "BGD", "KSNB", "HCNS"];

    public static MemoryStream Build()
    {
        var rows = new List<Row>
        {
            new("NV001", "Nguyễn Văn An", "an.nguyen@bank.local", "BRANCH_MANAGER", null, "ACTIVE", "BGD", "Ban Giám đốc"),
            new("NV002", "Trần Thị Bình", "binh.tran@bank.local", "DEPT_MANAGER", "NV001", "ACTIVE"),
            new("NV003", "Lê Văn Cường", "cuong.le@bank.local", "DEPT_MANAGER", "NV001", "ACTIVE", "PGD-BD", "Phòng giao dịch Bà Đình"),
            new("NV004", "Phạm Thị Dung", "dung.pham@bank.local", "TELLER", "NV002", "ACTIVE"),
            new("NV006", "Hoàng Văn Em", "em.hoang@bank.local", "TELLER", "NV002", "ON_LEAVE"),
            new("NV007", "Vũ Thị Giang", "giang.vu@bank.local", "SALES_OFFICER", "NV003", "ACTIVE", "PGD-BD", "Phòng giao dịch Bà Đình"),
            new("NV008", "Đỗ Văn Hải", "hai.do@bank.local", "SALES_OFFICER", "NV003", "ACTIVE", "PGD-BD", "Phòng giao dịch Bà Đình"),
            // row 9 - valid on its own, but duplicated at row 21 -> both invalid.
            new("NV005", "Ngô Thị Kim", "kim.ngo@bank.local", "QA", "NV001", "ACTIVE", "KSNB", "Kiểm soát nội bộ"),
            new("NV009", "Bùi Văn Long", "long.bui@bank.local", "HR_OFFICER", "NV001", "ACTIVE", "HCNS", "Hành chính nhân sự"),
            new("NV010", "Đặng Thị Mai", "mai.dang@bank.local", "SYS_ADMIN", "NV001", "ACTIVE", "HCNS", "Hành chính nhân sự"),
            new("NV011", "Tô Văn Nam", "nam.to@bank.local", "TELLER", "NV002", "TERMINATED"),
            new("NV012", "Lý Thị Oanh", "oanh.ly@bank.local", "SALES_OFFICER", "NV003", "ACTIVE", "PGD-BD", "Phòng giao dịch Bà Đình"),
            new("NV013", "Mai Văn Phúc", "phuc.mai@bank.local", "TELLER", "NV001", "ACTIVE", "KSNB", "Kiểm soát nội bộ"),
            new("NV014", "Chu Thị Quỳnh", "quynh.chu@bank.local", "SALES_OFFICER", "NV003", "PENDING_RESIGNATION", "PGD-BD", "Phòng giao dịch Bà Đình"),
            new("NV015", "Phan Văn Sơn", "son.phan@bank.local", "TELLER", "NV002", "ACTIVE"),
            new("NV016", "Dương Thị Thu", "thu.duong@bank.local", "QA", "NV001", "ACTIVE", "KSNB", "Kiểm soát nội bộ"),
            new("NV017", "Lưu Văn Uy", "uy.luu@bank.local", "TELLER", "NV001", "ACTIVE", "HCNS", "Hành chính nhân sự"),
            // row 19 - invalid: unknown status.
            new("NV018", "Mạc Thị Tâm", "mac.tam@bank.local", "TELLER", "NV002", "INACTIVE"),
            // row 20 - invalid: missing name/email, unknown position, impossible date.
            new("NV019", "", "", "CREDIT_OFFICER", "NV003", "ACTIVE", "PGD-BD", "Phòng giao dịch Bà Đình"),
            // row 21 - invalid: duplicate employee code (same as row 9).
            new("NV005", "Ngô Thị Kim 2", "kim2.ngo@bank.local", "QA", "NV001", "ACTIVE", "KSNB", "Kiểm soát nội bộ"),
            // row 22 - invalid: manager code does not exist.
            new("NV020", "Kiều Văn Việt", "viet.kieu@bank.local", "BRANCH_MANAGER", "NV099", "ACTIVE", "BGD", "Ban Giám đốc"),
        };

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Data");
        for (var col = 0; col < Headers.Length; col++)
        {
            worksheet.Cell(1, col + 1).Value = Headers[col];
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var excelRow = i + 2;
            worksheet.Cell(excelRow, 1).Value = "HN01";
            worksheet.Cell(excelRow, 2).Value = "CN Hà Nội";
            worksheet.Cell(excelRow, 3).Value = "";
            worksheet.Cell(excelRow, 4).Value = r.DeptCode;
            worksheet.Cell(excelRow, 5).Value = r.DeptName;
            worksheet.Cell(excelRow, 6).Value = "";
            worksheet.Cell(excelRow, 7).Value = r.EmployeeCode;
            worksheet.Cell(excelRow, 8).Value = r.FullName;
            worksheet.Cell(excelRow, 9).Value = r.Email;
            worksheet.Cell(excelRow, 10).Value = r.Position;
            worksheet.Cell(excelRow, 11).Value = r.ManagerCode ?? "";
            worksheet.Cell(excelRow, 12).Value = r.Status;
            worksheet.Cell(excelRow, 13).Value = "";
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
