using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddRankingConfiguration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ranking_tier_threshold",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                tier_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                rank_order = table.Column<int>(type: "integer", nullable: false),
                min_score = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("ranking_tier_threshold_pkey", x => x.id);
                table.CheckConstraint("ck_ranking_tier_threshold_min_score", "min_score >= 0 AND min_score <= 100");
                table.CheckConstraint("ck_ranking_tier_threshold_rank_order", "rank_order >= 1");
            });

        migrationBuilder.CreateTable(
            name: "ranking_veto_setting",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                trigger_severity = table.Column<int>(type: "risk_event_severity", nullable: false),
                cap_tier_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("ranking_veto_setting_pkey", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "uq_ranking_tier_threshold_code",
            table: "ranking_tier_threshold",
            column: "tier_code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "uq_ranking_tier_threshold_order",
            table: "ranking_tier_threshold",
            column: "rank_order",
            unique: true);

        // Default configuration (idempotent). Administrators change these rows afterwards.
        migrationBuilder.Sql(
            """
            INSERT INTO ranking_tier_threshold (tier_code, rank_order, min_score) VALUES
                ('A1', 1, 95),
                ('A2', 2, 90),
                ('A3', 3, 85),
                ('B', 4, 70),
                ('C', 5, 0)
            ON CONFLICT DO NOTHING;

            INSERT INTO ranking_veto_setting (trigger_severity, cap_tier_code)
            SELECT 'CRITICAL'::risk_event_severity, 'B'
            WHERE NOT EXISTS (SELECT 1 FROM ranking_veto_setting);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ranking_tier_threshold");

        migrationBuilder.DropTable(
            name: "ranking_veto_setting");
    }
}
