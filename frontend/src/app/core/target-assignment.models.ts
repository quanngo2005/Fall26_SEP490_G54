/** API values are UPPER_SNAKE strings (docs/api/TARGET_ASSIGNMENT_CONTRACT.md). */
export type TargetAssignmentStatus = 'DRAFT' | 'ACTIVE' | 'SUPERSEDED' | 'CLOSED';
export type TargetFeedbackStatus = 'NOT_VIEWED' | 'CONFIRMED' | 'COMMENTED';
export type TargetFeedbackType = 'CONFIRM' | 'COMMENT';

/** Exactly one of `departmentId` / `employeeId` is set. `feedbackStatus` is null for department-owned rows. */
export interface TargetAssignment {
  id: string;
  kpiDefinitionId: string;
  evaluationPeriodId: string;
  departmentId: string | null;
  employeeId: string | null;
  parentAssignmentId: string | null;
  targetValue: number;
  status: TargetAssignmentStatus;
  updatedAt: string;
  feedbackStatus: TargetFeedbackStatus | null;
  /** When the employee confirmed or commented; null unless `feedbackStatus` is CONFIRMED or COMMENTED. */
  feedbackAt: string | null;
}

export interface TargetAssignmentFilter {
  evaluationPeriodId?: string;
  kpiDefinitionId?: string;
  departmentId?: string;
  status?: TargetAssignmentStatus;
}

export interface TargetAssignmentFeedback {
  id: string;
  targetAssignmentId: string;
  employeeId: string;
  type: TargetFeedbackType;
  comment: string | null;
  createdAt: string;
}

// =========================================================================
// Year target import (docs/api/TARGET_ALLOCATION_CONTRACT.md, phase 1)
// =========================================================================

export type TargetImportRowAction = 'CREATE' | 'UPDATE' | 'UNCHANGED' | 'INVALID';

export interface TargetImportError {
  column: string;
  code: string;
  message: string;
}

export interface TargetImportRow {
  rowNumber: number;
  kpiCode: string | null;
  kpiName: string | null;
  unit: string | null;
  value: number | null;
  previousYearValue: number | null;
  delta: number | null;
  deltaPercent: number | null;
  action: TargetImportRowAction;
  errors: TargetImportError[];
  warnings: TargetImportError[];
}

export interface TargetImportSummary {
  total: number;
  valid: number;
  invalid: number;
  create: number;
  update: number;
  unchanged: number;
}

export interface TargetImportKpiNotInFile {
  kpiCode: string;
  kpiName: string;
}

export interface TargetImportQuarterRatios {
  q1: number | null;
  q2: number | null;
  q3: number | null;
  q4: number | null;
}

export interface TargetImportPreview {
  fileName: string;
  branchId: string;
  year: number;
  summary: TargetImportSummary;
  quarterRatios: TargetImportQuarterRatios;
  kpisNotInFile: TargetImportKpiNotInFile[];
  rows: TargetImportRow[];
}

export interface TargetImportCommitResult {
  branchId: string;
  year: number;
  targets: unknown[];
  committed: number;
  skipped: number;
  needsReview: boolean;
}

/** Wizard step returned by `GET /setup` (docs/api/TARGET_ALLOCATION_CONTRACT.md, phase 2.A). */
export type TargetSetupStep = 'IMPORT' | 'QUARTER' | 'ALLOCATE' | 'PUBLISH' | 'PUBLISHED';

export interface TargetSetupKpiBlocker {
  kpiId: string;
  kpiCode: string;
  kpiName: string;
}

export interface TargetSetupStepCount {
  done: number;
  total: number;
}

export interface TargetSetup {
  /** Anchor for the quarter-split / allocation / publish routes; null before the first import. */
  branchYearId: string | null;
  hasBranchYear: boolean;
  importedAt: string | null;
  rowCount: number;
  step: TargetSetupStep;
  status: string;
  quarters: TargetSetupStepCount;
  allocation: TargetSetupStepCount;
  canPublish: boolean;
  blockers: TargetSetupKpiBlocker[];
}

// =========================================================================
// Quarter split (docs/api/TARGET_ALLOCATION_CONTRACT.md, phase 2.A)
// =========================================================================

export interface QuarterPeriod {
  periodId: string;
  quarter: number;
  locked: boolean;
}

export interface QuarterValues {
  q1: number;
  q2: number;
  q3: number;
  q4: number;
}

export interface QuarterSplitRow {
  kpiId: string;
  kpiCode: string;
  kpiName: string;
  unit: string;
  isRatio: boolean;
  yearValue: number;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
  previousYear: QuarterValues | null;
  sum: number;
  diff: number;
  matched: boolean;
}

export interface QuarterSplit {
  branchYearId: string;
  year: number;
  status: string;
  importedAt: string | null;
  fileRatios: QuarterValues | null;
  quarters: QuarterPeriod[];
  rows: QuarterSplitRow[];
}

export interface SaveQuarterSplitRowRequest {
  kpiId: string;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
}

export interface SaveQuarterSplitRequest {
  rows: SaveQuarterSplitRowRequest[];
}

// =========================================================================
// KPI allocation (docs/api/TARGET_ALLOCATION_CONTRACT.md, phase 2.A)
// =========================================================================

export interface DepartmentAllocationRow {
  departmentId: string;
  departmentName: string;
  headcount: number;
  year: number;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
  previousYear: number | null;
  matched: boolean;
}

export interface DepartmentAllocationMetadata {
  departmentId: string;
  departmentName: string;
  reason: string;
}

export interface AllocationTotals {
  year: number;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
}

export interface AllocationRemaining {
  year: number;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
}

export interface BranchTargetValues {
  year: number;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
}

export interface KpiAllocation {
  kpiId: string;
  kpiName: string;
  unit: string;
  workType: string;
  branch: BranchTargetValues;
  departments: DepartmentAllocationRow[];
  totals: AllocationTotals;
  remaining: AllocationRemaining;
  hiddenDepartments: DepartmentAllocationMetadata[];
}

export interface SaveKpiAllocationDepartmentRequest {
  departmentId: string;
  year: number;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
}

export interface SaveKpiAllocationRequest {
  departments: SaveKpiAllocationDepartmentRequest[];
}

// =========================================================================
// Publish (docs/api/TARGET_ALLOCATION_CONTRACT.md, phase 2.A)
// =========================================================================

export interface PublishResult {
  publishedAt: string;
  branchYearCount: number;
  quarterCount: number;
  departmentYearCount: number;
  departmentQuarterCount: number;
}
