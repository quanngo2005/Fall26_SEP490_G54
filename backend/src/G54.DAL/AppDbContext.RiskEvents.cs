using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public partial class AppDbContext
{
    /// <summary>The columns the Risk Events screens (SCR-RSK-01) add: display number, penalty, linked work, recording time and reversal.</summary>
    private static void ConfigureRiskEventScreens(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("risk_event_code_seq");

        modelBuilder.Entity<RiskEvent>(entity =>
        {
            entity.ToTable("risk_event", table =>
            {
                table.HasCheckConstraint("ck_risk_event_penalty_points", "penalty_points IS NULL OR (penalty_points > 0 AND penalty_points <= 100)");
                table.HasCheckConstraint("ck_risk_event_reversal", "(reversed_at IS NULL) = (reversed_by IS NULL)");
            });
            entity.Property(e => e.CodeNo).HasColumnName("code_no").HasDefaultValueSql("nextval('risk_event_code_seq')").ValueGeneratedOnAdd();
            entity.Property(e => e.PenaltyPoints).HasPrecision(8, 2).HasColumnName("penalty_points");
            entity.Property(e => e.WorkId).HasColumnName("work_id");
            entity.Property(e => e.RecordedAt).HasColumnName("recorded_at").HasDefaultValueSql("now()");
            entity.Property(e => e.ReversedAt).HasColumnName("reversed_at");
            entity.Property(e => e.ReversedBy).HasColumnName("reversed_by");
            entity.Property(e => e.ReversalReason).HasMaxLength(2000).HasColumnName("reversal_reason");
            entity.HasIndex(e => e.CodeNo).IsUnique().HasDatabaseName("uq_risk_event_code_no");
            entity.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_risk_event_occurred_at");
            entity.HasOne(e => e.Work).WithMany().HasForeignKey(e => e.WorkId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_risk_event_work");
            entity.HasOne(e => e.ReversedByEmployee).WithMany().HasForeignKey(e => e.ReversedBy)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_risk_event_reversed_by");
        });
    }
}
