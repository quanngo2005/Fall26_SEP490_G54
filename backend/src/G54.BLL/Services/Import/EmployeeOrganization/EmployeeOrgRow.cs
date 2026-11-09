using G54.BLL.Common.Import;
using G54.DAL.Entities;

namespace G54.BLL.Services.Import.EmployeeOrganization;

/// <summary>One template row. Codes are upper-cased and the email lower-cased when the cell is mapped.</summary>
public sealed class EmployeeOrgRow
{
    public int RowNumber { get; set; }
    public string? BranchCode { get; set; }
    public string? BranchName { get; set; }
    public string? DeptCode { get; set; }
    public string? DeptName { get; set; }
    public string? DeptParentCode { get; set; }
    public string? EmployeeCode { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Position { get; set; }
    public string? ManagerCode { get; set; }
    public string? StatusText { get; set; }

    public EmployeeStatus Status { get; set; }
    public bool IsUpdate { get; set; }
    public List<ImportError> Errors { get; } = [];

    public void AddError(string column, string code, string message) => Errors.Add(new ImportError(column, code, message));
}
