using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class UserRole
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid RoleId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual Role Role { get; set; } = null!;
}
