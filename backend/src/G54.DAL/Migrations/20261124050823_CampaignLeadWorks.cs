using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class CampaignLeadWorks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_campaign_customer_work_id",
            table: "campaign_customer");

        migrationBuilder.AddColumn<Guid>(
            name: "created_by",
            table: "business_campaign",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "uq_campaign_customer_work",
            table: "campaign_customer",
            column: "work_id",
            unique: true,
            filter: "work_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_business_campaign_created_by",
            table: "business_campaign",
            column: "created_by");

        migrationBuilder.AddForeignKey(
            name: "fk_business_campaign_created_by",
            table: "business_campaign",
            column: "created_by",
            principalTable: "employee",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        // notification.type is stored as varchar via a PgName converter (AppDbContext.Notifications.cs), not a
        // real PostgreSQL enum, so CAMPAIGN_END_REMINDER needs no ALTER TYPE here - the C# enum member is enough.

        // (a) Legacy campaigns may hold more than one campaign_work_type row even though a campaign now targets
        // exactly one product. Keep the one whose work type already has the most Works in that campaign; ties
        // go to the oldest campaign_work_type.updated_at, then the lowest id.
        migrationBuilder.Sql("""
            WITH work_counts AS (
                SELECT business_campaign_id, work_type_id, COUNT(*) AS cnt
                FROM work
                GROUP BY business_campaign_id, work_type_id
            ),
            ranked AS (
                SELECT cwt.id,
                       ROW_NUMBER() OVER (
                           PARTITION BY cwt.business_campaign_id
                           ORDER BY COALESCE(wc.cnt, 0) DESC, cwt.updated_at ASC, cwt.id ASC
                       ) AS rn
                FROM campaign_work_type cwt
                LEFT JOIN work_counts wc
                    ON wc.business_campaign_id = cwt.business_campaign_id AND wc.work_type_id = cwt.work_type_id
            )
            DELETE FROM campaign_work_type
            WHERE id IN (SELECT id FROM ranked WHERE rn > 1);
            """);

        // (b) Works created under the old close-time flow (MyCampaignService, customer already CLOSED) are left
        // untouched: they already carry campaign_customer.work_id and their own verification/KPI history.

        // (c) Every assigned, not-CLOSED customer without a Work gets one now, mirroring CampaignLeadWork.Build /
        // Description (G54.BLL, not referenced here since migrations cannot depend on BLL): NEW for
        // NOT_CONTACTED, IN_PROGRESS otherwise. Codes continue the day's TSK-<code>-<yyyyMMdd>- sequence per
        // campaign (same prefix scheme as CampaignWorkCode.PrefixFor). created_by is the assignee, since no
        // other actor performed this backfill. Campaigns with no remaining work type (never had one) are
        // skipped: there is no product to build the Work from.
        migrationBuilder.Sql("""
            WITH target_campaigns AS MATERIALIZED (
                SELECT bc.id AS campaign_id, bc.code AS campaign_code, bc.name AS campaign_name,
                       cwt.work_type_id, wt.name AS work_type_name
                FROM business_campaign bc
                JOIN campaign_work_type cwt ON cwt.business_campaign_id = bc.id
                JOIN work_type wt ON wt.id = cwt.work_type_id
            ),
            existing_counts AS MATERIALIZED (
                SELECT tc.campaign_id,
                       ('TSK-' || LEFT(tc.campaign_code, 32) || '-' || to_char(CURRENT_DATE, 'YYYYMMDD') || '-') AS code_prefix
                FROM target_campaigns tc
            ),
            existing_counts_with_total AS MATERIALIZED (
                SELECT ec.campaign_id, ec.code_prefix,
                       (SELECT COUNT(*) FROM work w WHERE w.code LIKE ec.code_prefix || '%') AS existing_count
                FROM existing_counts ec
            ),
            to_create AS MATERIALIZED (
                SELECT gen_random_uuid() AS new_id,
                       cc.id AS customer_id,
                       cc.business_campaign_id AS campaign_id,
                       cc.assignee_employee_id,
                       cc.customer_cif,
                       cc.customer_name,
                       cc.note,
                       ROW_NUMBER() OVER (PARTITION BY cc.business_campaign_id ORDER BY cc.id) AS rn,
                       CASE cc.contact_status WHEN 'NOT_CONTACTED' THEN 'NEW' ELSE 'IN_PROGRESS' END AS work_status_text
                FROM campaign_customer cc
                WHERE cc.work_id IS NULL
                  AND cc.assignee_employee_id IS NOT NULL
                  AND cc.contact_status <> 'CLOSED'
                  AND cc.business_campaign_id IN (SELECT campaign_id FROM target_campaigns)
            ),
            inserted AS (
                INSERT INTO work (
                    id, work_type_id, business_campaign_id, assignee_employee_id, created_by, code, title,
                    description, priority, status, planned_start_date, planned_end_date, updated_at, assigned_at,
                    customer_cif, customer_name)
                SELECT tcr.new_id, tcamp.work_type_id, tcamp.campaign_id, tcr.assignee_employee_id,
                       tcr.assignee_employee_id,
                       ec.code_prefix || LPAD((ec.existing_count + tcr.rn)::text, 4, '0'),
                       '[Chiến dịch ' || tcamp.campaign_code || '] ' || tcr.customer_name,
                       'Nguồn: chiến dịch ' || tcamp.campaign_code || ' - ' || tcamp.campaign_name
                           || '. Mã KH: ' || tcr.customer_cif || ', Tên KH: ' || tcr.customer_name
                           || '. Sản phẩm: ' || tcamp.work_type_name || '.'
                           || CASE WHEN tcr.note IS NOT NULL AND tcr.note <> '' THEN ' Ghi chú: ' || tcr.note ELSE '' END,
                       'MEDIUM'::work_priority, tcr.work_status_text::work_status, CURRENT_DATE, NULL, now(), now(),
                       tcr.customer_cif, tcr.customer_name
                FROM to_create tcr
                JOIN target_campaigns tcamp ON tcamp.campaign_id = tcr.campaign_id
                JOIN existing_counts_with_total ec ON ec.campaign_id = tcr.campaign_id
                RETURNING id
            )
            UPDATE campaign_customer cc
            SET work_id = tcr.new_id
            FROM to_create tcr
            WHERE cc.id = tcr.customer_id;
            """);

        // (d) Lead Works (campaign_customer.work_id) carry the campaign's end date as their deadline, not their
        // own planned_end_date; clear it unless the Work is already SUBMITTED or later (that history is kept).
        migrationBuilder.Sql("""
            UPDATE work w
            SET planned_end_date = NULL
            WHERE w.id IN (SELECT work_id FROM campaign_customer WHERE work_id IS NOT NULL)
              AND w.status NOT IN ('SUBMITTED', 'VERIFIED', 'REJECTED', 'DONE')
              AND w.planned_end_date IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data steps (a)/(c)/(d) above are not reverted: the dropped campaign_work_type rows, backfilled lead
        // Works and cleared planned_end_date values are not restored. notification.type needs no schema
        // rollback either (CAMPAIGN_END_REMINDER is just a varchar value, never a real pg enum label).
        migrationBuilder.DropForeignKey(
            name: "fk_business_campaign_created_by",
            table: "business_campaign");

        migrationBuilder.DropIndex(
            name: "uq_campaign_customer_work",
            table: "campaign_customer");

        migrationBuilder.DropIndex(
            name: "IX_business_campaign_created_by",
            table: "business_campaign");

        migrationBuilder.DropColumn(
            name: "created_by",
            table: "business_campaign");

        migrationBuilder.CreateIndex(
            name: "IX_campaign_customer_work_id",
            table: "campaign_customer",
            column: "work_id");
    }
}
