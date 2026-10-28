using System.Reflection;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NpgsqlTypes;

namespace G54.DAL;

public partial class AppDbContext
{
    public DbSet<ScoreEntry> ScoreEntries => Set<ScoreEntry>();
    public DbSet<AppealScoreSetting> AppealScoreSettings => Set<AppealScoreSetting>();

    /// <summary>Maps an enum to its <c>[PgName]</c> text, so the stored values stay the UPPER_SNAKE strings the check constraints use.</summary>
    private static ValueConverter<TEnum, string> PgNameConverter<TEnum>()
        where TEnum : struct, Enum
    {
        var names = Enum.GetValues<TEnum>().ToDictionary(
            value => value,
            value => typeof(TEnum).GetField(value.ToString())!.GetCustomAttribute<PgNameAttribute>()!.PgName);
        var values = names.ToDictionary(pair => pair.Value, pair => pair.Key);
        return new ValueConverter<TEnum, string>(value => names[value], text => values[text]);
    }

    private static void ConfigureScores(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScoreEntry>(entity =>
        {
            entity.ToTable("score_entry", table =>
            {
                table.HasCheckConstraint("ck_score_entry_kind", "(kind IN ('HOLD', 'TASK_DEDUCTION') AND points < 0 AND reverses_entry_id IS NULL) OR (kind = 'REFUND' AND points > 0 AND reverses_entry_id IS NOT NULL)");
                table.HasCheckConstraint("ck_score_entry_origin", "(kind = 'TASK_DEDUCTION' AND work_id IS NOT NULL AND appeal_id IS NULL) OR (kind IN ('HOLD', 'REFUND') AND appeal_id IS NOT NULL)");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EvaluationId).HasColumnName("evaluation_id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.AppealId).HasColumnName("appeal_id");
            entity.Property(e => e.WorkId).HasColumnName("work_id");
            entity.Property(e => e.ReversesEntryId).HasColumnName("reverses_entry_id");
            entity.Property(e => e.RecordedByEmployeeId).HasColumnName("recorded_by_employee_id");
            entity.Property(e => e.Points).HasPrecision(14, 4).HasColumnName("points");
            entity.Property(e => e.Kind).HasConversion(PgNameConverter<ScoreEntryKind>()).HasMaxLength(20).HasColumnName("kind");
            entity.Property(e => e.Source).HasConversion(PgNameConverter<ScoreEntrySource>()).HasMaxLength(20).HasColumnName("source");
            entity.Property(e => e.Reason).HasMaxLength(2000).HasColumnName("reason");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(e => new { e.AppealId, e.Kind }).IsUnique().HasFilter("appeal_id IS NOT NULL");
            entity.HasIndex(e => e.ReversesEntryId).IsUnique();
            entity.HasIndex(e => new { e.EmployeeId, e.Kind });
            entity.HasOne(e => e.Evaluation).WithMany().HasForeignKey(e => e.EvaluationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Employee>().WithMany().HasForeignKey(e => e.RecordedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Appeal>().WithMany().HasForeignKey(e => e.AppealId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Work>().WithMany().HasForeignKey(e => e.WorkId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ScoreEntry>().WithMany().HasForeignKey(e => e.ReversesEntryId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<AppealScoreSetting>(entity =>
        {
            entity.ToTable("appeal_score_setting", table =>
            {
                table.HasCheckConstraint("ck_appeal_score_setting_singleton", "id = 1");
                table.HasCheckConstraint("ck_appeal_score_setting_points", "default_points > 0 AND default_points <= 1000");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.DefaultPoints).HasPrecision(14, 4).HasColumnName("default_points");
            entity.HasData(new AppealScoreSetting { Id = 1, DefaultPoints = 5m });
        });
        modelBuilder.Entity<Appeal>().Property(a => a.ScoreDisputeReason).HasMaxLength(2000).HasColumnName("score_dispute_reason");
    }
}
