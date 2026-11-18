using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddRankingSlotQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ranking_slot_quota",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    headcount = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ranking_slot_quota_pkey", x => x.id);
                    table.CheckConstraint("ck_ranking_slot_quota_headcount", "headcount >= 0");
                    table.CheckConstraint("ck_ranking_slot_quota_tier_code", "tier_code IN ('A1', 'A2', 'A3', 'B')");
                    table.ForeignKey(
                        name: "fk_ranking_slot_quota_department",
                        column: x => x.department_id,
                        principalTable: "department",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_ranking_slot_quota_department_tier",
                table: "ranking_slot_quota",
                columns: IndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ranking_slot_quota");
        }

        private static readonly string[] IndexColumns = ["department_id", "tier_code"];
    }
}
