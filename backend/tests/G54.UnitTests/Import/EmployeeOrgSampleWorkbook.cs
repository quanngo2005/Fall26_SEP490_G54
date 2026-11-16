using ClosedXML.Excel;

namespace G54.UnitTests.Import;

/// <summary>
/// Provides the real <c>02_Nhan_su_To_chuc.xlsx</c> sample copied to <c>TestData/</c>, plus a ClosedXML builder
/// reproducing its exact 21 data rows (17 valid, 4 invalid on rows 19, 20, 21, 22).
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
        string DeptCode = "PGD-HK", string DeptName = "Phòng Giao dịch Hoàn Kiếm",
        string? Note = null);

    /// <summary>Row numbers (1-based, matching the Excel row) that the sample intentionally makes invalid.</summary>
    public static readonly int[] InvalidRowNumbers = [19, 20, 21, 22];

    public static readonly string[] DepartmentCodes = ["PGD-HK", "PGD-BD", "BGD", "KSNB", "HCNS"];

    public static Stream OpenRealFile() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "02_Nhan_su_To_chuc.xlsx"));

    public static MemoryStream Build()
    {
        var rows = new List<Row>
        {
            new("NV001", "Nguyễn Văn An", "an.nv@bank.vn", "TELLER", "NV010", "ACTIVE"),
            new("NV002", "Trần Thị Bình", "binh.tt@bank.vn", "TELLER", "NV010", "ACTIVE"),
            new("NV015", "Lý Thị Ngọc", "ngoc.lt@bank.vn", "TELLER", "NV010", "ON_LEAVE", Note: "Nghỉ phép – vẫn giữ tài khoản"),
            new("NV003", "Lê Hoàng Cường", "cuong.lh@bank.vn", "SALES_OFFICER", "NV010", "ACTIVE"),
            new("NV004", "Phạm Minh Dũng", "dung.pm@bank.vn", "SALES_OFFICER", "NV010", "ACTIVE"),
            new("NV014", "Trịnh Văn Hùng", "hung.tv@bank.vn", "SALES_OFFICER", "NV010", "PENDING_RESIGNATION", Note: "Chờ nghỉ – chuyển giao KH"),
            new("NV010", "Hoàng Thu Hà", "ha.ht@bank.vn", "DEPT_MANAGER", "NV020", "ACTIVE"),
            // row 9 - valid on its own; kept when row 21 repeats NV005.
            new("NV005", "Vũ Thị Em", "em.vt@bank.vn", "TELLER", "NV011", "ACTIVE", "PGD-BD", "Phòng Giao dịch Ba Đình"),
            new("NV009", "Đặng Thu Trang", "trang.dt@bank.vn", "TELLER", "NV011", "ACTIVE", "PGD-BD", "Phòng Giao dịch Ba Đình"),
            new("NV013", "Cao Minh Quân", "quan.cm@bank.vn", "TELLER", "NV011", "ACTIVE", "PGD-BD", "Phòng Giao dịch Ba Đình", "Nhân viên mới"),
            new("NV006", "Đỗ Quang Phúc", "phuc.dq@bank.vn", "SALES_OFFICER", "NV011", "TERMINATED", "PGD-BD", "Phòng Giao dịch Ba Đình", "Đã nghỉ – khóa TK (BR-52)"),
            new("NV012", "Phan Thanh Sơn", "son.pt@bank.vn", "SALES_OFFICER", "NV011", "ACTIVE", "PGD-BD", "Phòng Giao dịch Ba Đình"),
            new("NV011", "Bùi Lan Giang", "giang.bl@bank.vn", "DEPT_MANAGER", "NV020", "ACTIVE", "PGD-BD", "Phòng Giao dịch Ba Đình"),
            new("NV020", "Ngô Đức Khải", "khai.nd@bank.vn", "BRANCH_MANAGER", null, "ACTIVE", "BGD", "Ban Giám đốc"),
            new("NV016", "Tạ Thị Uyên", "uyen.tt@bank.vn", "QA", "NV020", "ACTIVE", "KSNB", "Phòng Kiểm soát nội bộ"),
            new("NV017", "Hồ Văn Vinh", "vinh.hv@bank.vn", "HR_OFFICER", "NV020", "ACTIVE", "HCNS", "Phòng Hành chính Nhân sự"),
            new("NV018", "Kiều Văn Xuân", "xuan.kv@bank.vn", "SYS_ADMIN", "NV020", "ACTIVE", "HCNS", "Phòng Hành chính Nhân sự"),
            // row 19 - invalid: bad email format.
            new("NV007", "Mai Thanh Long", "long.mt", "TELLER", "NV010", "ACTIVE", Note: "LỖI: email sai định dạng"),
            // row 20 - invalid: missing name/email, unknown position CREDIT_OFFICER.
            new("NV008", null, null, "CREDIT_OFFICER", "NV010", "ACTIVE", Note: "LỖI: thiếu họ tên/email; vị trí ngoài release 1.0; ngày sai"),
            // row 21 - invalid: duplicate employee code (same as row 9).
            new("NV005", "Vũ Thị Em B", "emb.vt@bank.vn", "TELLER", "NV011", "ACTIVE", "PGD-BD", "Phòng Giao dịch Ba Đình", "LỖI: trùng mã NV trong file"),
            // row 22 - invalid: manager code NV099 does not exist.
            new("NV019", "Lâm Gia Yến", "yen.lg@bank.vn", "TELLER", "NV099", "ACTIVE", "PGD-BD", "Phòng Giao dịch Ba Đình", "LỖI: mã quản lý không tồn tại"),
        };

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Nhân sự - Tổ chức");
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
            worksheet.Cell(excelRow, 6).Value = "HN01";
            worksheet.Cell(excelRow, 7).Value = r.EmployeeCode ?? "";
            worksheet.Cell(excelRow, 8).Value = r.FullName ?? "";
            worksheet.Cell(excelRow, 9).Value = r.Email ?? "";
            worksheet.Cell(excelRow, 10).Value = r.Position ?? "";
            worksheet.Cell(excelRow, 11).Value = r.ManagerCode ?? "";
            worksheet.Cell(excelRow, 12).Value = r.Status ?? "";
            worksheet.Cell(excelRow, 13).Value = r.Note ?? "";
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
