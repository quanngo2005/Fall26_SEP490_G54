namespace G54.BLL.Common.Constants;

/// <summary>
/// Stable <c>job_position.code</c> values the department views use to break headcount down.
/// Matching is by code only; a position title is a display label and must not drive logic.
/// </summary>
public static class JobPositionCodes
{
    /// <summary>Giao dịch viên (counter staff).</summary>
    public const string Teller = "TELLER";

    /// <summary>Chuyên viên Kinh doanh (sales staff).</summary>
    public const string Sales = "SALES";

    /// <summary>Trưởng phòng (department manager).</summary>
    public const string DeptManager = "DEPT_MANAGER";

    /// <summary>Giám đốc chi nhánh (branch manager).</summary>
    public const string BranchManager = "BRANCH_MANAGER";

    /// <summary>Kiểm soát chất lượng (QA / verifier).</summary>
    public const string Qa = "QA";

    /// <summary>Chuyên viên nhân sự (HR officer).</summary>
    public const string HrOfficer = "HR_OFFICER";

    /// <summary>Quản trị hệ thống (system admin).</summary>
    public const string SysAdmin = "SYS_ADMIN";
}
