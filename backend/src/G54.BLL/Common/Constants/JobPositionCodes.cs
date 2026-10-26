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
}
