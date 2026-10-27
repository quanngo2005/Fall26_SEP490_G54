using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddPerformanceEvaluationDisputePending : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "disputed_at",
            table: "performance_evaluation",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "pre_dispute_status",
            table: "performance_evaluation",
            type: "performance_evaluation_status",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_performance_evaluation_pre_dispute",
            table: "performance_evaluation",
            sql: "pre_dispute_status IS NULL OR status = 'DISPUTED'::performance_evaluation_status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_performance_evaluation_pre_dispute",
            table: "performance_evaluation");

        migrationBuilder.DropColumn(
            name: "disputed_at",
            table: "performance_evaluation");

        migrationBuilder.DropColumn(
            name: "pre_dispute_status",
            table: "performance_evaluation");
    }
}
