import {
  EvaluationProfileItem,
  EvaluationProfileItemInput,
} from '../../core/evaluation-profile.models';

/**
 * One line of the SCR-CFG-01-M1 table: a KPI with its weight in the whole profile. The API stores a tree
 * (group weight, then KPI weight relative to the group); the form shows the product of both.
 */
export interface ProfileRow {
  group: string;
  kpiDefinitionId: string;
  /** Weight in the whole profile, percent. */
  weight: number | null;
  /** Read-only KPI caption from the stored tree; the form picks the KPI by id instead. */
  kpiLabel?: string;
}

export interface FlattenedProfile {
  rows: ProfileRow[];
  /** False when the stored tree has a shape the two-level form cannot rebuild (nested groups, KPI at the root). */
  supported: boolean;
}

const round4 = (value: number): number => Math.round(value * 10000) / 10000;

export function flattenItems(items: readonly EvaluationProfileItem[]): FlattenedProfile {
  const rows: ProfileRow[] = [];
  let supported = true;
  for (const root of items) {
    if (root.name === null) {
      supported = false;
      continue;
    }
    if (root.children.length === 0) {
      rows.push({ group: root.name, kpiDefinitionId: '', weight: root.weightPercent });
      continue;
    }
    for (const child of root.children) {
      if (child.kpiDefinitionId === null) {
        supported = false;
        continue;
      }
      rows.push({
        group: root.name,
        kpiDefinitionId: child.kpiDefinitionId,
        weight: round4((root.weightPercent * child.weightPercent) / 100),
        kpiLabel: child.kpiName ?? child.kpiCode ?? '',
      });
    }
  }
  return { rows, supported };
}

const groupKey = (name: string): string => name.trim().toLowerCase();

/** Rows without a KPI are skipped; the last KPI of a group takes the rounding remainder so the group totals 100. */
export function buildItems(rows: readonly ProfileRow[]): EvaluationProfileItemInput[] {
  const groups = new Map<string, { name: string; rows: ProfileRow[] }>();
  for (const row of rows) {
    if (!row.kpiDefinitionId) continue;
    const key = groupKey(row.group);
    const group = groups.get(key) ?? { name: row.group.trim(), rows: [] };
    group.rows.push(row);
    groups.set(key, group);
  }
  return [...groups.values()].map((group, groupIndex) => {
    const total = round4(group.rows.reduce((sum, row) => sum + (row.weight ?? 0), 0));
    let assigned = 0;
    const children = group.rows.map((row, index) => {
      const isLast = index === group.rows.length - 1;
      const weight = isLast
        ? round4(100 - assigned)
        : round4(total === 0 ? 0 : ((row.weight ?? 0) / total) * 100);
      assigned = round4(assigned + weight);
      return {
        kpiDefinitionId: row.kpiDefinitionId,
        weightPercent: weight,
        displayOrder: index + 1,
      };
    });
    return { name: group.name, weightPercent: total, displayOrder: groupIndex + 1, children };
  });
}

export function totalWeight(rows: readonly ProfileRow[]): number {
  return round4(rows.reduce((sum, row) => sum + (row.weight ?? 0), 0));
}

/** Rows added, removed or with a different weight/group compared with the stored rows. */
export function countRowChanges(
  original: readonly ProfileRow[],
  current: readonly ProfileRow[],
): number {
  const before = new Map(original.map((row) => [row.kpiDefinitionId, row]));
  const after = new Map(
    current.filter((row) => row.kpiDefinitionId).map((row) => [row.kpiDefinitionId, row]),
  );
  let changes = 0;
  for (const [kpi, row] of after) {
    const old = before.get(kpi);
    if (!old || old.weight !== row.weight || groupKey(old.group) !== groupKey(row.group)) changes++;
  }
  for (const kpi of before.keys()) if (!after.has(kpi)) changes++;
  return changes;
}
