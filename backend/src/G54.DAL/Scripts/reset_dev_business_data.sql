-- ============================================================================
-- Progest - DEV ONLY: wipe the sample business data so seed_dev_data.sql can rebuild it.
-- Run by hand against a development database (never production):
--   psql -U postgres -d bpms_db -f reset_dev_business_data.sql
-- Removes targets (+ feedback), progress, work / submissions / verifications, evaluations, appeals,
-- risk events and audit logs. Keeps organization, employees, accounts, roles, permissions, KPI catalog,
-- evaluation profiles, evaluation periods, campaigns and sidebar settings.
-- ============================================================================
BEGIN;

TRUNCATE TABLE
    score_entry,
    appeal,
    performance_evaluation_history,
    performance_contribution,
    performance_evaluation,
    risk_event,
    verified_work_result,
    verification,
    work_result_item,
    result_submission,
    work,
    actual_progress,
    target_assignment_feedback,
    target_assignment,
    audit_logs
CASCADE;

COMMIT;
