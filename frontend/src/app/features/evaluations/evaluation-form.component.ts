import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize, Observable, Subscription } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { EvaluationFormTargetService } from '../../core/evaluation-form-target.service';
import { EvaluationService } from '../../core/evaluation.service';
import {
  DepartmentEmployeeEvaluation,
  EvaluationFormModel,
  EvaluationPeriodOption,
  SubmitEvaluationRequest,
} from '../../core/evaluation.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { AppNumberPipe } from '../../shared/pipes';
import { serverMessage } from '../campaigns/campaign.utils';
import { saveFile } from '../performance-dashboard/performance-page';

type LoadFailure = 'offline' | 'forbidden' | 'notFound' | 'error';
type SaveMessage = { kind: 'success' | 'error'; text: string };
type PeriodTypeFilter = 'YEAR' | 'QUARTER' | 'MONTH';

const PERIOD_TYPES: readonly PeriodTypeFilter[] = ['YEAR', 'QUARTER', 'MONTH'];

@Component({
  selector: 'app-evaluation-form',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    AppNumberPipe,
  ],
  templateUrl: './evaluation-form.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    './evaluation-form.component.scss',
    './evaluation-form.layout.scss',
    './evaluations.layout.scss',
  ],
})
export class EvaluationFormComponent extends LocalizedComponent {
  private readonly router = inject(Router);
  private readonly evaluationService = inject(EvaluationService);
  private readonly formTarget = inject(EvaluationFormTargetService);
  private readonly destroyRef = inject(DestroyRef);
  private formRequest?: Subscription;
  private rosterRequest?: Subscription;
  private periodsRequest?: Subscription;
  private exportRequest?: Subscription;
  private submitRequest?: Subscription;
  private rosterKey = '';

  readonly form = signal<EvaluationFormModel | null>(null);
  readonly maxCommentLength = computed(() => this.form()?.formSettings.maxCommentLength ?? null);
  readonly periods = signal<EvaluationPeriodOption[]>([]);
  readonly selectedEmployeeId = signal<string | null>(null);
  readonly selectedPeriodId = signal<string | null>(null);
  readonly selectedComparePeriodId = signal<string | null>(null);
  readonly selectedPeriodType = signal<PeriodTypeFilter>('MONTH');
  readonly periodTypes = PERIOD_TYPES;
  readonly periodOptions = computed<EvaluationPeriodOption[]>(() => {
    const items = this.periods();
    const current = this.form()?.period;
    if (!current || items.some((period) => period.id === current.id)) {
      return items;
    }
    return [
      {
        id: current.id,
        code: current.code,
        name: current.name,
        startDate: current.startDate,
        endDate: current.endDate,
        periodType: current.periodType,
        status: current.status,
        isActive: current.status === 'OPEN',
      },
      ...items,
    ];
  });
  readonly filteredPeriods = computed<EvaluationPeriodOption[]>(() => {
    const type = this.selectedPeriodType();
    return this.periodOptions().filter((period) => period.periodType === type);
  });
  readonly compareOptions = computed<EvaluationPeriodOption[]>(
    () => this.form()?.compareOptions ?? [],
  );
  readonly loading = signal(false);
  readonly failure = signal<LoadFailure | null>(null);
  readonly saving = signal(false);
  readonly exporting = signal(false);
  readonly saveMessage = signal<SaveMessage | null>(null);

  readonly roster = signal<DepartmentEmployeeEvaluation[]>([]);
  readonly commentInput = signal('');

  readonly commentLength = computed(() => this.commentInput().length);

  readonly rosterIndex = computed(() => {
    const employeeId = this.form()?.employee.id;
    return this.roster().findIndex((row) => row.employeeId === employeeId);
  });

  readonly hasRosterNavigation = computed(() => this.rosterIndex() >= 0);

  readonly isDraftStatus = computed(() => this.form()?.currentEvaluation.status === 'DRAFT');

  readonly canSubmit = computed(() => !!this.form()?.canEdit && !this.saving());

  /** The comment differs from what the loaded form holds, so leaving would lose it. */
  readonly hasUnsavedChanges = computed(() => {
    const form = this.form();
    if (!form || !form.canEdit) return false;
    return this.commentInput() !== (form.currentEvaluation.comment ?? '');
  });

