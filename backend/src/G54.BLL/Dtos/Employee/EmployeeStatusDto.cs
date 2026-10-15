using System.ComponentModel.DataAnnotations;
using G54.DAL.Entities;

namespace G54.BLL.Dtos.Employee;

public sealed class EmployeeStatusDto
{
    [Required(ErrorMessage = "Trạng thái nhân viên là bắt buộc.")]
    public EmployeeStatus Status { get; set; }
}
