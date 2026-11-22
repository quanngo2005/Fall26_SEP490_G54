using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class RemoveEmployeeCheckEvaluationStatuses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TYPE performance_evaluation_status RENAME TO performance_evaluation_status_old;");

        migrationBuilder.Sql("""
            CREATE TYPE performance_evaluation_status AS ENUM (
                'DRAFT', 'DISPUTED', 'CALCULATED', 'SUBMITTED', 'FINAL');
            """);

        migrationBuilder.Sql("ALTER TABLE performance_evaluation ALTER COLUMN status DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE performance_evaluation DROP CONSTRAINT ck_performance_evaluation_pre_dispute;");

        migrationBuilder.Sql("""
            ALTER TABLE performance_evaluation
                ALTER COLUMN status TYPE performance_evaluation_status
                USING (CASE status::text
                    WHEN 'EMPLOYEE_CHECK' THEN 'DRAFT'
                    WHEN 'EMPLOYEE_CONFIRMED' THEN 'CALCULATED'
                    ELSE status::text END)::performance_evaluation_status,
                ALTER COLUMN pre_dispute_status TYPE performance_evaluation_status
                USING (CASE pre_dispute_status::text
                    WHEN 'EMPLOYEE_CHECK' THEN 'DRAFT'
                    WHEN 'EMPLOYEE_CONFIRMED' THEN 'CALCULATED'
                    ELSE pre_dispute_status::text END)::performance_evaluation_status;
            """);

        migrationBuilder.Sql("ALTER TABLE performance_evaluation ALTER COLUMN status SET DEFAULT 'DRAFT'::performance_evaluation_status;");
        migrationBuilder.Sql("ALTER TABLE performance_evaluation ADD CONSTRAINT ck_performance_evaluation_pre_dispute CHECK (pre_dispute_status IS NULL OR status = 'DISPUTED'::performance_evaluation_status);");

        migrationBuilder.Sql("""
            ALTER TABLE performance_evaluation_history
                ALTER COLUMN from_status TYPE performance_evaluation_status
                USING (CASE from_status::text
                    WHEN 'EMPLOYEE_CHECK' THEN 'DRAFT'
                    WHEN 'EMPLOYEE_CONFIRMED' THEN 'CALCULATED'
                    ELSE from_status::text END)::performance_evaluation_status,
                ALTER COLUMN to_status TYPE performance_evaluation_status
                USING (CASE to_status::text
                    WHEN 'EMPLOYEE_CHECK' THEN 'DRAFT'
                    WHEN 'EMPLOYEE_CONFIRMED' THEN 'CALCULATED'
                    ELSE to_status::text END)::performance_evaluation_status;
            """);

        migrationBuilder.Sql("DROP TYPE performance_evaluation_status_old;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TYPE performance_evaluation_status RENAME TO performance_evaluation_status_new;");

        migrationBuilder.Sql("""
            CREATE TYPE performance_evaluation_status AS ENUM (
                'DRAFT', 'EMPLOYEE_CHECK', 'DISPUTED', 'EMPLOYEE_CONFIRMED', 'CALCULATED', 'SUBMITTED', 'FINAL');
            """);

        migrationBuilder.Sql("ALTER TABLE performance_evaluation ALTER COLUMN status DROP DEFAULT;");
        migrationBuilder.Sql("ALTER TABLE performance_evaluation DROP CONSTRAINT ck_performance_evaluation_pre_dispute;");

        migrationBuilder.Sql("""
            ALTER TABLE performance_evaluation
                ALTER COLUMN status TYPE performance_evaluation_status
                USING status::text::performance_evaluation_status,
                ALTER COLUMN pre_dispute_status TYPE performance_evaluation_status
                USING pre_dispute_status::text::performance_evaluation_status;
            """);

        migrationBuilder.Sql("ALTER TABLE performance_evaluation ALTER COLUMN status SET DEFAULT 'DRAFT'::performance_evaluation_status;");
        migrationBuilder.Sql("ALTER TABLE performance_evaluation ADD CONSTRAINT ck_performance_evaluation_pre_dispute CHECK (pre_dispute_status IS NULL OR status = 'DISPUTED'::performance_evaluation_status);");

        migrationBuilder.Sql("""
            ALTER TABLE performance_evaluation_history
                ALTER COLUMN from_status TYPE performance_evaluation_status
                USING from_status::text::performance_evaluation_status,
                ALTER COLUMN to_status TYPE performance_evaluation_status
                USING to_status::text::performance_evaluation_status;
            """);

        migrationBuilder.Sql("DROP TYPE performance_evaluation_status_new;");
    }
}
