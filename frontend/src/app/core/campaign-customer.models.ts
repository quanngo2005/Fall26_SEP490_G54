import { AllocationRule } from './campaign.models';
import { PagedResult } from './paged-result.model';

/** Wire values of `campaign_contact_status` (docs/api/CAMPAIGN_CONTRACT.md §5.3). */
export type ContactStatus = 'NOT_CONTACTED' | 'CALLED' | 'APPOINTMENT_SET' | 'MET' | 'CLOSED';

export const CONTACT_STATUSES: readonly ContactStatus[] = [
  'NOT_CONTACTED',
  'CALLED',
  'APPOINTMENT_SET',
  'MET',
  'CLOSED',
];

export interface CampaignCustomer {
  id: string;
  customerCif: string;
  customerName: string;
  phone: string | null;
  note: string | null;
  assigneeEmployeeId: string | null;
  assigneeEmployeeName: string | null;
  contactStatus: ContactStatus;
  resultText: string;
  missedCallCount: number;
  appointmentAt: string | null;
  metAt: string | null;
  closedWorkTypeId: string | null;
  closedWorkTypeName: string | null;
  workId: string | null;
  /** Mirrors the lead Work (NEW, IN_PROGRESS, SUBMITTED, REJECTED, ...); null while unassigned. */
  workStatus: string | null;
  updatedAt: string;
}

export interface CustomerStatusCount {
  status: ContactStatus;
  count: number;
}

export interface CustomerAssigneeSummary {
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  assigned: number;
  closed: number;
}

export interface CampaignCustomerSummary {
  total: number;
  unassigned: number;
  byStatus: CustomerStatusCount[];
  byAssignee: CustomerAssigneeSummary[];
}

export interface CampaignCustomerList {
  page: PagedResult<CampaignCustomer>;
  summary: CampaignCustomerSummary;
}

export interface CampaignCustomerFilter {
  search?: string;
  status?: ContactStatus | '';
  assigneeId?: string;
  unassignedOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export interface AllocateCustomersPayload {
  rule: AllocationRule;
  employeeIds: string[];
}

export interface AllocationCount {
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  count: number;
}

export interface AllocateCustomersResult {
  assignedCount: number;
  byEmployee: AllocationCount[];
}

export interface CustomerImportError {
  column: string;
  code: string;
  message: string;
}

export type CustomerImportAction = 'CREATE' | 'UPDATE' | 'INVALID';

export interface CustomerImportRow {
  rowNumber: number;
  action: CustomerImportAction;
  data: Record<string, string | null>;
  errors: CustomerImportError[];
}

export interface CustomerImportPreview {
  fileName: string;
  summary: { totalRows: number; valid: number; invalid: number; create: number; update: number };
  rows: CustomerImportRow[];
}

export interface CustomerImportResult {
  created: number;
  updated: number;
  skipped: number;
}

/** Template and preview column headers (CampaignCustomerImportService). */
export const IMPORT_COLUMNS = {
  cif: 'Customer CIF*',
  name: 'Tên khách hàng*',
  phone: 'Số điện thoại',
  note: 'Ghi chú',
} as const;

// ---- employee side ("My campaigns") ----

export interface MyCampaign {
  campaignId: string;
  campaignCode: string;
  campaignName: string;
  status: string;
  startDate: string;
  endDate: string;
  totalAssigned: number;
  closed: number;
  byStatus: CustomerStatusCount[];
}

export interface MyCustomerFilter {
  search?: string;
  status?: ContactStatus | '';
  page?: number;
  pageSize?: number;
}

export interface UpdateContactPayload {
  status: ContactStatus;
  callAnswered: boolean | null;
  appointmentAt: string | null;
  metAt: string | null;
  transactionCodes: string[] | null;
  note: string | null;
}
