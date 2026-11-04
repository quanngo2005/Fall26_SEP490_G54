using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public partial class AppDbContext
{
    public DbSet<Notification> Notifications => Set<Notification>();

    private static void ConfigureNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notification", table =>
            {
                table.HasCheckConstraint("ck_notification_related", "(related_type IS NULL) = (related_id IS NULL)");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RecipientEmployeeId).HasColumnName("recipient_employee_id");
            entity.Property(e => e.SenderEmployeeId).HasColumnName("sender_employee_id");
            entity.Property(e => e.Type).HasConversion(PgNameConverter<NotificationType>()).HasMaxLength(30).HasColumnName("type");
            entity.Property(e => e.Severity).HasConversion(PgNameConverter<NotificationSeverity>()).HasMaxLength(20).HasColumnName("severity");
            entity.Property(e => e.Title).HasMaxLength(200).HasColumnName("title");
            entity.Property(e => e.Body).HasMaxLength(2000).HasColumnName("body");
            entity.Property(e => e.RelatedType).HasConversion(PgNameConverter<NotificationRelatedType>()).HasMaxLength(20).HasColumnName("related_type");
            entity.Property(e => e.RelatedId).HasColumnName("related_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ReadAt).HasColumnName("read_at");
            entity.HasIndex(e => new { e.RecipientEmployeeId, e.CreatedAt });
            entity.HasIndex(e => e.RecipientEmployeeId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notification_unread");
            entity.HasOne<Employee>().WithMany().HasForeignKey(e => e.RecipientEmployeeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Sender).WithMany().HasForeignKey(e => e.SenderEmployeeId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
