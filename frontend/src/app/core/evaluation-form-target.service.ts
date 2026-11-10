import { Injectable } from '@angular/core';

/** The employee and period the evaluation form shows. */
export interface EvaluationFormTarget {
  employeeId: string;
  periodId: string;
}

/** Key of the target in the tab's session storage. */
const TARGET_KEY = 'g54.evaluation.formTarget';

/**
 * Keeps the form's employee and period in the tab instead of the address, so the address names neither.
 * The target survives a refresh and is not shared between tabs.
 */
@Injectable({ providedIn: 'root' })
export class EvaluationFormTargetService {
  save(target: EvaluationFormTarget): void {
    try {
      sessionStorage.setItem(TARGET_KEY, JSON.stringify(target));
    } catch {
      // Storage is unavailable: the form then opens as "not found" instead of a stale target.
    }
  }

  /** The saved target, or null when none was saved or what is stored is not a target. */
  read(): EvaluationFormTarget | null {
    try {
      const stored: unknown = JSON.parse(sessionStorage.getItem(TARGET_KEY) ?? 'null');
      return isTarget(stored) ? { employeeId: stored.employeeId, periodId: stored.periodId } : null;
    } catch {
      return null;
    }
  }
}

function isTarget(value: unknown): value is EvaluationFormTarget {
  if (typeof value !== 'object' || value === null) return false;
  const { employeeId, periodId } = value as Record<string, unknown>;
  return (
    typeof employeeId === 'string' &&
    employeeId.length > 0 &&
    typeof periodId === 'string' &&
    periodId.length > 0
  );
}
