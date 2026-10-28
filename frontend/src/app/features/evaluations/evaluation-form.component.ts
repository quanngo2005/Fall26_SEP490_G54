import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { EvaluationService } from '../../core/evaluation.service';
import {
  DepartmentEmployeeEvaluation,
  EvaluationCriterionGroup,
  EvaluationFormModel,
  SubmitEvaluationRequest,
} from '../../core/evaluation.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppNumberPipe } from '../../shared/pipes';

/** Longest comment the form accepts; the backend does not limit it, so the form does. */
const COMMENT_MAX_LENGTH = 1000;
/** A qualitative score is a percentage of the group, so it runs from 0 to the percent scale. */
const SCORE_MIN = 0;
const SCORE_MAX = 100;

type LoadFailure = 'offline' | 'forbidden' | 'notFound' | 'error';
type SaveMessage = { kind: 'success' | 'error'; text: string };

@Component({
  selector: 'app-evaluation-form',
  standalone: true,
  imports: [CommonModule, FormsModule, HeaderComponent, SidebarComponent, AppNumberPipe],
  templateUrl: './evaluation-form.component.html',
  styleUrls: ['./evaluation-form.component.scss', './evaluation-form.layout.scss'],
})
export class EvaluationFormComponent extends LocalizedComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly evaluationService = inject(EvaluationService);
  private formRequest?: Subscription;
  private rosterRequest?: Subscription;
  private rosterKey = '';

  readonly maxCommentLength = COMMENT_MAX_LENGTH;
  readonly scoreMin = SCORE_MIN;
  readonly scoreMax = SCORE_MAX;

  readonly form = signal<EvaluationFormModel | null>(null);
  readonly loading = signal(false);
  readonly failure = signal<LoadFailure | null>(null);
  readonly saving = signal(false);
  readonly saveMessage = signal<SaveMessage | null>(null);

  readonly roster = signal<DepartmentEmployeeEvaluation[]>([]);
  readonly commentInput = signal('');
  readonly qualitativeScores = signal<Record<string, number | null>>({});

  readonly qualitativeGroups = computed<EvaluationCriterionGroup[]>(() =>
    (this.form()?.criteriaGroups ?? []).filter((group) => group.isQualitative),
  );

  readonly commentLength = computed(() => this.commentInput().length);

  readonly qualitativeAverage = computed<number | null>(() => {
    const scores = this.qualitativeGroups()
      .map((group) => this.qualitativeScores()[group.kpiGroupId])
      .filter((score): score is number => typeof score === 'number');
    return scores.length ? scores.reduce((sum, score) => sum + score, 0) / scores.length : null;
  });

  readonly rosterIndex = computed(() => {
    const employeeId = this.form()?.employee.id;
    return this.roster().findIndex((row) => row.employeeId === employeeId);
  });

  readonly hasRosterNavigation = computed(() => this.rosterIndex() >= 0);

  readonly isDraftStatus = computed(() => this.form()?.currentEvaluation.status === 'DRAFT');

  /** Out-of-range or non-numeric scores block both actions; a missing score or comment only blocks completion. */
  readonly scoreInvalid = computed(() =>
    this.qualitativeGroups().some((group) => {
      const score = this.qualitativeScores()[group.kpiGroupId];
      return score !== null && score !== undefined && !this.isValidScore(score);
    }),
  );

  readonly completeBlocked = computed(
    () =>
      this.commentInput().trim().length === 0 ||
      this.qualitativeGroups().some((group) => this.qualitativeScores()[group.kpiGroupId] == null),
  );

  readonly canSubmit = computed(
    () => !!this.form()?.canEdit && !this.saving() && !this.scoreInvalid(),
  );

  readonly stateText = computed(() => {
    const text = this.localization.evaluationForm;
    if (this.loading()) return text.loading;
    switch (this.failure()) {
      case 'offline':
        return text.offline;
      case 'forbidden':
        return text.forbidden;
      case 'notFound':
        return text.notFound;
      case 'error':
        return text.loadError;
      default:
        return '';
    }
  });

  readonly charCounterText = computed(() =>
    this.i18n.translate('evaluationForm.charCounter', {
      current: this.commentLength(),
      max: this.maxCommentLength,
    }),
  );

  readonly scoreInvalidText = computed(() =>
    this.i18n.translate('evaluationForm.scoreInvalid', { min: this.scoreMin, max: this.scoreMax }),
  );

  readonly navIndicatorText = computed(() =>
    this.i18n.translate('evaluationForm.navIndicator', {
      current: this.rosterIndex() + 1,
      total: this.roster().length,
    }),
  );

  readonly pendingNotesText = computed(() =>
    this.i18n.translate('evaluationForm.itemPendingWorkTypes', {
      count: this.form()?.pendingSubmissionCount ?? 0,
    }),
  );

  readonly riskPenaltyNotesText = computed(() =>
    this.i18n.translate('evaluationForm.itemRiskPenalty', {
      count: this.form()?.riskEvents.length ?? 0,
      points: this.i18n.formatNumber(this.form()?.summary.totalRiskPenalty ?? 0, 2),
    }),
  );

  readonly appealsNotesText = computed(() =>
    this.i18n.translate('evaluationForm.itemAppeals', {
      open: this.form()?.openAppealCount ?? 0,
    }),
  );

  readonly comparisonText = computed(() => {
    const previous = this.form()?.previousPeriod;
    if (!previous) return this.localization.evaluations.emptyValue;
    const key =
      previous.scoreDifference > 0
        ? 'comparisonHigher'
        : previous.scoreDifference < 0
          ? 'comparisonLower'
          : 'comparisonEqual';
    return this.i18n.translate(`evaluationForm.${key}`, {
      points: this.i18n.formatNumber(Math.abs(previous.scoreDifference), 2),
    });
  });

  readonly prevPeriodTitleText = computed(() => {
    const previous = this.form()?.previousPeriod;
    return previous
      ? this.i18n.translate('evaluationForm.prevPeriodTitle', { period: previous.periodCode })
      : this.localization.evaluationForm.prevPeriodNone;
  });

  readonly quantitativeMetaText = computed(() => {
    const form = this.form();
    return form
      ? this.i18n.translate('evaluationForm.quantitativeMeta', {
          profile: form.profile.name,
          period: form.period.code,
        })
      : '';
  });

  readonly tierText = computed(() => {
    const tier = this.form()?.summary.proposedTier;
    return tier
      ? this.i18n.translate('evaluationForm.proposedTier', { tier })
      : this.localization.evaluations.emptyValue;
  });

  constructor() {
    super();
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const employeeId = params.get('employeeId');
      const periodId = params.get('periodId');
      if (!employeeId || !periodId) {
        this.form.set(null);
        this.failure.set('notFound');
        return;
      }
      this.loadForm(employeeId, periodId);
    });
  }

  isValidScore(score: number): boolean {
    return Number.isFinite(score) && score >= SCORE_MIN && score <= SCORE_MAX;
  }

  setQualitativeScore(kpiGroupId: string, value: number | null): void {
    this.qualitativeScores.update((scores) => ({ ...scores, [kpiGroupId]: value }));
  }

  onPrev(): void {
    this.openRosterEntry(this.rosterIndex() - 1);
  }

  onNext(): void {
    this.openRosterEntry(this.rosterIndex() + 1);
  }

  onSaveDraft(): void {
    this.submit(true);
  }

  onCompleteEvaluation(): void {
    if (this.completeBlocked()) {
      this.saveMessage.set({
        kind: 'error',
        text: this.localization.evaluationForm.completeRequirements,
      });
      return;
    }
    this.submit(false);
  }

  onBackToList(): void {
    void this.router.navigate(['/evaluations']);
  }

  private openRosterEntry(index: number): void {
    const target = this.roster()[index];
    const periodId = this.form()?.period.id;
    if (!target || !periodId) return;
    void this.router.navigate(['/evaluations/form'], {
      queryParams: { employeeId: target.employeeId, periodId },
    });
  }

  private loadForm(employeeId: string, periodId: string, keepMessage = false): void {
    this.formRequest?.unsubscribe();
    this.loading.set(true);
    this.failure.set(null);
    if (!keepMessage) this.saveMessage.set(null);
    this.formRequest = this.evaluationService.form(employeeId, periodId).subscribe({
      next: (form) => {
        this.form.set(form);
        this.applyInputs(form);
        this.loading.set(false);
        this.loadRoster(form);
      },
      error: (error: unknown) => {
        const status = error instanceof HttpErrorResponse ? error.status : null;
        this.form.set(null);
        this.failure.set(
          status === 0
            ? 'offline'
            : status === 403
              ? 'forbidden'
              : status === 404
                ? 'notFound'
                : 'error',
        );
        this.loading.set(false);
      },
    });
  }

  private applyInputs(form: EvaluationFormModel): void {
    this.commentInput.set(form.currentEvaluation.comment ?? '');
    const scores: Record<string, number | null> = {};
    for (const group of form.criteriaGroups) {
      if (group.isQualitative) scores[group.kpiGroupId] = group.qualitativeScore ?? null;
    }
    this.qualitativeScores.set(scores);
  }

  /**
   * Previous/Next walk the same roster as the evaluation list: the caller's own department and everything beneath it.
   * No departmentId is sent, so the backend defaults to the caller's department; the roster stays empty when the
   * caller may not read it.
   */
  private loadRoster(form: EvaluationFormModel): void {
    const key = form.period.id;
    if (key === this.rosterKey) return;
    this.rosterKey = key;
    this.rosterRequest?.unsubscribe();
    this.roster.set([]);
    this.rosterRequest = this.evaluationService.departmentEmployees(form.period.id).subscribe({
      next: (rows) => this.roster.set(rows),
      error: () => this.roster.set([]),
    });
  }

  private submit(isDraft: boolean): void {
    const form = this.form();
    if (!form || !this.canSubmit()) return;

    const request: SubmitEvaluationRequest = {
      employeeId: form.employee.id,
      periodId: form.period.id,
      comment: this.commentInput().trim() || null,
      coachingPlan: form.currentEvaluation.coachingPlan,
      coachingRequired: form.currentEvaluation.coachingRequired,
      qualitativeScores: this.qualitativeGroups()
        .filter((group) => typeof this.qualitativeScores()[group.kpiGroupId] === 'number')
        .map((group) => ({
          kpiGroupId: group.kpiGroupId,
          score: this.qualitativeScores()[group.kpiGroupId] as number,
          notes: group.qualitativeNotes,
        })),
      isDraft,
    };

    this.saving.set(true);
    this.saveMessage.set(null);
    this.evaluationService.submit(request).subscribe({
      next: () => {
        this.saving.set(false);
        this.saveMessage.set({
          kind: 'success',
          text: isDraft
            ? this.localization.evaluationForm.saveDraftSuccess
            : this.localization.evaluationForm.completeSuccess,
        });
        this.loadForm(form.employee.id, form.period.id, true);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.saveMessage.set({ kind: 'error', text: this.saveErrorText(error) });
      },
    });
  }

  private saveErrorText(error: unknown): string {
    const text = this.localization.evaluationForm;
    const status = error instanceof HttpErrorResponse ? error.status : null;
    switch (status) {
      case 0:
        return text.offline;
      case 403:
        return text.saveForbidden;
      case 409:
        return text.saveConflict;
      default:
        return text.saveError;
    }
  }
}
