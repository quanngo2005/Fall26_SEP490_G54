using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddNotifications : Migration
{
    private static readonly string[] InboxColumns = ["recipient_employee_id", "created_at"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                recipient_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                sender_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                related_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                related_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification", x => x.id);
                table.CheckConstraint("ck_notification_related", "(related_type IS NULL) = (related_id IS NULL)");
                table.ForeignKey(
                    name: "FK_notification_employee_recipient_employee_id",
                    column: x => x.recipient_employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_notification_employee_sender_employee_id",
                    column: x => x.sender_employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_notification_recipient_employee_id_created_at",
            table: "notification",
            columns: InboxColumns);

        migrationBuilder.CreateIndex(
            name: "IX_notification_sender_employee_id",
            table: "notification",
            column: "sender_employee_id");

        migrationBuilder.CreateIndex(
            name: "ix_notification_unread",
            table: "notification",
            column: "recipient_employee_id",
            filter: "read_at IS NULL");

        // Every role reads its own notifications; managers and administrators send announcements within their scope.
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action)
            VALUES ('NOTIFICATION_READ', 'Xem thông báo của mình', 'notification', 'read'),
                   ('NOTIFICATION_SEND', 'Gửi thông báo cho nhân viên', 'notification', 'send')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, m.scope
            FROM (VALUES
                ('ADMIN', 'NOTIFICATION_READ', 'All'), ('MANAGER', 'NOTIFICATION_READ', 'Own'), ('EMPLOYEE', 'NOTIFICATION_READ', 'Own'),
                ('SYSTEM_ADMIN', 'NOTIFICATION_READ', 'All'), ('BRANCH_MANAGER', 'NOTIFICATION_READ', 'Own'), ('DEPT_MANAGER', 'NOTIFICATION_READ', 'Own'),
                ('ADMIN', 'NOTIFICATION_SEND', 'All'), ('SYSTEM_ADMIN', 'NOTIFICATION_SEND', 'All'),
                ('BRANCH_MANAGER', 'NOTIFICATION_SEND', 'Branch'), ('DEPT_MANAGER', 'NOTIFICATION_SEND', 'Dept'), ('MANAGER', 'NOTIFICATION_SEND', 'Dept')
            ) AS m(role_code, permission_code, scope)
            JOIN role r ON r.code = m.role_code
            JOIN permission p ON p.code = m.permission_code
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code IN ('NOTIFICATION_READ', 'NOTIFICATION_SEND'));
            DELETE FROM permission WHERE code IN ('NOTIFICATION_READ', 'NOTIFICATION_SEND');
            """);

        migrationBuilder.DropTable(
            name: "notification");
    }
}
