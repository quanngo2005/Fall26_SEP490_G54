-- The dev seed (seed_dev_data.sql) authors HDV and DU_NO in tỷ VND (1 = one billion); every other monetary KPI is stored in full VND.
-- Run by DatabaseSeeder after the seed transaction commits (ALTER TABLE cannot run while that transaction still has
-- pending trigger events). Same statements as migration 20261125090000_ConvertTyVndKpisToVnd, keyed on the unit: a no-op
-- once converted, so seeding again is safe.

ALTER TABLE target_assignment DISABLE TRIGGER USER;
ALTER TABLE actual_progress DISABLE TRIGGER USER;
ALTER TABLE performance_contribution DISABLE TRIGGER USER;
ALTER TABLE work_result_item DISABLE TRIGGER USER;

UPDATE target_assignment
SET target_value = target_value * 1000000000
WHERE kpi_definition_id IN (SELECT id FROM kpi_definition WHERE code IN ('HDV', 'DU_NO') AND unit = 'tỷ VND');

UPDATE target_change_log
SET old_value = old_value * 1000000000, new_value = new_value * 1000000000
WHERE kpi_definition_id IN (SELECT id FROM kpi_definition WHERE code IN ('HDV', 'DU_NO') AND unit = 'tỷ VND');

UPDATE performance_contribution
SET target_value_snapshot = target_value_snapshot * 1000000000,
    actual_value = actual_value * 1000000000,
    weighted_actual_value = weighted_actual_value * 1000000000
WHERE actual_progress_id IN (
    SELECT ap.id FROM actual_progress ap
    JOIN target_assignment ta ON ta.id = ap.target_assignment_id
    JOIN kpi_definition k ON k.id = ta.kpi_definition_id
    WHERE k.code IN ('HDV', 'DU_NO') AND k.unit = 'tỷ VND');

UPDATE work_result_item
SET value = value * 1000000000, raw_value = raw_value * 1000000000
WHERE actual_progress_id IN (
    SELECT ap.id FROM actual_progress ap
    JOIN target_assignment ta ON ta.id = ap.target_assignment_id
    JOIN kpi_definition k ON k.id = ta.kpi_definition_id
    WHERE k.code IN ('HDV', 'DU_NO') AND k.unit = 'tỷ VND');

UPDATE actual_progress
SET value = value * 1000000000
WHERE target_assignment_id IN (
    SELECT ta.id FROM target_assignment ta
    JOIN kpi_definition k ON k.id = ta.kpi_definition_id
    WHERE k.code IN ('HDV', 'DU_NO') AND k.unit = 'tỷ VND');

UPDATE kpi_definition
SET unit = 'VND', baseline_value = baseline_value * 1000000000
WHERE code IN ('HDV', 'DU_NO') AND unit = 'tỷ VND';

ALTER TABLE target_assignment ENABLE TRIGGER USER;
ALTER TABLE actual_progress ENABLE TRIGGER USER;
ALTER TABLE performance_contribution ENABLE TRIGGER USER;
ALTER TABLE work_result_item ENABLE TRIGGER USER;
