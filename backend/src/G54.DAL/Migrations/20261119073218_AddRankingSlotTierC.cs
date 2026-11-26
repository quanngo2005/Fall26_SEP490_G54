using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddRankingSlotTierC : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_ranking_slot_quota_tier_code",
            table: "ranking_slot_quota");

        migrationBuilder.AddCheckConstraint(
            name: "ck_ranking_slot_quota_tier_code",
            table: "ranking_slot_quota",
            sql: "tier_code IN ('A1', 'A2', 'A3', 'B', 'C')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM ranking_slot_quota WHERE tier_code = 'C';");

        migrationBuilder.DropCheckConstraint(
            name: "ck_ranking_slot_quota_tier_code",
            table: "ranking_slot_quota");

        migrationBuilder.AddCheckConstraint(
            name: "ck_ranking_slot_quota_tier_code",
            table: "ranking_slot_quota",
            sql: "tier_code IN ('A1', 'A2', 'A3', 'B')");
    }
}
