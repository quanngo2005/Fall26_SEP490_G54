using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class RenameRankingVetoToDownrank : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Rename in place so the stored configuration is kept.
        migrationBuilder.RenameTable(
            name: "ranking_veto_setting",
            newName: "ranking_downrank_setting");

        migrationBuilder.Sql(
            "ALTER TABLE ranking_downrank_setting RENAME CONSTRAINT ranking_veto_setting_pkey TO ranking_downrank_setting_pkey;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE ranking_downrank_setting RENAME CONSTRAINT ranking_downrank_setting_pkey TO ranking_veto_setting_pkey;");

        migrationBuilder.RenameTable(
            name: "ranking_downrank_setting",
            newName: "ranking_veto_setting");
    }
}
