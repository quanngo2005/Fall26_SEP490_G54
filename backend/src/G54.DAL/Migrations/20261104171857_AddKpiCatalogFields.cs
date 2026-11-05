using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddKpiCatalogFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "baseline_value",
            table: "kpi_definition",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "credit_role",
            table: "kpi_definition",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "PROCESSOR");

        migrationBuilder.AddColumn<string>(
            name: "data_source",
            table: "kpi_definition",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "TRANSACTION");

        migrationBuilder.AddColumn<Guid>(
            name: "effective_from_period_id",
            table: "kpi_definition",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "effective_to_period_id",
            table: "kpi_definition",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "product_name",
            table: "kpi_definition",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "submitted_at",
            table: "kpi_definition",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "kpi_definition_job_position",
            columns: table => new
            {
                kpi_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                job_position_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_kpi_definition_job_position", x => new { x.kpi_definition_id, x.job_position_id });
                table.ForeignKey(
                    name: "FK_kpi_definition_job_position_job_position_job_position_id",
                    column: x => x.job_position_id,
                    principalTable: "job_position",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_kpi_definition_job_position_kpi_definition_kpi_definition_id",
                    column: x => x.kpi_definition_id,
                    principalTable: "kpi_definition",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_kpi_definition_effective_from_period_id",
            table: "kpi_definition",
            column: "effective_from_period_id");

        migrationBuilder.CreateIndex(
            name: "IX_kpi_definition_effective_to_period_id",
            table: "kpi_definition",
            column: "effective_to_period_id");

        migrationBuilder.CreateIndex(
            name: "ix_kpi_definition_product",
            table: "kpi_definition",
            column: "product_name");

        migrationBuilder.CreateIndex(
            name: "IX_kpi_definition_job_position_job_position_id",
            table: "kpi_definition_job_position",
            column: "job_position_id");

        migrationBuilder.AddForeignKey(
            name: "fk_kpi_definition_effective_from",
            table: "kpi_definition",
            column: "effective_from_period_id",
            principalTable: "evaluation_period",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_kpi_definition_effective_to",
            table: "kpi_definition",
            column: "effective_to_period_id",
            principalTable: "evaluation_period",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_kpi_definition_effective_from",
            table: "kpi_definition");

        migrationBuilder.DropForeignKey(
            name: "fk_kpi_definition_effective_to",
            table: "kpi_definition");

        migrationBuilder.DropTable(
            name: "kpi_definition_job_position");

        migrationBuilder.DropIndex(
            name: "IX_kpi_definition_effective_from_period_id",
            table: "kpi_definition");

        migrationBuilder.DropIndex(
            name: "IX_kpi_definition_effective_to_period_id",
            table: "kpi_definition");

        migrationBuilder.DropIndex(
            name: "ix_kpi_definition_product",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "baseline_value",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "credit_role",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "data_source",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "effective_from_period_id",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "effective_to_period_id",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "product_name",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "submitted_at",
            table: "kpi_definition");
    }
}
