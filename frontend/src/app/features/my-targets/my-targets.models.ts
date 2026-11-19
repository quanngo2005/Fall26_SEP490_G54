/**
 * DTOs for the employee "my-targets" API (Target Allocation v3, BR-36).
 * Contract: docs/api/TARGET_ALLOCATION_CONTRACT.md § "Employee side: my-targets".
 */
import { KpiAggregationType } from '../../core/kpi-definition.models';

export type AckStatus = 'PENDING' | 'CONFIRMED' | 'RECONFIRM';

export interface MyTargetKpi {
  readonly kpiId: string;
  readonly kpiCode: string;
  readonly kpiName: string;
  readonly unit: string;
  readonly aggregationType: KpiAggregationType;
  readonly year: number;
  readonly q1: number;
  readonly q2: number;
  readonly q3: number;
  readonly q4: number;
  readonly previousYearTarget: number | null;
  readonly previousYearActual: number | null;
  readonly changed: boolean;
  readonly changedOldValue: number | null;
  readonly changedNewValue: number | null;
  readonly hasFeedback: boolean;
}

export interface MyTargetYear {
  readonly year: number;
  readonly employeeId: string;
  readonly employeeName: string;
  readonly departmentId: string;
  readonly departmentName: string;
  readonly ackStatus: AckStatus;
  readonly confirmedAt: string | null;
  readonly kpis: readonly MyTargetKpi[];
}

export interface MyTargetConfirmResult {
  readonly year: number;
  readonly ackStatus: AckStatus;
  readonly confirmedAt: string;
}

export interface MyTargetFeedbackRequest {
  readonly kpiId: string;
  readonly reason: string;
  readonly proposedYear?: number | null;
  readonly proposedQ1?: number | null;
  readonly proposedQ2?: number | null;
  readonly proposedQ3?: number | null;
  readonly proposedQ4?: number | null;
}

/**
 * Shape inferred from the `TargetAssignmentFeedback` entity (backend/src/G54.DAL/Entities/TargetAssignmentFeedback.cs)
 * extended with `kpiId`/author display fields; the contract doc does not spell out `TargetFeedbackDto` field names,
 * so this is a best-effort mapping to reconcile once the BE side lands.
 */
export interface MyTargetFeedback {
  readonly id: string;
  readonly kpiId: string;
  readonly employeeId: string;
  readonly authorName: string;
  readonly reason: string;
  readonly proposedYear: number | null;
  readonly proposedQ1: number | null;
  readonly proposedQ2: number | null;
  readonly proposedQ3: number | null;
  readonly proposedQ4: number | null;
  readonly reply: string | null;
  readonly repliedBy: string | null;
  readonly repliedByName: string | null;
  readonly repliedAt: string | null;
  readonly createdAt: string;
}

export type ProgressPeriod = 'YEAR' | 'Q1' | 'Q2' | 'Q3' | 'Q4';

export type PaceStatus = 'ACHIEVED' | 'ON_PACE' | 'SLOW' | 'AT_RISK';

export interface MyTargetKpiProgress {
  readonly target: number;
  readonly achieved: number;
  readonly inReviewCount: number;
  readonly achievedPercent: number;
  readonly expectedPacePercent: number;
  readonly paceStatus: PaceStatus;
}

export interface MyTargetProgressKpi {
  readonly kpiId: string;
  readonly kpiCode: string;
  readonly kpiName: string;
  readonly unit: string;
  readonly progress: MyTargetKpiProgress;
}

export interface MyTargetProgress {
  readonly year: number;
  readonly period: string;
  readonly kpis: readonly MyTargetProgressKpi[];
}
