using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddDataImportSupport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Raw idempotent SQL instead of an AlterDatabase enum diff: PostgreSQL cannot drop an enum label, so a
        // generated ADD VALUE fails with "already exists" when a database replays Up after Down.
        migrationBuilder.Sql("ALTER TYPE employee_status ADD VALUE IF NOT EXISTS 'PENDING_RESIGNATION';");

        migrationBuilder.AddColumn<DateOnly>(
            name: "hire_date",
            table: "employee",
            type: "date",
            nullable: true);

        // Seed data for the Employee/Organization data import (docs/api/DATA_IMPORT_CONTRACT.md):
        // the 5 missing job positions, the HR_OFFICER role, and the DATA_IMPORT permission granted to
        // ADMIN and SYSTEM_ADMIN. Idempotent, same pattern as SeedJobPositionManageAccess / AddAuditLogs seed.
        migrationBuilder.Sql("""
            INSERT INTO job_position (id, code, title, description)
            VALUES
                (gen_random_uuid(), 'DEPT_MANAGER',   'Trưởng phòng',          'Quản lý phòng ban'),
                (gen_random_uuid(), 'BRANCH_MANAGER', 'Giám đốc chi nhánh',    'Quản lý chi nhánh'),
                (gen_random_uuid(), 'QA',              'Kiểm soát chất lượng', 'Kiểm soát chất lượng dịch vụ'),
                (gen_random_uuid(), 'HR_OFFICER',      'Chuyên viên nhân sự',  'Quản lý nhân sự'),
                (gen_random_uuid(), 'SYS_ADMIN',       'Quản trị hệ thống',    'Quản trị hệ thống')
            ON CONFLICT (code) DO UPDATE
            SET title = EXCLUDED.title, description = EXCLUDED.description;

            INSERT INTO role (id, code, name, description)
            VALUES (gen_random_uuid(), 'HR_OFFICER', 'Chuyên viên nhân sự', 'Quản lý hồ sơ nhân sự và tổ chức')
            ON CONFLICT (code) DO UPDATE
            SET name = EXCLUDED.name, description = EXCLUDED.description;

            INSERT INTO permission (id, code, name, resource, action)
            VALUES (gen_random_uuid(), 'DATA_IMPORT', 'Import dữ liệu', 'data_import', 'import')
            ON CONFLICT (code) DO UPDATE
            SET name = EXCLUDED.name, resource = EXCLUDED.resource, action = EXCLUDED.action;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'All'
            FROM role r
            JOIN permission p ON p.code = 'DATA_IMPORT'
            WHERE r.code IN ('ADMIN', 'SYSTEM_ADMIN')
            ON CONFLICT (role_id, permission_id) DO UPDATE
            SET scope = EXCLUDED.scope;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code = 'DATA_IMPORT');
            DELETE FROM permission WHERE code = 'DATA_IMPORT';
            DELETE FROM role WHERE code = 'HR_OFFICER';
            DELETE FROM job_position WHERE code IN ('DEPT_MANAGER', 'BRANCH_MANAGER', 'QA', 'HR_OFFICER', 'SYS_ADMIN');
            """);

        migrationBuilder.DropColumn(
            name: "hire_date",
            table: "employee");

        // employee_status keeps PENDING_RESIGNATION: PostgreSQL cannot drop an enum label.
    }
}
