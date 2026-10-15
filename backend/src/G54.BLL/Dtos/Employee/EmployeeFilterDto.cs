using G54.DAL.Entities;

namespace G54.BLL.Dtos.Employee;

public sealed class EmployeeFilterDto
{
    public string? Keyword { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? JobPositionId { get; set; }
    public Guid? ManagerId { get; set; }
    public EmployeeStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
