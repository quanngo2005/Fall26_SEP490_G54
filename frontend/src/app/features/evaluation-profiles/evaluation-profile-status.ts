import { EvaluationProfileStatus } from '../../core/evaluation-profile.models';
import { Localization } from '../../core/i18n/i18n.models';

export function evaluationStatusLabel(
  text: Localization['evaluationProfiles'],
  status: EvaluationProfileStatus,
): string {
  const labels: Record<EvaluationProfileStatus, string> = {
    DRAFT: text.statusDraft,
    PENDING_APPROVAL: text.statusPending,
    REJECTED: text.statusRejected,
    ACTIVE: text.statusActive,
    INACTIVE: text.statusInactive,
    ARCHIVED: text.statusArchived,
  };
  return labels[status] ?? status;
}
