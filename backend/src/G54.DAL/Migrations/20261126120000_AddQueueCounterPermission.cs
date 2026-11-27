using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace G54.DAL.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126120000_AddQueueCounterPermission")]
public sealed class AddQueueCounterAccess : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO permission (id, code, name, resource, action)
        VALUES (gen_random_uuid(), 'QUEUE_COUNTER_OPERATE', 'Điều hành quầy cá nhân', 'queue_counter', 'operate')
        ON CONFLICT DO NOTHING;
        INSERT INTO role_permission (role_id, permission_id, scope)
        SELECT r.id, p.id, 'Own' FROM role r CROSS JOIN permission p
        WHERE r.code = 'EMPLOYEE' AND p.code = 'QUEUE_COUNTER_OPERATE' ON CONFLICT DO NOTHING;
        INSERT INTO permission (id,code,name,resource,action) VALUES (gen_random_uuid(),'QUEUE_COUNTER_SUPERVISE','Điều phối sảnh','queue_counter','supervise') ON CONFLICT DO NOTHING;
        INSERT INTO role_permission(role_id,permission_id,scope) SELECT r.id,p.id,m.scope
        FROM (VALUES ('DEPT_MANAGER','Dept'),('BRANCH_MANAGER','Branch')) m(code,scope)
        JOIN role r ON r.code=m.code JOIN permission p ON p.code='QUEUE_COUNTER_SUPERVISE' ON CONFLICT DO NOTHING;
        UPDATE sidebar_configurations SET item_ids=array_append(item_ids,'queueLobby'), version=version+1
        WHERE role IN ('departmentManager','branchManager') AND NOT ('queueLobby'=ANY(item_ids));
        UPDATE sidebar_configurations SET item_ids = array_append(item_ids, 'queueCounter'), version = version + 1
        WHERE role = 'employee' AND NOT ('queueCounter' = ANY(item_ids));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE sidebar_configurations SET item_ids = array_remove(item_ids, 'queueCounter'),
            routes = routes - 'queueCounter', version = version + 1 WHERE 'queueCounter' = ANY(item_ids);
        UPDATE sidebar_configurations SET item_ids=array_remove(item_ids,'queueLobby'),routes=routes-'queueLobby',version=version+1 WHERE 'queueLobby'=ANY(item_ids);
        DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code IN ('QUEUE_COUNTER_OPERATE','QUEUE_COUNTER_SUPERVISE'));
        DELETE FROM permission WHERE code IN ('QUEUE_COUNTER_OPERATE','QUEUE_COUNTER_SUPERVISE');
        """);
}
