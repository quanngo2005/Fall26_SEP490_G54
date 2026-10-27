using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public partial class AppDbContext
{
    public DbSet<SidebarConfiguration> SidebarConfigurations => Set<SidebarConfiguration>();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Implements the instance partial model hook declared by AppDbContext.")]
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SidebarConfiguration>(entity =>
        {
            entity.ToTable("sidebar_configurations");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Role).IsUnique();
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Role).HasMaxLength(32).HasColumnName("role");
            entity.Property(e => e.ItemIds).HasColumnName("item_ids").HasColumnType("text[]");
            entity.Property(e => e.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(e => e.RoutesJson).HasColumnName("routes").HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
        });

        modelBuilder.Entity<SystemParameter>(entity =>
        {
            entity.ToTable("system_parameters");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Key).HasMaxLength(64).HasColumnName("key");
            entity.Property(e => e.NumericValue).HasColumnName("numeric_value");
            entity.Property(e => e.TextValue).HasMaxLength(64).HasColumnName("text_value");
            entity.Property(e => e.MinValue).HasColumnName("min_value");
            entity.Property(e => e.MaxValue).HasColumnName("max_value");
            entity.Property(e => e.IsLocked).HasColumnName("is_locked");
            entity.Property(e => e.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
        });

        modelBuilder.Entity<SystemParameterChange>(entity =>
        {
            entity.ToTable("system_parameter_changes");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Key, e.ChangedAt });
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Key).HasMaxLength(64).HasColumnName("key");
            entity.Property(e => e.OldValue).HasMaxLength(64).HasColumnName("old_value");
            entity.Property(e => e.NewValue).HasMaxLength(64).HasColumnName("new_value");
            entity.Property(e => e.ChangedBy).HasColumnName("changed_by");
            entity.Property(e => e.ChangedAt).HasColumnName("changed_at");
        });
    }
}
