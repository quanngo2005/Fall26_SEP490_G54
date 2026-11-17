using ClosedXML.Excel;

namespace G54.UnitTests.Import;

/// <summary>
/// A synthetic workbook with the same layout as the business sample <c>02_Nhan_su_To_chuc.xlsx</c> (13 columns, 21 data
/// rows: 17 valid, 4 invalid on rows 19-22) but made-up names, e-mails and units: the real sample is confidential and is
/// never copied into the repository. Invalid rows: 19 bad e-mail, 20 missing name/e-mail and unknown position,
/// 21 employee code repeated from row 9, 22 unknown manager.
/// </summary>
internal static class EmployeeOrgSampleWorkbook
{
    private static readonly string[] Headers =
    [
        "Mã chi nhánh*", "Tên chi nhánh*", "Mã CN cha", "Mã phòng ban*", "Tên phòng ban*", "Đơn vị cha",
        "Mã NV*", "Họ tên*", "Email*", "Vị trí*", "Mã quản lý trực tiếp", "Trạng thái*", "Ghi chú (demo)",
    ];

    public const string BranchCode = "CN-T01";

    private sealed record Row(
        string? EmployeeCode, string? FullName, string? Email, string? Position,
        string? ManagerCode, string? Status,
        string DeptCode = "PGD-T1", string DeptName = "Phòng Giao dịch T1",
        string? Note = null);

    /// <summary>Row numbers (1-based, matching the Excel row) that the workbook intentionally makes invalid.</summary>
    public static readonly int[] InvalidRowNumbers = [19, 20, 21, 22];

    public static readonly string[] DepartmentCodes = ["PGD-T1", "PGD-T2", "BGD-T", "KSNB-T", "HCNS-T"];

    public static string NameOf(string employeeCode) => $"Nhân viên {employeeCode[2..]}";

    public static string EmailOf(string employeeCode) => $"{employeeCode.ToLowerInvariant()}@example.test";

    private static Row Valid(string code, string position, string? manager, string status = "ACTIVE",
        string dept = "PGD-T1", string deptName = "Phòng Giao dịch T1", string? note = null) =>
        new(code, NameOf(code), EmailOf(code), position, manager, status, dept, deptName, note);

    public static MemoryStream Build()
    {
        const string T2 = "PGD-T2", T2Name = "Phòng Giao dịch T2";
        var rows = new List<Row>
        {
            Valid("NV001", "TELLER", "NV010"),
            Valid("NV002", "TELLER", "NV010"),
            Valid("NV015", "TELLER", "NV010", "ON_LEAVE", note: "Nghỉ phép"),
            Valid("NV003", "SALES_OFFICER", "NV010"),
            Valid("NV004", "SALES_OFFICER", "NV010"),
            Valid("NV014", "SALES_OFFICER", "NV010", "PENDING_RESIGNATION", note: "Chờ nghỉ"),
            Valid("NV010", "DEPT_MANAGER", "NV020"),
            // row 9 - valid on its own; kept when row 21 repeats NV005.
            Valid("NV005", "TELLER", "NV011", dept: T2, deptName: T2Name),
            Valid("NV009", "TELLER", "NV011", dept: T2, deptName: T2Name),
            Valid("NV013", "TELLER", "NV011", dept: T2, deptName: T2Name),
            Valid("NV006", "SALES_OFFICER", "NV011", "TERMINATED", T2, T2Name, "Đã nghỉ"),
            Valid("NV012", "SALES_OFFICER", "NV011", dept: T2, deptName: T2Name),
            Valid("NV011", "DEPT_MANAGER", "NV020", dept: T2, deptName: T2Name),
            Valid("NV020", "BRANCH_MANAGER", null, dept: "BGD-T", deptName: "Ban Giám đốc T"),
            Valid("NV016", "QA", "NV020", dept: "KSNB-T", deptName: "Phòng Kiểm soát T"),
            Valid("NV017", "HR_OFFICER", "NV020", dept: "HCNS-T", deptName: "Phòng Nhân sự T"),
            Valid("NV018", "SYS_ADMIN", "NV020", dept: "HCNS-T", deptName: "Phòng Nhân sự T"),
            // row 19 - invalid: bad email format.
            new("NV007", NameOf("NV007"), "nv007.example.test", "TELLER", "NV010", "ACTIVE", Note: "LỖI: email sai định dạng"),
            // row 20 - invalid: missing name/email, unknown position CREDIT_OFFICER.
            new("NV008", null, null, "CREDIT_OFFICER", "NV010", "ACTIVE", Note: "LỖI: thiếu họ tên/email; vị trí không hợp lệ"),
            // row 21 - invalid: duplicate employee code (same as row 9).
            new("NV005", NameOf("NV005") + " B", "nv005b@example.test", "TELLER", "NV011", "ACTIVE", T2, T2Name, "LỖI: trùng mã NV trong file"),
            // row 22 - invalid: manager code NV099 does not exist.
            new("NV019", NameOf("NV019"), EmailOf("NV019"), "TELLER", "NV099", "ACTIVE", T2, T2Name, "LỖI: mã quản lý không tồn tại"),
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
            worksheet.Cell(excelRow, 1).Value = BranchCode;
            worksheet.Cell(excelRow, 2).Value = "Chi nhánh Thử nghiệm";
            worksheet.Cell(excelRow, 3).Value = "";
            worksheet.Cell(excelRow, 4).Value = r.DeptCode;
            worksheet.Cell(excelRow, 5).Value = r.DeptName;
            worksheet.Cell(excelRow, 6).Value = BranchCode;
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
