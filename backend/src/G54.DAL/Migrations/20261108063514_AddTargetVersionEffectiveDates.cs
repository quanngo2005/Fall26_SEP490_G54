using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddTargetVersionEffectiveDates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "changed_by_employee_id",
            table: "target_assignment",
            type: "uuid",
            nullable: true);

        // effective_from starts nullable so existing rows can be backfilled before the NOT NULL is enforced.
        migrationBuilder.AddColumn<DateOnly>(
            name: "effective_from",
            table: "target_assignment",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "effective_to",
            table: "target_assignment",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "previous_version_id",
            table: "target_assignment",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "hard_locked_through",
            table: "evaluation_period",
            type: "date",
            nullable: true);

        // Backfill: every existing version starts to count as effective from its evaluation period's start date.
        // Rows SUPERSEDED before BR-15 keep effective_to = NULL; that combination marks "replaced before BR-15"
        // and must never be read as an effective-dated segment (see FindActiveTargetAsync / LoadTargetsAsync).
        // Runs outside the migration's ambient transaction (suppressTransaction): PostgreSQL refuses a later
        // ALTER TABLE on target_assignment in the same transaction as this bulk UPDATE ("55006: pending trigger
        // events", from the row triggers this UPDATE fires), so the UPDATE must commit on its own first.
        migrationBuilder.Sql(
            "UPDATE target_assignment t SET effective_from = p.start_date " +
            "FROM evaluation_period p WHERE p.id = t.evaluation_period_id;",
            suppressTransaction: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "effective_from",
            table: "target_assignment",
            type: "date",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "date",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_target_assignment_changed_by_employee_id",
            table: "target_assignment",
            column: "changed_by_employee_id");

        migrationBuilder.CreateIndex(
            name: "ix_target_assignment_previous_version",
            table: "target_assignment",
            column: "previous_version_id");

        migrationBuilder.AddCheckConstraint(
            name: "ck_target_assignment_effective_range",
            table: "target_assignment",
            sql: "effective_to IS NULL OR effective_to >= effective_from");

        migrationBuilder.AddForeignKey(
            name: "fk_target_assignment_changed_by",
            table: "target_assignment",
            column: "changed_by_employee_id",
            principalTable: "employee",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_target_assignment_changed_by",
            table: "target_assignment");

        migrationBuilder.DropIndex(
            name: "IX_target_assignment_changed_by_employee_id",
            table: "target_assignment");

        migrationBuilder.DropIndex(
            name: "ix_target_assignment_previous_version",
            table: "target_assignment");

        migrationBuilder.DropCheckConstraint(
            name: "ck_target_assignment_effective_range",
            table: "target_assignment");

        migrationBuilder.DropColumn(
            name: "changed_by_employee_id",
            table: "target_assignment");

        migrationBuilder.DropColumn(
            name: "effective_from",
            table: "target_assignment");

        migrationBuilder.DropColumn(
            name: "effective_to",
            table: "target_assignment");

        migrationBuilder.DropColumn(
            name: "previous_version_id",
            table: "target_assignment");

        migrationBuilder.DropColumn(
            name: "hard_locked_through",
            table: "evaluation_period");
    }
}
