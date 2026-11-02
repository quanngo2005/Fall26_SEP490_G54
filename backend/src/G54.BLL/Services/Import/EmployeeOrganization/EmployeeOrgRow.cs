using G54.DAL.Entities;

namespace G54.BLL.Services.Import.EmployeeOrganization;

/// <summary>
/// One template row. The *Raw properties hold the trimmed cell text (template setters only trim); the
/// Norm/typed properties are filled in by <c>EmployeeOrgFieldFormatRule</c> once a cell's format is confirmed,
/// so later rules and <c>Classify</c>/<c>CommitRowsAsync</c> never re-parse text.
/// </summary>
public sealed class EmployeeOrgRow
{
    public string? BranchCodeRaw { get; set; }
    public string? BranchNameRaw { get; set; }
    public string? BranchParentCodeRaw { get; set; }
    public string? DeptCodeRaw { get; set; }
    public string? DeptNameRaw { get; set; }
    public string? DeptParentCodeRaw { get; set; }
    public string? EmployeeCodeRaw { get; set; }
    public string? FullNameRaw { get; set; }
    public string? EmailRaw { get; set; }
    public string? PositionRaw { get; set; }
    public string? ManagerCodeRaw { get; set; }
    public string? StatusRaw { get; set; }
    public string? HireDateRaw { get; set; }

    public string? BranchCode { get; set; }
    public string? DeptCode { get; set; }
    public string? DeptParentCode { get; set; }
    public string? EmployeeCode { get; set; }
    public string? Email { get; set; }
    public string? ManagerCode { get; set; }
    public string? Position { get; set; }
    public EmployeeStatus? Status { get; set; }
    public DateOnly? HireDate { get; set; }
}
