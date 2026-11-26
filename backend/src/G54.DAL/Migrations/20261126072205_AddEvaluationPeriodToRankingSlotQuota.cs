using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationPeriodToRankingSlotQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_ranking_slot_quota_department_tier",
                table: "ranking_slot_quota");

            migrationBuilder.AddColumn<Guid>(
                name: "evaluation_period_id",
                table: "ranking_slot_quota",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_ranking_slot_quota_department_tier",
                table: "ranking_slot_quota",
                columns: DeptTierColumns,
                unique: true,
                filter: "evaluation_period_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_ranking_slot_quota_period_department_tier",
                table: "ranking_slot_quota",
                columns: PeriodDeptTierColumns,
                unique: true,
                filter: "evaluation_period_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_ranking_slot_quota_evaluation_period",
                table: "ranking_slot_quota",
                column: "evaluation_period_id",
                principalTable: "evaluation_period",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ranking_slot_quota_evaluation_period",
                table: "ranking_slot_quota");

            migrationBuilder.DropIndex(
                name: "uq_ranking_slot_quota_department_tier",
                table: "ranking_slot_quota");

            migrationBuilder.DropIndex(
                name: "uq_ranking_slot_quota_period_department_tier",
                table: "ranking_slot_quota");

            migrationBuilder.DropColumn(
                name: "evaluation_period_id",
                table: "ranking_slot_quota");

            migrationBuilder.CreateIndex(
                name: "uq_ranking_slot_quota_department_tier",
                table: "ranking_slot_quota",
                columns: DeptTierColumns,
                unique: true);
        }

        private static readonly string[] DeptTierColumns = ["department_id", "tier_code"];
        private static readonly string[] PeriodDeptTierColumns = ["evaluation_period_id", "department_id", "tier_code"];
    }
}
