using System.ComponentModel.DataAnnotations;
using G54.DAL.Entities;

namespace G54.BLL.Dtos.Employee;

public sealed class CreateEmployeeDto
{
    [Required(ErrorMessage = "Mã nhân viên là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Mã nhân viên không vượt quá 50 ký tự.")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
    [StringLength(200, ErrorMessage = "Họ và tên không vượt quá 200 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Định dạng email không hợp lệ.")]
    [StringLength(255, ErrorMessage = "Email không vượt quá 255 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [StringLength(30, ErrorMessage = "Số điện thoại không vượt quá 30 ký tự.")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Phòng ban (DepartmentId) là bắt buộc.")]
    public Guid DepartmentId { get; set; }

    [Required(ErrorMessage = "Vị trí công việc (JobPositionId) là bắt buộc.")]
    public Guid JobPositionId { get; set; }

    public Guid? ManagerId { get; set; }

    public EmployeeStatus? Status { get; set; } = EmployeeStatus.Active;
}
