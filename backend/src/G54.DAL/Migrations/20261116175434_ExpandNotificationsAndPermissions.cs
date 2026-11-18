using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Widens <c>notification.type</c> (40) and <c>notification.related_type</c> (30) for GB-914 automatic/exception
/// notification types, and grants <c>NOTIFICATION_READ</c> (<c>Own</c>) to <c>QA</c>, <c>VERIFIER</c> and <c>HR_OFFICER</c>
/// so every operational role can read its own inbox.
/// </summary>
public partial class ExpandNotificationsAndPermissions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "type",
            table: "notification",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(30)",
            oldMaxLength: 30);

        migrationBuilder.AlterColumn<string>(
            name: "related_type",
            table: "notification",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20,
            oldNullable: true);

        migrationBuilder.Sql("""
            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'Own'
            FROM role r
            JOIN permission p ON p.code = 'NOTIFICATION_READ'
            WHERE r.code IN ('QA', 'VERIFIER', 'HR_OFFICER')
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission
            WHERE permission_id IN (SELECT id FROM permission WHERE code = 'NOTIFICATION_READ')
              AND role_id IN (SELECT id FROM role WHERE code IN ('QA', 'VERIFIER', 'HR_OFFICER'));

            UPDATE notification SET type = left(type, 30) WHERE length(type) > 30;
            UPDATE notification SET related_type = left(related_type, 20) WHERE related_type IS NOT NULL AND length(related_type) > 20;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "type",
            table: "notification",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(40)",
            oldMaxLength: 40);

        migrationBuilder.AlterColumn<string>(
            name: "related_type",
            table: "notification",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(30)",
            oldMaxLength: 30,
            oldNullable: true);
    }
}
