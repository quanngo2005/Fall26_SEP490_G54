using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Databases that applied the withdrawn GB-326 migration (20261027065734_AddSystemParameters, file removed
            // in 540fdb3) still hold its tables with an incompatible schema. They are unused, so drop them first.
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS system_parameter_changes;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = current_schema() AND table_name = 'system_parameters' AND column_name = 'numeric_value') THEN
                        DROP TABLE system_parameters;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateTable(
                name: "system_parameters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("system_parameters_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_system_parameters_key",
                table: "system_parameters",
                column: "key",
                unique: true);

            migrationBuilder.CreateTable(
                name: "evaluation_comparison_setting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    baseline = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    lookback_periods = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("evaluation_comparison_setting_pkey", x => x.id);
                    table.CheckConstraint("ck_evaluation_comparison_setting_baseline", "baseline IN ('PREVIOUS_PERIOD', 'SAME_PERIOD_LAST_YEAR', 'AVERAGE_LAST_N')");
                    table.CheckConstraint("ck_evaluation_comparison_setting_lookback", "lookback_periods BETWEEN 2 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "tier_criterion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    metric = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    threshold = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("tier_criterion_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tier_criterion_requirement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_criterion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("tier_criterion_requirement_pkey", x => x.id);
                    table.ForeignKey(
                        name: "fk_tier_criterion_requirement_criterion",
                        column: x => x.tier_criterion_id,
                        principalTable: "tier_criterion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_tier_criterion_code",
                table: "tier_criterion",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_tier_criterion_requirement",
                table: "tier_criterion_requirement",
                columns: ["tier_criterion_id", "tier_code"],
                unique: true);

            // SCR-ADM-04: view/update permissions for the administrator roles, and the default comparison baseline.
            migrationBuilder.Sql("""
                INSERT INTO permission (id, code, name, resource, action) VALUES
                    (gen_random_uuid(), 'SYSTEM_PARAMETER_VIEW', 'Xem tham số hệ thống', 'system_parameter', 'view'),
                    (gen_random_uuid(), 'SYSTEM_PARAMETER_UPDATE', 'Cập nhật tham số hệ thống', 'system_parameter', 'update')
                ON CONFLICT (code) DO UPDATE
                SET name = EXCLUDED.name, resource = EXCLUDED.resource, action = EXCLUDED.action;

                INSERT INTO role_permission (role_id, permission_id, scope)
                SELECT r.id, p.id, 'All'
                FROM role r
                JOIN permission p ON p.code IN ('SYSTEM_PARAMETER_VIEW', 'SYSTEM_PARAMETER_UPDATE')
                WHERE r.code IN ('ADMIN', 'SYSTEM_ADMIN')
                ON CONFLICT (role_id, permission_id) DO UPDATE
                SET scope = EXCLUDED.scope;

                INSERT INTO evaluation_comparison_setting (id, baseline, lookback_periods, created_at, is_deleted)
                VALUES (gen_random_uuid(), 'PREVIOUS_PERIOD', 3, now(), false);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code IN ('SYSTEM_PARAMETER_VIEW', 'SYSTEM_PARAMETER_UPDATE'));
                DELETE FROM permission WHERE code IN ('SYSTEM_PARAMETER_VIEW', 'SYSTEM_PARAMETER_UPDATE');
                """);

            migrationBuilder.DropTable(
                name: "system_parameters");

            migrationBuilder.DropTable(
                name: "evaluation_comparison_setting");

            migrationBuilder.DropTable(
                name: "tier_criterion_requirement");

            migrationBuilder.DropTable(
                name: "tier_criterion");
        }
    }
}
