import { TestBed } from '@angular/core/testing';
import { EvaluationProfileStatus } from '../../core/evaluation-profile.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { evaluationStatusLabel } from './evaluation-profile-status';

describe('evaluationStatusLabel', () => {
  const statuses: EvaluationProfileStatus[] = [
    'DRAFT',
    'PENDING_APPROVAL',
    'REJECTED',
    'ACTIVE',
    'INACTIVE',
    'ARCHIVED',
  ];

  afterEach(() => localStorage.removeItem('bpms_language'));

  it('gives every status its own non-empty label in Vietnamese and English', () => {
    const i18n = TestBed.inject(I18nService);

    for (const language of ['vi', 'en'] as const) {
      i18n.setLanguage(language);
      const labels = statuses.map((status) =>
        evaluationStatusLabel(i18n.localization.evaluationProfiles, status),
      );

      expect(
        labels.every((label) => label.length > 0 && !statuses.includes(label as never)),
      ).toBeTrue();
      expect(new Set(labels).size).toBe(statuses.length);
    }
  });

  it('follows the current language', () => {
    const i18n = TestBed.inject(I18nService);

    i18n.setLanguage('vi');
    const vi = evaluationStatusLabel(i18n.localization.evaluationProfiles, 'PENDING_APPROVAL');
    i18n.setLanguage('en');
    const en = evaluationStatusLabel(i18n.localization.evaluationProfiles, 'PENDING_APPROVAL');

    expect(vi).not.toBe(en);
  });
});
