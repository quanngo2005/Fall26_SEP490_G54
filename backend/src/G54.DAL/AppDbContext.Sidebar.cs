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
    }
}
