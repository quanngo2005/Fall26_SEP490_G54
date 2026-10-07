using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class PermissionRecord
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Resource { get; set; } = null!;

    public string Action { get; set; } = null!;

    public string? Description { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual ICollection<RolePermissionAssignment> RolePermissions { get; set; } = new List<RolePermissionAssignment>();
}
