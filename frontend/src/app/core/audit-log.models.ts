/** Audit log API (SCR-ADM-05, SCR-ADM-05-D1). See docs/api/AUDIT_LOG_CONTRACT.md. */
export const AUDIT_ACTIONS = ['Create', 'Update', 'Delete', 'SoftDelete'] as const;
export type AuditAction = (typeof AUDIT_ACTIONS)[number];

export interface AuditLogListItem {
  id: string;
  entityName: string;
  entityId: string;
  action: AuditAction;
  actorId: string | null;
  actorName: string | null;
  ipAddress: string | null;
  occurredAt: string;
  changedColumns: string[] | null;
}

export interface AuditLogDetail extends AuditLogListItem {
  userAgent: string | null;
  httpMethod: string | null;
  requestPath: string | null;
  traceId: string | null;
  oldValues: Record<string, unknown> | null;
  newValues: Record<string, unknown> | null;
}

export interface AuditLogQuery {
  page?: number;
  pageSize?: number;
  entityName?: string;
  entityId?: string;
  action?: AuditAction | '';
  actorId?: string;
  /** ISO 8601 with offset. */
  from?: string;
  to?: string;
  keyword?: string;
  sort?: 'occurredAt desc' | 'occurredAt asc';
}
