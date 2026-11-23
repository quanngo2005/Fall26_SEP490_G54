import { DepartmentOverview } from '../../core/department.models';

/** "KPI đạt kế hoạch": KPI rows of the API at or above 100 % achievement, out of all KPI rows. */
export function kpiOnPlan(data: DepartmentOverview | null): { onPlan: number; total: number } {
  const kpis = data?.performance.kpiGroups.flatMap((group) => group.kpis) ?? [];
  return {
    onPlan: kpis.filter((kpi) => kpi.achievementPercent != null && kpi.achievementPercent >= 100)
      .length,
    total: kpis.length,
  };
}

/** Technical, not business: how long the browser may keep the object URL of a download. */
const OBJECT_URL_LIFETIME_MS = 60_000;

/** Saves a file the API generated under the given name. */
export function saveFile(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), OBJECT_URL_LIFETIME_MS);
}
