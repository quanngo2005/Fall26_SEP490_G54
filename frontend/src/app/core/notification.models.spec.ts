import { notificationLink, NotificationItem } from './notification.models';

describe('notificationLink', () => {
  const link = (
    type: NotificationItem['type'],
    relatedType: NotificationItem['relatedType'],
    relatedId: string | null = 'abc',
  ) => notificationLink({ type, relatedType, relatedId });

  it('opens a work in the work board by id', () => {
    expect(link('WORK_ASSIGNED', 'WORK')).toEqual({
      commands: ['/my-work'],
      state: { workId: 'abc' },
    });
    expect(link('WORK_OVERDUE', 'WORK', null)).toEqual({ commands: ['/my-work'] });
  });

  it('sends each audience to its own screen for the same kind of record', () => {
    expect(link('APPEAL_FILED', 'APPEAL')).toEqual({
      commands: ['/appeals/department/detail'],
      state: { appealId: 'abc' },
    });
    expect(link('APPEAL_DECIDED', 'APPEAL')).toEqual({ commands: ['/appeals/mine'] });
    expect(link('SUBMISSION_SUBMITTED', 'SUBMISSION')).toEqual({
      commands: ['/review-verify'],
      state: { submissionId: 'abc' },
    });
    expect(link('SUBMISSION_DECIDED', 'SUBMISSION')).toEqual({ commands: ['/submissions'] });
  });

  it('routes evaluation notifications by type', () => {
    expect(link('CALIBRATION_SUBMITTED', 'EVALUATION_PERIOD')).toEqual({
      commands: ['/branch-performance/evaluations/approval'],
      state: { periodId: 'abc' },
    });
    expect(link('CALIBRATION_DECIDED', 'EVALUATION_PERIOD')).toEqual({
      commands: ['/evaluations'],
    });
    expect(link('RESULT_PUBLISHED', 'EVALUATION_PERIOD')).toEqual({ commands: ['/my-evaluation'] });
  });

  it('maps the remaining related records', () => {
    expect(link('CAMPAIGN_DECIDED', 'CAMPAIGN')).toEqual({
      commands: ['/campaigns/detail'],
      state: { campaignId: 'abc' },
    });
    expect(link('KPI_APPROVED', 'KPI')).toEqual({ commands: ['/kpi-definitions'] });
    expect(link('TARGET_REVISED', 'TARGET')).toEqual({ commands: ['/my-targets'] });
    expect(link('RISK_EVENT_RECORDED', 'RISK_EVENT')).toEqual({ commands: ['/risk-events'] });
    expect(link('QUALITY_FLAG_RAISED', 'QUALITY_FLAG')).toEqual({ commands: ['/qa-queue'] });
    expect(link('EVALUATION_PROFILE_DECIDED', 'EVALUATION_PROFILE')).toEqual({
      commands: ['/evaluation-profiles/detail'],
      state: { profileId: 'abc' },
    });
    expect(link('IMPORT_COMPLETED_WITH_ERRORS', 'IMPORT_BATCH')).toEqual({
      commands: ['/import-history'],
    });
    expect(link('RATING_ONE_STAR', 'IMPORT_BATCH')).toEqual({
      commands: ['/import-history'],
      state: { importFileType: 'SERVICE_RATING', importBatchId: 'abc' },
    });
    expect(link('HANDOVER_REQUESTED', 'HANDOVER')).toEqual({ commands: ['/my-work'] });
  });

  it('has nothing to open for an announcement or alert without a record', () => {
    expect(link('ANNOUNCEMENT', null, null)).toBeNull();
    expect(link('SYSTEM_ALERT', null, null)).toBeNull();
    expect(link('SYSTEM_ALERT', 'AUDIT_LOG')).toBeNull();
  });
});
