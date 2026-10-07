using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class Department
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public Guid? ParentDepartmentId { get; set; }

    public DateTimeOffset LastSyncAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DepartmentLevelType LevelType { get; set; }
    public RecordStatus Status { get; set; }

    public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();

    public virtual ICollection<Department> InverseParentDepartment { get; set; } = new List<Department>();

    public virtual Department? ParentDepartment { get; set; }

    public virtual ICollection<TargetAssignment> TargetAssignments { get; set; } = new List<TargetAssignment>();
}
