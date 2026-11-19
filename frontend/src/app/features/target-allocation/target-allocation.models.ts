/**
 * Phase 3 (tier-3 employee allocation) and suggestion-engine DTOs
 * (docs/api/TARGET_ALLOCATION_CONTRACT.md, "Phase 3" and "Suggestion engine" sections).
 * Not yet in `core/target-assignment.models.ts`, so they live next to the feature that uses them.
 */

export type EmployeePeriodKey = 'year' | 'q1' | 'q2' | 'q3' | 'q4';

export const EMPLOYEE_QUARTER_KEYS: readonly EmployeePeriodKey[] = ['q1', 'q2', 'q3', 'q4'];

export interface EmployeeAllocationPeriodValues {
  readonly year: number;
  readonly q1: number;
  readonly q2: number;
  readonly q3: number;
  readonly q4: number;
}

export interface EmployeeAllocationEmployeeRow extends EmployeeAllocationPeriodValues {
  readonly employeeId: string;
  readonly employeeName: string;
  readonly jobPositionName: string;
  readonly previousYear: number | null;
  readonly previousYearActual: number | null;
  readonly matched: boolean;
}

export interface EmployeeAllocationLockedEmployee {
  readonly employeeId: string;
  readonly employeeName: string;
  readonly reason: string;
}

/** `GET /{deptYearId}/employee-allocation` and one entry of `GET /{deptYearId}/employee-allocations`. */
export interface EmployeeAllocation {
  readonly deptYearId: string;
  readonly kpiId: string;
  readonly kpiName: string;
  readonly unit: string;
  readonly departmentId: string;
  readonly departmentName: string;
  readonly status: string;
  readonly department: EmployeeAllocationPeriodValues;
  readonly employees: readonly EmployeeAllocationEmployeeRow[];
  readonly totals: EmployeeAllocationPeriodValues;
  readonly remaining: EmployeeAllocationPeriodValues;
  readonly lockedEmployees: readonly EmployeeAllocationLockedEmployee[];
}

export interface SaveEmployeeAllocationEmployeeRequest extends EmployeeAllocationPeriodValues {
  readonly employeeId: string;
}

export interface SaveEmployeeAllocationRequest {
  readonly employees: readonly SaveEmployeeAllocationEmployeeRequest[];
}

export interface PublishEmployeesResult {
  readonly publishedAt: string;
  readonly departmentId: string;
  readonly year: number;
  readonly employeeYearCount: number;
  readonly employeeQuarterCount: number;
}

// ---------------------------------------------------------------------------
// Suggestion engine (POST /target-suggestions/{year})
// ---------------------------------------------------------------------------

export type SuggestionEngineBasis = 'TARGET_LAST_YEAR' | 'ACTUAL_LAST_YEAR';
export type SuggestionEngineEmployees = 'ALL' | 'NEW_ONLY';
export type SuggestionEngineOverwrite = 'EMPTY_ONLY' | 'UNLOCKED';
export type SuggestionEngineRemainderMethod = 'LARGEST_REMAINDER' | 'BUFFER';
export type SuggestionEngineRoundingMode = 'NEAREST' | 'UP' | 'DOWN';

export interface SuggestionEngineCurrentValue {
  readonly unitId: string;
  readonly year: number | null;
  readonly q1: number | null;
  readonly q2: number | null;
  readonly q3: number | null;
  readonly q4: number | null;
  readonly locked: boolean;
}

/** Tier 3 is always scoped to the one KPI `anchorId` (`deptYearId`) pins. */
export interface SuggestionEngineRequest {
  readonly tier: 3;
  readonly basis: SuggestionEngineBasis;
  readonly anchorId: string;
  readonly employees: SuggestionEngineEmployees;
  readonly periods: 'YEAR_PLUS_4Q';
  readonly overwrite: SuggestionEngineOverwrite;
  readonly currentValues: readonly SuggestionEngineCurrentValue[];
  readonly rounding: {
    readonly moneyStep: number;
    readonly countStep: number;
    readonly mode: SuggestionEngineRoundingMode;
  };
  readonly remainder: {
    readonly method: SuggestionEngineRemainderMethod;
    readonly bufferUnitId: string | null;
  };
}

export interface SuggestionEngineCell {
  readonly unitId: string;
  readonly unitName: string;
  readonly year: number | null;
  readonly q1: number | null;
  readonly q2: number | null;
  readonly q3: number | null;
  readonly q4: number | null;
  readonly usedFallbackAverage: boolean;
  readonly overwritten: boolean;
  readonly locked: boolean;
  readonly lockReason: string | null;
}

export interface SuggestionEngineLockedUnit {
  readonly unitId: string;
  readonly unitName: string;
  readonly reason: string;
}

export interface SuggestionEngineKpiResult {
  readonly kpiId: string;
  readonly kpiCode: string;
  readonly kpiName: string;
  readonly unit: string;
  readonly aggregationType: string;
  readonly cells: readonly SuggestionEngineCell[];
  readonly lockedUnits: readonly SuggestionEngineLockedUnit[];
  readonly total: number;
  readonly sumMatches: boolean;
  readonly disabledReason: string | null;
  readonly warnings: readonly string[];
}

export interface SuggestionEngineResponse {
  readonly tier: number;
  readonly basis: SuggestionEngineBasis;
  readonly overwrite: SuggestionEngineOverwrite;
  readonly kpis: readonly SuggestionEngineKpiResult[];
  readonly warnings: readonly string[];
}

// ---------------------------------------------------------------------------
// DM reply to an employee's feedback (POST /my-targets/feedback/{feedbackId}/reply)
// ---------------------------------------------------------------------------

export interface MyTargetFeedbackReplyResult {
  readonly id: string;
  readonly reply: string;
  readonly repliedBy: string;
  readonly repliedAt: string;
}
