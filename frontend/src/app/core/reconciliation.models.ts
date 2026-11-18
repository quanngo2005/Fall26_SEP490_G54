/** Mirrors G54.BLL.Dtos.Reconciliation. Contract: docs/api/RECONCILIATION_CONTRACT.md. */

export type ReconciliationStatus = 'NOT_MATCHED' | 'IN_REVIEW' | 'MATCHED';

export const RECONCILIATION_REASONS = [
  'EMPLOYEE_MISMATCH',
  'PRODUCT_MISMATCH',
  'CUSTOMER_MISMATCH',
  'REFERRER_MISMATCH',
  'TRANSACTION_BEFORE_HANDOVER',
  'METRIC_TYPE_MISMATCH',
  'NO_IMPORTED_TRANSACTION',
  'KPI_TARGET_NOT_ASSIGNED',
  'KPI_AGGREGATION_NOT_SUPPORTED',
  'PERIOD_LOCKED',
  'REFERRER_NOT_CREDITED',
] as const;

export type ReconciliationReason = (typeof RECONCILIATION_REASONS)[number];

export interface ReconciliationFilter {
  periodId?: string;
  from?: string;
  to?: string;
  departmentId?: string;
  jobPositionId?: string;
  productCode?: string;
  status?: ReconciliationStatus;
  reason?: ReconciliationReason;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface ReconciliationItem {
  id: string;
  transactionCode: string;
  workId: string;
  workCode: string;
  workTitle: string;
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  jobTitle: string | null;
  departmentId: string;
  departmentName: string | null;
  customerCif: string | null;
  referEmployeeCode: string | null;
  productCode: string | null;
  amount: number | null;
  transactionDate: string | null;
  status: ReconciliationStatus;
  reason: string | null;
  submittedAt: string;
  updatedAt: string;
}

export interface ReconciliationCounts {
  all: number;
  notMatched: number;
  inReview: number;
  matched: number;
}

export interface ReconciliationFilterOptions {
  departments: { id: string; name: string }[];
  jobPositions: { id: string; title: string }[];
  products: { code: string; name: string | null }[];
  reasons: string[];
}

export interface ReconciliationList {
  items: ReconciliationItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  counts: ReconciliationCounts;
  options: ReconciliationFilterOptions;
}

export interface ReconciliationWork {
  id: string;
  code: string;
  title: string;
  status: string;
  workTypeName: string | null;
  productCode: string | null;
  customerName: string | null;
  customerCif: string | null;
}

export interface ReconciliationSubmission {
  id: string;
  submissionNo: number;
  status: string;
  submittedAt: string;
  submitterName: string;
}

export interface ReconciliationImport {
  batchId: string;
  fileName: string;
  importedAt: string;
  importedByName: string | null;
  transactionDate: string;
  amount: number;
}

export type ReconciliationFieldCode =
  | 'TRANSACTION_CODE'
  | 'CUSTOMER'
  | 'EMPLOYEE'
  | 'PRODUCT'
  | 'REFERRAL'
  | 'TRANSACTION_DATE'
  | 'AMOUNT';

export interface ReconciliationField {
  field: ReconciliationFieldCode;
  declared: string | null;
  imported: string | null;
  /** Null when there is nothing to compare. */
  matched: boolean | null;
}

export interface ReconciliationSibling {
  id: string;
  transactionCode: string;
  status: ReconciliationStatus;
  reason: string | null;
}

export type ReconciliationHistoryKind =
  'CODE_ADDED' | 'IMPORTED' | 'RECONCILED' | 'QUALITY_FLAG' | 'RISK_EVENT' | 'APPEAL';

export interface ReconciliationHistory {
  kind: ReconciliationHistoryKind;
  at: string;
  status: string | null;
  reason: string | null;
  actor: string | null;
  reference: string | null;
}

export type ReconciliationAppealState =
  'NOT_APPLICABLE' | 'AVAILABLE' | 'EXPIRED' | 'IN_PROGRESS' | 'DECIDED';

export interface ReconciliationAppeal {
  state: ReconciliationAppealState;
  deadline: string | null;
  appealId: string | null;
  status: string | null;
  submissionId: string | null;
}

export type FlagBlockedReason =
  'NO_PERMISSION' | 'NO_APPROVED_SUBMISSION' | 'OWN_WORK' | 'OPEN_FLAG';

export interface ReconciliationActions {
  canFlag: boolean;
  flagSubmissionId: string | null;
  flagBlockedReason: FlagBlockedReason | null;
  canRecordRisk: boolean;
}

export interface ReconciliationDetail {
  item: ReconciliationItem;
  work: ReconciliationWork;
  submission: ReconciliationSubmission | null;
  import: ReconciliationImport | null;
  fields: ReconciliationField[];
  comparedFields: number;
  matchedFields: number;
  siblings: ReconciliationSibling[];
  history: ReconciliationHistory[];
  appeal: ReconciliationAppeal;
  actions: ReconciliationActions;
}

export type QualityFlagSeverity = 'LOW' | 'MEDIUM' | 'HIGH';
