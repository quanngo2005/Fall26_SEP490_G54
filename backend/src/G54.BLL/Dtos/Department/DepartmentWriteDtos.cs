using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Department;

public sealed class CreateDepartmentDto
{
    /// <summary>Unique, URL-safe code (letters, digits, <c>-</c>, <c>_</c>); stored upper-case.</summary>
    [Required(ErrorMessage = "Mã phòng ban là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Mã phòng ban không vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên phòng ban là bắt buộc.")]
    [StringLength(200, ErrorMessage = "Tên phòng ban không vượt quá 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Mô tả không vượt quá 1000 ký tự.")]
    public string? Description { get; set; }

    /// <summary>UPPER_SNAKE level: BRANCH, DEPARTMENT, TEAM.</summary>
    [Required(ErrorMessage = "Cấp phòng ban là bắt buộc.")]
    public string LevelType { get; set; } = string.Empty;

    /// <summary>Required for DEPARTMENT and TEAM, must be omitted for BRANCH.</summary>
    public Guid? ParentDepartmentId { get; set; }
}

/// <summary>Same shape as create; the status is changed through its own endpoint.</summary>
public sealed class UpdateDepartmentDto
{
    [Required(ErrorMessage = "Mã phòng ban là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Mã phòng ban không vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên phòng ban là bắt buộc.")]
    [StringLength(200, ErrorMessage = "Tên phòng ban không vượt quá 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Mô tả không vượt quá 1000 ký tự.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Cấp phòng ban là bắt buộc.")]
    public string LevelType { get; set; } = string.Empty;

    public Guid? ParentDepartmentId { get; set; }
}

public sealed class DepartmentStatusDto
{
    /// <summary>UPPER_SNAKE status: ACTIVE, INACTIVE, ARCHIVED.</summary>
    [Required(ErrorMessage = "Trạng thái phòng ban là bắt buộc.")]
    public string Status { get; set; } = string.Empty;
}
