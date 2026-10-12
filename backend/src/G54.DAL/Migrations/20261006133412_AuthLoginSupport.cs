using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

public partial class AuthLoginSupport : Migration
{
    private static readonly string[] AuditIndexColumns = ["actor_id", "occurred_at"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "failed_login_attempts",
            table: "account",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "locked_until",
            table: "account",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "auth_audit_log",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_auth_audit_log", row => row.id);
                table.ForeignKey(
                    name: "FK_auth_audit_log_account_actor_id",
                    column: row => row.actor_id,
                    principalTable: "account",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_auth_audit_log_actor_id_occurred_at",
            table: "auth_audit_log",
            columns: AuditIndexColumns);

        migrationBuilder.CreateIndex(
            name: "ix_role_permission_permission",
            table: "role_permission",
            column: "permission_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_role_role",
            table: "user_role",
            column: "role_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_role_permission_permission", table: "role_permission");
        migrationBuilder.DropIndex(name: "ix_user_role_role", table: "user_role");
        migrationBuilder.DropTable(name: "auth_audit_log");
        migrationBuilder.DropColumn(name: "failed_login_attempts", table: "account");
        migrationBuilder.DropColumn(name: "locked_until", table: "account");
    }
}
