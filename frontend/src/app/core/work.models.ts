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

/** Work still needing action; mirrors `WorkStatusGroups.Open` in the backend. */
export const OPEN_WORK_STATUSES: readonly WorkStatus[] = [
  'New',
  'InProgress',
  'HandedOver',
  'Submitted',
  'Rejected',
  'Overdue',
];

/** Work verified or closed out; mirrors `WorkStatusGroups.Completed` in the backend. */
export const COMPLETED_WORK_STATUSES: readonly WorkStatus[] = ['Done', 'Verified'];

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
  /** The fields below arrive with the work detail screen (SCR-WRK-03); older responses do not have them. */
  customerName?: string | null;
  customerCif?: string | null;
  /** ISO 8601 date-time with offset. */
  assignedAt?: string | null;
  assignedByEmployeeId?: string | null;
  assignedByName?: string | null;
  assignedByPositionTitle?: string | null;
  checklistItems?: WorkChecklistItem[];
  /** Share of done checklist items, 0 to 100; null while the work has no checklist and is not done yet. */
  progressPercent?: number | null;
  relatedKpis?: WorkKpi[];
  /** The caller may change the deadline, priority and checklist (the assignee's manager). */
  canEditPlan?: boolean;
  /** The caller may tick checklist items (the assignee, while the work is open). */
  canUpdateChecklist?: boolean;
}

export interface WorkChecklistItem {
  id: string;
  title: string;
  isDone: boolean;
  sortOrder: number;
  /** ISO 8601 date-time with offset; null while the item is open. */
  doneAt: string | null;
}

export type WorkKpiCreditRole = 'PROCESSOR' | 'REFERRER';

export interface WorkKpi {
  code: string;
  name: string;
  creditRole: WorkKpiCreditRole;
}

/** One entry of the activity log (`GET /works/{id}/activities`), newest first. `actorName` is null for the system. */
export interface WorkActivity {
  id: string;
  action: string;
  message: string;
  actorEmployeeId: string | null;
  actorName: string | null;
  /** ISO 8601 date-time with offset. */
  createdAt: string;
}

/** A checklist line of `PUT /works/{id}/plan`: an existing item keeps its id, a new one has none. */
export interface WorkPlanChecklistItem {
  id?: string;
  title: string;
}

/** `PUT /works/{id}/plan` body. Done items must be listed unchanged; not-done items left out are removed. */
export interface UpdateWorkPlanRequest {
  /** `YYYY-MM-DD`. */
  plannedEndDate: string;
  /** Omit to keep the current priority. */
  priority?: WorkPriority;
  /** Omit to keep the checklist as it is. */
  checklist?: WorkPlanChecklistItem[];
  /** At most 1000 characters. */
  note?: string;
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

/** `POST /works` body (`CreateWorkRequestDto`). Dates are `YYYY-MM-DD`. */
export interface CreateWorkRequest {
  title: string;
  workTypeId: string;
  assigneeEmployeeId: string;
  priority: WorkPriority;
  plannedEndDate: string;
  checklistItems?: string[];
  customerName?: string;
  customerCif?: string;
}
