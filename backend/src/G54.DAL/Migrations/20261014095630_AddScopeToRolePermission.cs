using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Migration name matches entity table.")]
public partial class AddScopeToRolePermission : Migration
{
    /// <inheritdoc />
    /// <remarks>
    /// Idempotent: the embedded ERD script used by <c>InitialProgestSchema</c> already creates
    /// <c>role_permission.scope</c> on newly created databases, while databases created before
    /// that script changed do not have it yet.
    /// </remarks>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE role_permission ADD COLUMN IF NOT EXISTS scope character varying(50) NOT NULL DEFAULT 'Own';");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE role_permission DROP COLUMN IF EXISTS scope;");
    }
}
