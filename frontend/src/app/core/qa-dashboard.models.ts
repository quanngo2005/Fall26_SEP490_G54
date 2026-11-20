/**
 * QA dashboard (SCR-PRF-04, UC118). Mirrors `G54.BLL.Dtos.Quality.QaDashboardDtos`.
 * Contract: docs/api/QA_DASHBOARD_CONTRACT.md. Dates (`DateOnly`) arrive as `yyyy-MM-dd`, instants as ISO 8601 strings.
 */

export interface QaDashboardFilter {
  periodId?: string | null;
  departmentId?: string | null;
  jobPositionId?: string | null;
  jobPositionCode?: string | null;
}

export interface QaDashboardPeriod {
  id: string;
  code: string;
  name: string;
  startDate: string;
  endDate: string;
  lockDeadline: string;
  isLocked: boolean;
}

export interface QaDashboardBanner {
  inReviewCount: number;
  message: string;
  warning: string;
  hasUnmatchedInReview: boolean;
}

export interface QaDashboardNotMatchedCard {
  count: number;
  oldestDays: number | null;
  subtext: string;
}

export interface QaDashboardInReviewCard {
  count: number;
  subtext: string;
}

export interface QaDashboardQaQueueCard {
  total: number;
  sampleCount: number;
  flagCount: number;
  /** Pending AI-flagged call segments. Planned BE addition (QA-05): absent until the API sends it. */
  aiSegmentCount?: number | null;
  subtext: string;
}

export interface QaDashboardRiskEventsCard {
  count: number;
  /** Risk events not yet closed. Planned BE addition (QA-05): absent until the API sends it. */
  openCount?: number | null;
  totalPenaltyPoints: number;
  subtext: string;
}

export interface QaDashboardCards {
  notMatched: QaDashboardNotMatchedCard;
  inReview: QaDashboardInReviewCard;
  qaQueue: QaDashboardQaQueueCard;
  riskEvents: QaDashboardRiskEventsCard;
}

export interface QaDashboardWeeklyRate {
  weekLabel: string;
  weekNumber: number;
  year: number;
  startDate: string;
  endDate: string;
  matchedCount: number;
  totalCount: number;
  matchRate: number | null;
}

export interface QaDashboardMatchedRateChart {
  title: string;
  subtitle: string;
  currentRate: number | null;
  currentWeekLabel: string;
  weeks: QaDashboardWeeklyRate[];
}

export interface QaDashboardTopEmployee {
  employeeId: string;
  employeeCode: string;
  fullName: string;
  jobTitle: string;
  departmentName: string | null;
  discrepancyCount: number;
  flagCount: number;
}

export interface QaDashboardReasonCount {
  reasonCode: string;
  reason: string;
  count: number;
}

export interface QaDashboard {
  asOf: string;
  period: QaDashboardPeriod;
  banner: QaDashboardBanner;
  kpiCards: QaDashboardCards;
  matchedRateChart: QaDashboardMatchedRateChart;
  topDiscrepancyEmployees: QaDashboardTopEmployee[];
  commonDiscrepancyReasons: QaDashboardReasonCount[];
}

export interface QaDashboardPeriodOption {
  id: string;
  code: string;
  name: string;
  isCurrent: boolean;
}

export interface QaDashboardDepartmentOption {
  id: string;
  code: string;
  name: string;
}

export interface QaDashboardJobPositionOption {
  id: string;
  code: string;
  title: string;
}

export interface QaDashboardFilterOptions {
  periods: QaDashboardPeriodOption[];
  departments: QaDashboardDepartmentOption[];
  jobPositions: QaDashboardJobPositionOption[];
}
