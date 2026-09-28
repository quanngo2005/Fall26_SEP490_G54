using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id).HasName("pk_users");
            entity.HasIndex(user => user.Email).IsUnique().HasDatabaseName("ix_users_email");
            entity.Property(user => user.Id).HasColumnName("id");
            entity.Property(user => user.Email).HasColumnName("email").HasMaxLength(320);
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(500);
            entity.Property(user => user.Role).HasColumnName("role").HasMaxLength(50);
            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
            entity.Property(user => user.UpdatedAt).HasColumnName("updated_at");
            entity.Property(user => user.IsDeleted).HasColumnName("is_deleted");
            entity.HasQueryFilter(user => !user.IsDeleted);
        });
    }
}
