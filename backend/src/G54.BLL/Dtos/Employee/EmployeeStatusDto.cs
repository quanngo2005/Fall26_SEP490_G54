using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Employee;

public sealed class EmployeeStatusDto
{
    /// <summary>UPPER_SNAKE status: ACTIVE, INACTIVE, ON_LEAVE, TERMINATED.</summary>
    [Required(ErrorMessage = "Trạng thái nhân viên là bắt buộc.")]
    public string Status { get; set; } = string.Empty;
}
