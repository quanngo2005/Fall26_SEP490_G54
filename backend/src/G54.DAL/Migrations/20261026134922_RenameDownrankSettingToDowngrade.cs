using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class RenameDownrankSettingToDowngrade : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Rename in place so the stored configuration is kept.
        migrationBuilder.RenameTable(
            name: "ranking_downrank_setting",
            newName: "downgrade");

        migrationBuilder.Sql(
            "ALTER TABLE downgrade RENAME CONSTRAINT ranking_downrank_setting_pkey TO downgrade_pkey;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE downgrade RENAME CONSTRAINT downgrade_pkey TO ranking_downrank_setting_pkey;");

        migrationBuilder.RenameTable(
            name: "downgrade",
            newName: "ranking_downrank_setting");
    }
}
