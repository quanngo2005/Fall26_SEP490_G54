using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public partial class AppDbContext
{
    /// <summary>The catalog columns of a KPI added for the KPI list: credit role, data source, product, applicable positions and validity.</summary>
    private static void ConfigureKpiCatalog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KpiDefinition>(entity =>
        {
            entity.Property(e => e.CreditRole)
                .HasConversion(PgNameConverter<KpiCreditRole>())
                .HasMaxLength(20)
                .HasDefaultValue(KpiCreditRole.Processor)
                .HasColumnName("credit_role");
            entity.Property(e => e.DataSource)
                .HasConversion(PgNameConverter<KpiDataSource>())
                .HasMaxLength(20)
                .HasDefaultValue(KpiDataSource.Transaction)
                .HasColumnName("data_source");
            entity.Property(e => e.ProductName).HasMaxLength(100).HasColumnName("product_name");
            entity.Property(e => e.BaselineValue).HasPrecision(18, 4).HasColumnName("baseline_value");
            entity.Property(e => e.EffectiveFromPeriodId).HasColumnName("effective_from_period_id");
            entity.Property(e => e.EffectiveToPeriodId).HasColumnName("effective_to_period_id");
            entity.Property(e => e.SubmittedAt).HasColumnName("submitted_at");
            entity.HasIndex(e => e.ProductName).HasDatabaseName("ix_kpi_definition_product");

            entity.HasOne(e => e.EffectiveFromPeriod).WithMany()
                .HasForeignKey(e => e.EffectiveFromPeriodId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_kpi_definition_effective_from");
            entity.HasOne(e => e.EffectiveToPeriod).WithMany()
                .HasForeignKey(e => e.EffectiveToPeriodId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_kpi_definition_effective_to");

            entity.HasMany(e => e.JobPositions).WithMany(p => p.KpiDefinitions)
                .UsingEntity<Dictionary<string, object>>(
                    "kpi_definition_job_position",
                    right => right.HasOne<JobPosition>().WithMany().HasForeignKey("job_position_id").OnDelete(DeleteBehavior.Cascade),
                    left => left.HasOne<KpiDefinition>().WithMany().HasForeignKey("kpi_definition_id").OnDelete(DeleteBehavior.Cascade),
                    join => join.HasKey("kpi_definition_id", "job_position_id"));
        });
    }
}
