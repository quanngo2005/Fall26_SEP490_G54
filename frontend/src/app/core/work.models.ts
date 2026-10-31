/** Work API enums are PascalCase on the wire (docs/api/WORK_CONTRACT.md, section 6). */
export type WorkStatus =
  | 'New'
  | 'InProgress'
  | 'HandedOver'
  | 'Submitted'
  | 'Verified'
  | 'Rejected'
  | 'Overdue'
  | 'Done'
  | 'Cancelled';

export type WorkPriority = 'Low' | 'Medium' | 'High' | 'Critical';

/** Mirrors `WorkListItemDto` returned by `GET /works`. Dates are date-only (`YYYY-MM-DD`) unless noted. */
export interface WorkListItem {
  id: string;
  code: string;
  title: string;
  workTypeId: string | null;
  workTypeName: string | null;
  businessCampaignId: string | null;
  assigneeEmployeeId: string;
  assigneeEmployeeCode: string | null;
  assigneeEmployeeName: string | null;
  departmentId: string | null;
  departmentCode: string | null;
  departmentName: string | null;
  priority: WorkPriority;
  status: WorkStatus;
  plannedStartDate: string | null;
  plannedEndDate: string | null;
  /** ISO 8601 date-time with offset. */
  updatedAt: string;
}

/** Mirrors `WorkDetailDto` returned by `GET /works/{id}` and `PATCH /works/{id}/status`. */
export interface WorkDetail {
  id: string;
  code: string;
  title: string;
  description: string | null;
  workTypeId: string | null;
  workTypeName: string | null;
  businessCampaignId: string | null;
  businessCampaignName: string | null;
  assigneeEmployeeId: string;
  assigneeEmployeeName: string | null;
  assigneeEmployeeCode: string | null;
  priority: WorkPriority;
  status: WorkStatus;
  plannedStartDate: string | null;
  plannedEndDate: string | null;
  estimatedHours: number | null;
  updatedAt: string;
}

export interface WorkFilter {
  keyword?: string;
  /** Sent as a comma separated list; empty means every status. */
  status?: readonly WorkStatus[];
  assigneeEmployeeId?: string;
  departmentId?: string;
  includeChildDepartments?: boolean;
  workTypeId?: string;
  priority?: WorkPriority;
  page?: number;
  /** Server limit is 100. */
  pageSize?: number;
}

/** Only these target statuses can be set through `PATCH /works/{id}/status`. */
export type WorkManualStatus = 'InProgress' | 'Done' | 'Cancelled';

export interface WorkStatusChange {
  status: WorkManualStatus;
  /** Required when cancelling; at most 500 characters. */
  reason?: string;
}
