using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class RolePermissionAssignment
{
    public Guid Id { get; set; }

    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual PermissionRecord Permission { get; set; } = null!;

    public virtual Role Role { get; set; } = null!;
}
