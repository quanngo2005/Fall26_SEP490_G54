using Microsoft.EntityFrameworkCore;

namespace G54.DAL.Authentication;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<AuthAuditLog> AuthAuditLogs => Set<AuthAuditLog>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<RolePermissionAssignment> RolePermissions => Set<RolePermissionAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum<EmployeeStatus>("public", "employee_status")
            .HasPostgresEnum<AuthRecordStatus>("public", "record_status");

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(employee => employee.Id).HasName("employee_pkey");
            entity.ToTable("employee");
            entity.HasIndex(employee => employee.EmployeeCode, "uq_employee_code").IsUnique();
            entity.HasIndex(employee => employee.Email, "uq_employee_email").IsUnique();
            entity.Property(employee => employee.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(employee => employee.EmployeeCode)
                .HasMaxLength(50)
                .HasColumnName("employee_code");
            entity.Property(employee => employee.FullName)
                .HasMaxLength(200)
                .HasColumnName("full_name");
            entity.Property(employee => employee.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(employee => employee.Status)
                .HasColumnType("employee_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(account => account.Id).HasName("account_pkey");
            entity.ToTable("account");
            entity.HasIndex(account => account.EmployeeId, "uq_account_employee").IsUnique();
            entity.HasIndex(account => account.Username, "uq_account_username").IsUnique();
            entity.Property(account => account.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(account => account.EmployeeId).HasColumnName("employee_id");
            entity.Property(account => account.Username)
                .HasMaxLength(100)
                .HasColumnName("username");
            entity.Property(account => account.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(account => account.IsLocked)
                .HasDefaultValue(false)
                .HasColumnName("is_locked");
            entity.Property(account => account.LastSyncAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("last_sync_at");
            entity.Property(account => account.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(account => account.FailedLoginAttempts)
                .HasDefaultValue(0)
                .HasColumnName("failed_login_attempts");
            entity.Property(account => account.LockedUntil).HasColumnName("locked_until");
            entity.HasOne(account => account.Employee)
                .WithOne(employee => employee.Account)
                .HasForeignKey<Account>(account => account.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_account_employee");
        });

        modelBuilder.Entity<AuthAuditLog>(entity =>
        {
            entity.HasKey(audit => audit.Id).HasName("PK_auth_audit_log");
            entity.ToTable("auth_audit_log");
            entity.HasIndex(audit => new { audit.ActorId, audit.OccurredAt })
                .HasDatabaseName("IX_auth_audit_log_actor_id_occurred_at");
            entity.Property(audit => audit.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(audit => audit.ActorId).HasColumnName("actor_id");
            entity.Property(audit => audit.Action)
                .HasMaxLength(50)
                .HasColumnName("action");
            entity.Property(audit => audit.OccurredAt).HasColumnName("occurred_at");
            entity.Property(audit => audit.IpAddress)
                .HasMaxLength(64)
                .HasColumnName("ip_address");
            entity.HasOne(audit => audit.Actor)
                .WithMany(account => account.AuthAuditLogs)
                .HasForeignKey(audit => audit.ActorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(role => role.Id).HasName("role_pkey");
            entity.ToTable("role");
            entity.HasIndex(role => role.Code, "uq_role_code").IsUnique();
            entity.Property(role => role.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(role => role.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(role => role.Status)
                .HasColumnType("record_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<PermissionRecord>(entity =>
        {
            entity.HasKey(permission => permission.Id).HasName("permission_pkey");
            entity.ToTable("permission");
            entity.HasIndex(permission => permission.Code, "uq_permission_code").IsUnique();
            entity.Property(permission => permission.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(permission => permission.Code)
                .HasMaxLength(100)
                .HasColumnName("code");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(userRole => userRole.Id).HasName("user_role_pkey");
            entity.ToTable("user_role");
            entity.HasIndex(userRole => userRole.RoleId, "ix_user_role_role");
            entity.HasIndex(userRole => new { userRole.AccountId, userRole.RoleId }, "uq_user_role").IsUnique();
            entity.Property(userRole => userRole.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(userRole => userRole.AccountId).HasColumnName("account_id");
            entity.Property(userRole => userRole.RoleId).HasColumnName("role_id");
            entity.HasOne(userRole => userRole.Account)
                .WithMany(account => account.UserRoles)
                .HasForeignKey(userRole => userRole.AccountId)
                .HasConstraintName("fk_user_role_account");
            entity.HasOne(userRole => userRole.Role)
                .WithMany(role => role.UserRoles)
                .HasForeignKey(userRole => userRole.RoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_user_role_role");
        });

        modelBuilder.Entity<RolePermissionAssignment>(entity =>
        {
            entity.HasKey(rolePermission => rolePermission.Id).HasName("role_permission_pkey");
            entity.ToTable("role_permission");
            entity.HasIndex(rolePermission => rolePermission.PermissionId, "ix_role_permission_permission");
            entity.HasIndex(
                    rolePermission => new { rolePermission.RoleId, rolePermission.PermissionId },
                    "uq_role_permission")
                .IsUnique();
            entity.Property(rolePermission => rolePermission.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(rolePermission => rolePermission.PermissionId).HasColumnName("permission_id");
            entity.Property(rolePermission => rolePermission.RoleId).HasColumnName("role_id");
            entity.HasOne(rolePermission => rolePermission.Permission)
                .WithMany(permission => permission.RolePermissions)
                .HasForeignKey(rolePermission => rolePermission.PermissionId)
                .HasConstraintName("fk_role_permission_permission");
            entity.HasOne(rolePermission => rolePermission.Role)
                .WithMany(role => role.RolePermissions)
                .HasForeignKey(rolePermission => rolePermission.RoleId)
                .HasConstraintName("fk_role_permission_role");
        });
    }
}
