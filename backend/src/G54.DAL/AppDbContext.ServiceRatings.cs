using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public partial class AppDbContext
{
    public DbSet<ServiceRating> ServiceRatings => Set<ServiceRating>();

    private static void ConfigureServiceRatings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServiceRating>(entity =>
        {
            entity.ToTable("service_rating", table => table.HasCheckConstraint("ck_service_rating_score", "score BETWEEN 1 AND 5"));
            entity.HasKey(e => e.Id).HasName("service_rating_pkey");
            entity.HasIndex(e => e.TransactionCode, "uq_service_rating_transaction_code").IsUnique();
            entity.HasIndex(e => e.ImportBatchId, "ix_service_rating_batch");
            entity.HasIndex(e => e.EmployeeId, "ix_service_rating_employee");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ImportBatchId).HasColumnName("import_batch_id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.TransactionCode).HasMaxLength(100).HasColumnName("transaction_code");
            entity.Property(e => e.RatingDate).HasColumnName("rating_date");
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.CustomerComment).HasMaxLength(1000).HasColumnName("customer_comment");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasOne(e => e.ImportBatch).WithMany(b => b.ServiceRatings).HasForeignKey(e => e.ImportBatchId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_service_rating_batch");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_service_rating_employee");
        });
    }
}
