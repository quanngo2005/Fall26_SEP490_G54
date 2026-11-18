/** Mirrors the risk event screens of docs/api/RISK_EVENT_CONTRACT.md §9. */

export type RiskSeverity = 'LOW' | 'MEDIUM' | 'HIGH' | 'CRITICAL';
export type RiskStatus = 'OPEN' | 'CONFIRMED' | 'DISMISSED' | 'RESOLVED';
/** What the list shows: REVERSED once the event was reversed, RECORDED otherwise. */
export type RiskState = 'RECORDED' | 'REVERSED';

/** Event types the recording form offers; the API stores the code as free text (max 50 characters). */
export const RISK_EVENT_TYPES = [
  'WRONG_CUSTOMER_INFO',
  'MISSING_DOCUMENTS',
  'WRONG_PRODUCT_ADVICE',
  'KYC_VIOLATION',
  'OTHER',
] as const;

export const RISK_SEVERITIES: readonly RiskSeverity[] = ['LOW', 'MEDIUM', 'HIGH', 'CRITICAL'];

export interface RiskEventFilter {
  periodId?: string;
  departmentId?: string;
  severity?: RiskSeverity;
  state?: RiskState;
  search?: string;
  sortBy?: string;
  sortDir?: 'asc' | 'desc';
  page?: number;
  pageSize?: number;
}

export interface RiskEventListItem {
  id: string;
  /** `RSK-0045`. */
  code: string;
  occurredAt: string;
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  departmentId: string;
  departmentName: string;
  eventType: string;
  severity: RiskSeverity;
  /** Positive number; the screens show it as `−N`. */
  penaltyPoints: number;
  workId: string | null;
  workCode: string | null;
  status: RiskStatus;
  state: RiskState;
  createdAt: string;
}

export interface RiskEventCounts {
  recorded: number;
  reversed: number;
}

export interface RiskEventDepartment {
  id: string;
  name: string;
}

export interface RiskEventList {
  items: RiskEventListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  counts: RiskEventCounts;
  departments: RiskEventDepartment[];
}

export interface RiskEventAttachment {
  id: string;
  fileName: string;
  fileSize: number;
  mimeType: string;
}

export interface RiskEventDetails {
  item: RiskEventListItem;
  jobPositionTitle: string | null;
  description: string;
  periodCode: string | null;
  periodName: string | null;
  recordedByName: string | null;
  recordedAt: string;
  reversedAt: string | null;
  reversedByName: string | null;
  reversalReason: string | null;
  attachments: RiskEventAttachment[];
  canReverse: boolean;
}

export interface CreateRiskEventRequest {
  employeeId: string;
  eventType: string;
  description: string;
  severity: RiskSeverity;
  occurredAt: string;
  penaltyPoints: number;
  workCode?: string;
  /** Always true from the screen: the event is recorded and penalised in one step. */
  record: true;
}

export interface CreatedRiskEvent {
  id: string;
  code: string;
}
