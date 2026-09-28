using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace G54.DAL.Migrations;

[DbContext(typeof(AppDbContext))]
partial class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.20");
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            entity.Property<string>("Email").IsRequired().HasMaxLength(320).HasColumnType("character varying(320)").HasColumnName("email");
            entity.Property<bool>("IsDeleted").HasColumnType("boolean").HasColumnName("is_deleted");
            entity.Property<string>("PasswordHash").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("password_hash");
            entity.Property<string>("Role").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)").HasColumnName("role");
            entity.Property<DateTimeOffset?>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            entity.HasKey("Id").HasName("pk_users");
            entity.HasIndex("Email").IsUnique().HasDatabaseName("ix_users_email");
            entity.ToTable("users");
            entity.HasQueryFilter(user => !user.IsDeleted);
        });
    }
}
