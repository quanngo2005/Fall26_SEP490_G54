/** Mirrors G54.BLL.Dtos.Notification. Contract: docs/api/NOTIFICATION_CONTRACT.md. */

export const NOTIFICATION_TYPES = [
  'ANNOUNCEMENT',
  'HANDOVER_REQUESTED',
  'HANDOVER_ACCEPTED',
  'HANDOVER_REJECTED',
  'HANDOVER_TRANSFERRED',
  'WORK_OVERDUE',
  'KPI_APPROVED',
  'KPI_REJECTED',
  'REWORK_REQUESTED',
  'TARGET_REVISED',
  'RATING_ONE_STAR',
  'RESULT_PUBLISHED',
  'WORK_PLAN_UPDATED',
  'WORK_ASSIGNED',
  'SUBMISSION_SUBMITTED',
  'SUBMISSION_DECIDED',
  'TRANSACTION_MISMATCH',
  'IMPORT_COMPLETED_WITH_ERRORS',
  'RISK_EVENT_RECORDED',
  'RISK_EVENT_REVERSED',
  'QUALITY_FLAG_RAISED',
  'APPEAL_FILED',
  'APPEAL_DECIDED',
  'CALIBRATION_SUBMITTED',
  'CALIBRATION_DECIDED',
  'CAMPAIGN_DECIDED',
  'EVALUATION_PROFILE_DECIDED',
  'SYSTEM_ALERT',
] as const;

export type NotificationType = (typeof NOTIFICATION_TYPES)[number];
export type NotificationSeverity = 'NORMAL' | 'IMPORTANT';

export type NotificationRelatedType =
  | 'WORK'
  | 'CAMPAIGN'
  | 'HANDOVER'
  | 'KPI'
  | 'TARGET'
  | 'SUBMISSION'
  | 'APPEAL'
  | 'RISK_EVENT'
  | 'QUALITY_FLAG'
  | 'EVALUATION_PERIOD'
  | 'EVALUATION_PROFILE'
  | 'IMPORT_BATCH'
  | 'AUDIT_LOG';

export interface NotificationItem {
  id: string;
  type: NotificationType;
  severity: NotificationSeverity;
  title: string;
  body: string | null;
  relatedType: NotificationRelatedType | null;
  relatedId: string | null;
  senderEmployeeId: string | null;
  senderName: string | null;
  createdAt: string;
  isRead: boolean;
  readAt: string | null;
}

export interface NotificationQuery {
  unread?: boolean;
  type?: NotificationType | '';
  severity?: NotificationSeverity | '';
  keyword?: string;
  page?: number;
  pageSize?: number;
}

export type AnnouncementRelatedType = 'WORK' | 'CAMPAIGN';

export interface SendNotificationRequest {
  title: string;
  body?: string;
  severity?: NotificationSeverity;
  /** Omitted or empty = every active employee inside the sender's scope. */
  recipientEmployeeIds?: string[];
  relatedType?: AnnouncementRelatedType;
  relatedId?: string;
}

export interface NotificationRecipientOption {
  id: string;
  employeeCode: string;
  fullName: string;
  departmentId: string;
  departmentName: string;
  jobPositionTitle: string;
}

export interface NotificationLink {
  commands: string[];
  /** Record ids for the target page, carried in history state so they stay out of the URL. */
  state?: Record<string, string>;
}

/**
 * The screen a notification opens, or null when there is nothing to open. The type wins over the related record
 * because the same record is shown differently to its audience (an appeal is reviewed by a manager and read by the filer).
 */
export function notificationLink(
  item: Pick<NotificationItem, 'type' | 'relatedType' | 'relatedId'>,
): NotificationLink | null {
  const id = item.relatedId;
  switch (item.type) {
    case 'SUBMISSION_SUBMITTED':
      return id
        ? { commands: ['/review-verify'], state: { submissionId: id } }
        : { commands: ['/review-verify'] };
    case 'SUBMISSION_DECIDED':
      return { commands: ['/submissions'] };
    case 'APPEAL_FILED':
      return id
        ? { commands: ['/appeals/department/detail'], state: { appealId: id } }
        : { commands: ['/appeals/department'] };
    case 'APPEAL_DECIDED':
      return { commands: ['/appeals/mine'] };
    case 'CALIBRATION_SUBMITTED':
      return id
        ? { commands: ['/branch-performance/evaluations/approval'], state: { periodId: id } }
        : { commands: ['/branch-performance/evaluations'] };
    case 'RATING_ONE_STAR':
      return {
        commands: ['/import-history'],
        state: id
          ? { importFileType: 'SERVICE_RATING', importBatchId: id }
          : { importFileType: 'SERVICE_RATING' },
      };
    case 'CALIBRATION_DECIDED':
      return { commands: ['/evaluations'] };
    case 'RESULT_PUBLISHED':
      return { commands: ['/my-evaluation'] };
    default:
      break;
  }

  switch (item.relatedType) {
    case 'WORK':
      return id ? { commands: ['/my-work'], state: { workId: id } } : { commands: ['/my-work'] };
    case 'HANDOVER':
      return { commands: ['/my-work'] };
    case 'CAMPAIGN':
      return id
        ? { commands: ['/campaigns/detail'], state: { campaignId: id } }
        : { commands: ['/campaigns'] };
    case 'KPI':
      return { commands: ['/kpi-definitions'] };
    case 'TARGET':
      return { commands: ['/my-targets'] };
    case 'RISK_EVENT':
      return { commands: ['/risk-events'] };
    case 'QUALITY_FLAG':
      return { commands: ['/qa-queue'] };
    case 'EVALUATION_PROFILE':
      return id
        ? { commands: ['/evaluation-profiles/detail'], state: { profileId: id } }
        : { commands: ['/evaluation-profiles'] };
    case 'IMPORT_BATCH':
      return { commands: ['/import-history'] };
    default:
      return null;
  }
}