  readonly completeBlocked = computed(() => this.commentInput().trim().length === 0);

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
      max: this.maxCommentLength() ?? 0,
    }),
  );

  readonly navIndicatorText = computed(() =>
    this.i18n.translate('evaluationForm.navIndicator', {
      current: this.rosterIndex() + 1,
      total: this.roster().length,
    }),
  );

  readonly weightedPillText = computed(() => {
    const previous = this.form()?.previousPeriod;
    if (!previous || previous.scoreDifference === null)
      return this.localization.evaluations.emptyValue;
    const sign = previous.scoreDifference > 0 ? '+' : previous.scoreDifference < 0 ? '-' : '';
    return `${sign}${this.i18n.formatNumber(Math.abs(previous.scoreDifference), 2)}`;
  });

  readonly criteriaPillText = computed(() => {
    const rate = this.form()?.criteriaSummary.achievedRatePercent ?? null;
    if (rate === null) return this.localization.evaluations.emptyValue;
    return this.i18n.translate('evaluationForm.criteriaPill', {
      percent: this.i18n.formatNumber(rate, 0),
    });
  });

  readonly criteriaValueText = computed(() => {
    const summary = this.form()?.criteriaSummary;
    if (!summary || summary.totalCount === 0) return this.localization.evaluations.emptyValue;
    return `${summary.achievedCount} / ${summary.totalCount}`;
  });

  readonly criteriaFootText = computed(() => {
    const current = this.form();
    if (!current) return '';
    const summary = current.criteriaSummary;
    return this.i18n.translate('evaluationForm.criteriaFoot', {
      achieved: summary.achievedCount,
      near: summary.nearCount,
      below: summary.belowCount,
      nearThreshold: this.i18n.formatNumber(current.formSettings.kpiNearThresholdPercent, 0),
    });
  });

  readonly slaValueText = computed(() => {
    const rate = this.form()?.slaSummary.onTimeRatePercent ?? null;
    if (rate === null) return this.localization.evaluations.emptyValue;
    return `${this.i18n.formatNumber(rate, 1)}%`;
  });

  readonly slaFootText = computed(() =>
    this.i18n.translate('evaluationForm.slaFoot', {
      late: this.form()?.slaSummary.overdueCount ?? 0,
    }),
  );

  readonly riskCountPillText = computed(() =>
    this.i18n.translate('evaluationForm.riskCountPill', {
      count: this.form()?.riskEvents.length ?? 0,
    }),
  );

  readonly pendingPillText = computed(() =>
    this.i18n.translate('evaluationForm.attentionCount', {
      count: this.form()?.pendingSubmissionCount ?? 0,
    }),
  );

  readonly appealPillText = computed(() =>
    this.i18n.translate('evaluationForm.attentionCount', {
      count: this.form()?.openAppealCount ?? 0,
    }),
  );

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

  readonly tierToneClass = computed(() => {
    const tier = this.form()?.summary.proposedTier;
    if (!tier) return '';
    if (tier.startsWith('A')) return 'tier-a';
    return `tier-${tier.toLowerCase()}`;
  });

  constructor() {
    super();
    const target = this.formTarget.read();
    if (!target) {
      this.failure.set('notFound');
      return;
    }
    this.selectedEmployeeId.set(target.employeeId);
    this.selectedPeriodId.set(target.periodId);
    this.loadPeriods();
    this.loadForm(this.evaluationService.form(target.employeeId, target.periodId));
  }

  periodTypeLabel(type: PeriodTypeFilter): string {
    const dict = this.localization.evaluationForm.periodTypes as Record<string, string>;
    return dict[type] ?? type;
  }

  /** Colour follows the configured met/near thresholds supplied by the backend. */
  scoreToneClass(score: number | null): string {
    const settings = this.form()?.formSettings;
    if (score === null || !settings) return '';
    if (score >= settings.kpiMetThresholdPercent) return 'tone-good';
    if (score >= settings.kpiNearThresholdPercent) return 'tone-warn';
    return 'tone-bad';
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
    if (!this.confirmLeave()) return;
    void this.router.navigate(['/evaluations']);
  }

  onPeriodChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    if (!this.switchPeriod(select.value)) {
      select.value = this.selectedPeriodId() ?? '';
    }
  }

  onPeriodTypeChange(event: Event): void {
    const nextType = (event.target as HTMLSelectElement).value as PeriodTypeFilter;
    this.selectedPeriodType.set(nextType);
    const firstMatching = this.periodOptions().find((p) => p.periodType === nextType);
    if (firstMatching) {
      this.switchPeriod(firstMatching.id);
    }
  }

  onCompareChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const compareId = select.value || null;
    const employeeId = this.form()?.employee.id ?? this.selectedEmployeeId();
    const periodId = this.selectedPeriodId();
    if (!employeeId || !periodId || compareId === this.selectedComparePeriodId()) return;
    if (!this.confirmLeave()) {
      select.value = this.selectedComparePeriodId() ?? '';
      return;
    }
    this.exportRequest?.unsubscribe();
    this.exporting.set(false);
    this.selectedComparePeriodId.set(compareId);
    this.loadForm(this.evaluationService.form(employeeId, periodId, compareId));
  }

  onExportExcel(): void {
    const form = this.form();
    if (!form || this.exporting() || this.loading()) return;

    this.exportRequest?.unsubscribe();
    this.exporting.set(true);
    this.saveMessage.set(null);
    this.exportRequest = this.evaluationService
      .exportEvaluationForm(form.employee.id, form.period.id, this.selectedComparePeriodId())
      .pipe(
        finalize(() => this.exporting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (blob) => {
          saveFile(
            blob,
            `danh-gia-nhan-vien-${this.safeFilePart(form.employee.employeeCode)}-${this.safeFilePart(form.period.code)}.xlsx`,
          );
        },
        error: () => {
          this.saveMessage.set({
            kind: 'error',
            text: this.localization.evaluationForm.exportError,
          });
        },
      });
  }

  private safeFilePart(value: string): string {
    return value.replace(/[\\/:*?"<>|]+/g, '-');
  }

  private switchPeriod(nextPeriodId: string | null): boolean {
    const employeeId = this.form()?.employee.id ?? this.selectedEmployeeId();
    if (!nextPeriodId || !employeeId || nextPeriodId === this.selectedPeriodId()) return false;
    if (!this.confirmLeave()) return false;
    this.exportRequest?.unsubscribe();
    this.exporting.set(false);
    this.selectedPeriodId.set(nextPeriodId);
    this.selectedComparePeriodId.set(null);
    this.formTarget.save({ employeeId, periodId: nextPeriodId });
    this.loadForm(this.evaluationService.form(employeeId, nextPeriodId));
    return true;
  }

  /** True when there is nothing to lose or the reviewer agrees to discard the unsaved input. */
  private confirmLeave(): boolean {
    return !this.hasUnsavedChanges() || confirm(this.localization.evaluationForm.confirmDiscard);
  }

  private openRosterEntry(index: number): void {
    const target = this.roster()[index];
    const periodId = this.selectedPeriodId() ?? this.form()?.period.id;
    if (!target || !periodId) return;
    if (!this.confirmLeave()) return;
    // The address stays the same, so the next employee is loaded here instead of by navigating.
    this.exportRequest?.unsubscribe();
    this.exporting.set(false);
    this.selectedEmployeeId.set(target.employeeId);
    this.formTarget.save({ employeeId: target.employeeId, periodId });
    this.loadForm(this.evaluationService.form(target.employeeId, periodId));
  }

  private loadPeriods(): void {
    this.periodsRequest?.unsubscribe();
    this.periodsRequest = this.evaluationService
      .periods()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (items) => this.periods.set(items.filter((period) => period.periodType !== 'DAY')),
        error: () => this.periods.set([]),
      });
  }

  private loadForm(request: Observable<EvaluationFormModel>, keepMessage = false): void {
    this.formRequest?.unsubscribe();
    this.loading.set(true);
    this.failure.set(null);
    if (!keepMessage) this.saveMessage.set(null);
    this.formRequest = request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (form) => {
        this.form.set(form);
        this.selectedEmployeeId.set(form.employee.id);
        this.selectedPeriodId.set(form.period.id);
        this.selectedComparePeriodId.set(form.comparePeriod?.id ?? null);
        this.selectedPeriodType.set(form.period.periodType as PeriodTypeFilter);
        this.commentInput.set(form.currentEvaluation.comment ?? '');
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
    this.rosterRequest = this.evaluationService
      .departmentEmployees(form.period.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
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
      isDraft,
    };

    this.submitRequest?.unsubscribe();
    this.saving.set(true);
    this.saveMessage.set(null);
    this.submitRequest = this.evaluationService
      .submit(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.saveMessage.set({
            kind: 'success',
            text: isDraft
              ? this.localization.evaluationForm.saveDraftSuccess
              : this.localization.evaluationForm.completeSuccess,
          });
          this.loadForm(
            this.evaluationService.form(
              form.employee.id,
              form.period.id,
              this.selectedComparePeriodId(),
            ),
            true,
          );
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
      case 400:
        return serverMessage(error) ?? text.saveError;
      case 403:
        return text.saveForbidden;
      case 409:
        return text.saveConflict;
      default:
        return text.saveError;
    }
  }
}
