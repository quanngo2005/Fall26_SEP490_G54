using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddPeriodTypeWeek : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TYPE period_type ADD VALUE IF NOT EXISTS 'WEEK' BEFORE 'DAY';");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Postgres cannot drop enum values. To remove WEEK, drop and recreate the type.
    }
}
