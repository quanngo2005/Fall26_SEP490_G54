using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public partial class AppDbContext
{
    public DbSet<WorkChecklistItem> WorkChecklistItems => Set<WorkChecklistItem>();

    public DbSet<WorkActivity> WorkActivities => Set<WorkActivity>();

    private static void ConfigureWorkDetail(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkChecklistItem>(entity =>
        {
            entity.ToTable("work_checklist_item");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()").HasColumnName("id");
            entity.Property(e => e.WorkId).HasColumnName("work_id");
            entity.Property(e => e.Title).HasMaxLength(200).HasColumnName("title");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsDone).HasColumnName("is_done");
            entity.Property(e => e.DoneAt).HasColumnName("done_at");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").HasColumnName("created_at");
            entity.HasIndex(e => new { e.WorkId, e.SortOrder }).HasDatabaseName("ix_work_checklist_item_work");
            entity.HasOne(e => e.Work).WithMany(w => w.ChecklistItems)
                .HasForeignKey(e => e.WorkId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_work_checklist_item_work");
        });

        modelBuilder.Entity<WorkActivity>(entity =>
        {
            entity.ToTable("work_activity");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()").HasColumnName("id");
            entity.Property(e => e.WorkId).HasColumnName("work_id");
            entity.Property(e => e.ActorEmployeeId).HasColumnName("actor_employee_id");
            entity.Property(e => e.Action).HasMaxLength(30).HasColumnName("action");
            entity.Property(e => e.Message).HasMaxLength(1500).HasColumnName("message");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").HasColumnName("created_at");
            entity.HasIndex(e => new { e.WorkId, e.CreatedAt }).HasDatabaseName("ix_work_activity_work");
            entity.HasOne(e => e.Work).WithMany(w => w.Activities)
                .HasForeignKey(e => e.WorkId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_work_activity_work");
            entity.HasOne(e => e.ActorEmployee).WithMany()
                .HasForeignKey(e => e.ActorEmployeeId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_work_activity_actor");
        });
    }
}
