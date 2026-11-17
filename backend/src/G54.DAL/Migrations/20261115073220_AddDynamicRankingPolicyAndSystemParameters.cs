using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddDynamicRankingPolicyAndSystemParameters : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ranking_policy_snapshot_json",
            table: "evaluation_period",
            type: "jsonb",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ranking_policy",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                evaluation_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                policy_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("ranking_policy_pkey", x => x.id);
                table.CheckConstraint("ck_ranking_policy_version", "version >= 1");
                table.ForeignKey(
                    name: "fk_ranking_policy_period",
                    column: x => x.evaluation_period_id,
                    principalTable: "evaluation_period",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "uq_ranking_policy_code",
            table: "ranking_policy",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "uq_ranking_policy_period",
            table: "ranking_policy",
            column: "evaluation_period_id",
            unique: true);

        migrationBuilder.Sql("""
            INSERT INTO ranking_policy (id, code, name, description, version, is_active, evaluation_period_id, policy_json, created_at, is_deleted)
            VALUES (
                '5eed0000-0000-0000-0099-000000000001',
                'DEFAULT_POLICY',
                'Chính sách gợi ý xếp hạng chuẩn Chi nhánh',
                'Áp dụng công thức điểm xét hạng, hạ trần khi vi phạm rủi ro nghiêm trọng và kiểm soát hạn ngạch nhóm Hạng A.',
                1,
                true,
                NULL,
                '{
                  "scoreFormula": "TotalScore",
                  "tiers": [
                    { "tierCode": "A1", "rankOrder": 1, "minScore": 95 },
                    { "tierCode": "A2", "rankOrder": 2, "minScore": 90 },
                    { "tierCode": "A3", "rankOrder": 3, "minScore": 85 },
                    { "tierCode": "B", "rankOrder": 4, "minScore": 70 },
                    { "tierCode": "C", "rankOrder": 5, "minScore": 0 }
                  ],
                  "tieBreakers": [
                    "EFFECTIVE_SCORE_DESC",
                    "BUSINESS_SCORE_DESC",
                    "SERVICE_QUALITY_SCORE_DESC",
                    "EMPLOYEE_CODE_ASC"
                  ],
                  "rules": [
                    {
                      "id": "rule-critical-risk-cap",
                      "name": "Hạ trần tối đa hạng B khi có sự kiện rủi ro Nghiêm trọng",
                      "enabled": true,
                      "stopOnMatch": false,
                      "conditions": {
                        "logicalOperator": "AND",
                        "items": [
                          { "metric": "MAX_RISK_SEVERITY", "operator": "GTE", "value": 4, "secondaryValue": null, "values": null }
                        ],
                        "groups": []
                      },
                      "actions": [
                        { "type": "CAP_MAX_TIER", "tierCode": "B", "numericValue": null, "flagValue": null, "message": "Hạ trần tối đa hạng B do phát sinh sự kiện rủi ro mức CRITICAL" }
                      ]
                    },
                    {
                      "id": "rule-low-tier-coaching",
                      "name": "Tự động gắn cờ Kèm cặp (Coaching) cho hạng C trở xuống",
                      "enabled": true,
                      "stopOnMatch": false,
                      "conditions": {
                        "logicalOperator": "AND",
                        "items": [
                          { "metric": "CURRENT_TIER", "operator": "IN", "value": null, "secondaryValue": null, "values": ["C", "D"] }
                        ],
                        "groups": []
                      },
                      "actions": [
                        { "type": "SET_COACHING_FLAG", "tierCode": null, "numericValue": null, "flagValue": true, "message": "Đề xuất kèm cặp do xếp hạng từ C trở xuống" }
                      ]
                    }
                  ]
                }'::jsonb,
                now(),
                false
            )
            ON CONFLICT (code) DO NOTHING;

            UPDATE sidebar_configurations
            SET item_ids = array_cat(
                item_ids,
                ARRAY(
                    SELECT x FROM unnest(ARRAY['systemParameters', 'rankingStudio']) AS x
                    WHERE NOT (x = ANY(item_ids))
                )
            )
            WHERE role = 'admin'
              AND (NOT ('systemParameters' = ANY(item_ids)) OR NOT ('rankingStudio' = ANY(item_ids)));
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE sidebar_configurations
            SET item_ids = array_remove(array_remove(item_ids, 'systemParameters'), 'rankingStudio'),
                routes = (routes - 'systemParameters') - 'rankingStudio'
            WHERE 'systemParameters' = ANY(item_ids)
               OR 'rankingStudio' = ANY(item_ids)
               OR jsonb_exists(routes, 'systemParameters')
               OR jsonb_exists(routes, 'rankingStudio');
            """);

        migrationBuilder.DropTable(
            name: "ranking_policy");

        migrationBuilder.DropColumn(
            name: "ranking_policy_snapshot_json",
            table: "evaluation_period");
    }
}
