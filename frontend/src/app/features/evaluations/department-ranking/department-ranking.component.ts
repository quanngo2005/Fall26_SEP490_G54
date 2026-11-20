import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { EvaluationFormTargetService } from '../../../core/evaluation-form-target.service';
import { EvaluationService } from '../../../core/evaluation.service';
import {
  EvaluationPeriodOption,
  RankingBoardOverview,
  RankingBoardStaffRow,
  RankingTierCode,
  SaveRankingBoardRequest,
} from '../../../core/evaluation.models';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../../shared/layout';
import { LayoutIconComponent } from '../../../shared/layout/layout-icon.component';
import { StatusBadgeComponent, UiSelectComponent } from '../../../shared/ui';
import { saveFile } from '../../performance-dashboard/performance-page';

interface TierTableRow {
  tier: RankingTierCode;
  headcount: number | null;
  actualCount: number;
  isFilled: boolean;
  share: number | null;
}

const TIER_CLASS: Record<RankingTierCode, string> = {
  A1: 'tier-a',
  A2: 'tier-a',
  A3: 'tier-a',
  B: 'tier-b',
  C: 'tier-c',
};

export type ScoreOrder = 'desc' | 'asc';

export type CompositeSort = 'none' | 'scoreChange' | 'sla' | 'criteria' | 'risk';

function compositeSortKey(row: RankingBoardStaffRow, sort: CompositeSort): number | null {
  switch (sort) {
    case 'none':
      return null;
    case 'scoreChange':
      return row.quantitativeScore.change === null ? null : Math.abs(row.quantitativeScore.change);
    case 'sla':
      return row.onTimeSla.value;
    case 'criteria':
      return row.achievedCriteria;
    case 'risk':
      return row.riskEventCount.value;
  }
}

@Component({
  selector: 'app-department-ranking',
  standalone: true,
  imports: [
    CommonModule,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    StatusBadgeComponent,
    UiSelectComponent,
  ],
  templateUrl: './department-ranking.component.html',
  styleUrls: [
    '../../../shared/ui/ui-controls.scss',
    '../../../shared/ui/workspace-page.scss',
    './department-ranking.component.scss',
    '../evaluations.layout.scss',
  ],
})
export class DepartmentRankingComponent extends LocalizedComponent implements OnInit {
  private readonly evaluationService = inject(EvaluationService);
  private readonly formTarget = inject(EvaluationFormTargetService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  get emptyValue(): string {
    return this.localization.evaluations.emptyValue;
  }

  formatNumber(value: number, maxDecimals?: number): string {
    return this.i18n.formatNumber(value, maxDecimals);
  }

  signed(value: number, maxDecimals: number): string {
    const arrow = value > 0 ? '▲' : '▼';
    return `${arrow} ${this.i18n.formatNumber(Math.abs(value), maxDecimals)}`;
  }

  lockNoticeText(): string {
    const board = this.board();
    if (!board) return '';
    if (board.isSentToDirector) {
      return this.localization.evaluations.departmentRanking.noticeSent;
    }
    if (board.isLocked) {
      return this.localization.evaluations.departmentRanking.noticeLocked;
    }
    const [year, month, day] = board.rankingLockDate.split('-');
    return this.i18n.translate('evaluations.departmentRanking.noticeScheduled', {
      date: `${day}/${month}/${year}`,
    });
  }

  lockNoticeTone(): 'sent' | 'locked' | 'pending' {
    const board = this.board();
    if (board?.isSentToDirector) return 'sent';
    return board?.isLocked ? 'locked' : 'pending';
  }

  staffFooterText(): string {
    return this.i18n.translate('evaluations.departmentRanking.staffFooter', {
      total: this.staffRows().length,
    });
  }

  periodTypeLabel(type: string): string {
    const dict = this.localization.evaluations.departmentRanking.periodTypes as Record<
      string,
      string
    >;
    return dict[type] ?? type;
  }

  readonly periods = signal<EvaluationPeriodOption[]>([]);
  readonly selectedPeriodId = signal<string | null>(null);
  readonly selectedComparePeriodId = signal<string | null>(null);
  readonly selectedPeriodType = signal<string>('');

  readonly board = signal<RankingBoardOverview | null>(null);
  readonly loading = signal<boolean>(false);
  readonly saving = signal<boolean>(false);
  readonly exporting = signal<boolean>(false);
  readonly sending = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly toastMessage = signal<string | null>(null);
  readonly toastType = signal<'success' | 'error'>('success');
  readonly confirmSendOpen = signal<boolean>(false);
  readonly confirmCancelOpen = signal<boolean>(false);
  /** True while the manager is calibrating tiers; shows the system tier and reason columns. */
  readonly adjusting = signal<boolean>(false);

  /** Edits made on the page that have not been saved yet, keyed by employee id. */
  private readonly pendingTier = signal<ReadonlyMap<string, RankingTierCode>>(new Map());
  private readonly pendingReason = signal<ReadonlyMap<string, string>>(new Map());

  readonly periodTypes = computed(() => [...new Set(this.periods().map((p) => p.periodType))]);

  readonly periodTypeOptions = computed(() =>
    this.periodTypes().map((type) => ({ value: type, label: this.periodTypeLabel(type) })),
  );

  readonly periodOptions = computed(() =>
    this.filteredPeriods().map((period) => ({ value: period.id, label: period.name })),
  );

  readonly comparePeriodOptions = computed(() => [
    { value: '', label: this.localization.evaluations.departmentRanking.compareNone },
    ...this.comparePeriods().map((period) => ({ value: period.id, label: period.name })),
  ]);

  readonly scoreOrderOptions = computed(() => [
    { value: 'desc', label: this.localization.evaluations.departmentRanking.sortScoreDesc },
    { value: 'asc', label: this.localization.evaluations.departmentRanking.sortScoreAsc },
  ]);

  readonly compositeSortOptions = computed(() => {
    const dict = this.localization.evaluations.departmentRanking;
    return [
      { value: 'none', label: dict.sortComposite },
      { value: 'scoreChange', label: dict.sortScoreChange },
      { value: 'sla', label: dict.sortSla },
      { value: 'criteria', label: dict.sortCriteria },
      { value: 'risk', label: dict.sortRisk },
    ];
  });

  readonly filteredPeriods = computed(() =>
    this.periods().filter((p) => p.periodType === this.selectedPeriodType()),
  );

  readonly comparePeriods = computed(() =>
    this.filteredPeriods().filter((p) => p.id !== this.selectedPeriodId()),
  );

  readonly staffRows = computed<RankingBoardStaffRow[]>(() => this.board()?.staffRows ?? []);

  readonly scoreOrder = signal<ScoreOrder>('desc');
  readonly compositeSort = signal<CompositeSort>('none');
  readonly statusFilter = signal<string>('all');

  readonly statusFilterOptions = computed(() => {
    const dict = this.localization.evaluations.departmentRanking;
    return [
      { value: 'all', label: dict.statusFilterAll },
      { value: 'DRAFT', label: this.localization.evaluationForm.statusDraft },
      { value: 'CALCULATED', label: this.localization.evaluationForm.statusCalculated },
      { value: 'DISPUTED', label: this.localization.evaluationForm.statusDisputed },
    ];
  });

  /** Staff rows in the chosen order; the composite sort wins when set, otherwise the score order applies. */
  readonly displayRows = computed<RankingBoardStaffRow[]>(() => {
    const composite = this.compositeSort();
    const status = this.statusFilter();
    const rows = this.staffRows().filter(
      (row) => status === 'all' || row.evaluationStatus === status,
    );
    if (composite === 'none') {
      const direction = this.scoreOrder() === 'desc' ? -1 : 1;
      return rows.sort((a, b) => {
        const diff = (a.quantitativeScore.value ?? 0) - (b.quantitativeScore.value ?? 0);
        return diff === 0 ? a.rank - b.rank : diff * direction;
      });
    }
    const direction = composite === 'risk' ? 1 : -1;
    return rows.sort((a, b) => {
      const keyA = compositeSortKey(a, composite);
      const keyB = compositeSortKey(b, composite);
      if (keyA === keyB) return a.rank - b.rank;
      if (keyA === null) return 1;
      if (keyB === null) return -1;
      return (keyA - keyB) * direction;
    });
  });

  readonly tierOptions: RankingTierCode[] = ['A1', 'A2', 'A3', 'B', 'C'];

  /** Tier table rows: A1–B from the board, plus C as the remaining eligible employees (no slot). */
  readonly tierTableRows = computed<TierTableRow[]>(() => {
    const total = this.staffRows().length;
    return (this.board()?.tiers ?? []).map((tier) => ({
      tier: tier.tierCode,
      headcount: tier.headcount,
      actualCount: tier.actualCount,
      isFilled: tier.isFilled,
      share: tier.headcount === null || total === 0 ? null : (tier.headcount / total) * 100,
    }));
  });

  readonly totalSlots = computed(() =>
    this.tierTableRows().reduce((sum, row) => sum + (row.headcount ?? 0), 0),
  );

  readonly totalActual = computed(() =>
    this.tierTableRows().reduce((sum, row) => sum + row.actualCount, 0),
  );

  readonly allSlotsFilled = computed(() => {
    const rows = this.tierTableRows();
    return rows.length > 0 && rows.every((row) => row.isFilled);
  });

  readonly lockReached = computed(() => this.board()?.isLocked ?? false);

  readonly canSend = computed(() => this.board()?.canSendToDirector ?? false);

  /** Hạng hệ thống và lý do điều chỉnh chỉ hiện với kỳ đang diễn ra (hôm nay nằm trong khoảng ngày của kỳ). */
  readonly isCurrentPeriod = computed(() => {
    const period = this.periods().find((p) => p.id === this.selectedPeriodId());
    if (!period) return false;
    const today = new Date();
    const todayKey = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
    return period.startDate <= todayKey && todayKey <= period.endDate;
  });

  /** Ranking adjustment is allowed only while the record is CALCULATED or SUBMITTED. */
  canAdjustRow(row: RankingBoardStaffRow): boolean {
    return row.evaluationStatus === 'CALCULATED' || row.evaluationStatus === 'SUBMITTED';
  }

  readonly sendBlockers = computed(() => this.board()?.sendBlockers ?? []);

  ngOnInit(): void {
    this.evaluationService
      .periods()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (periods) => {
          this.periods.set(periods);
          const first = periods[0] ?? null;
          if (!first) return;
          this.selectedPeriodType.set(first.periodType);
          this.selectedPeriodId.set(first.id);
          this.loadBoard(first.id, null);
        },
        error: () =>
          this.errorMessage.set(this.localization.evaluations.departmentRanking.loadError),
      });
  }

  onPeriodTypeSelected(periodType: string): void {
    this.selectedPeriodType.set(periodType);
    const first = this.periods().find((p) => p.periodType === periodType);
    if (first) this.onPeriodSelected(first.id);
  }

  onPeriodValueSelected(periodId: string): void {
    if (periodId) this.onPeriodSelected(periodId);
  }

  onComparePeriodSelected(value: string): void {
    const comparePeriodId = value || null;
    const periodId = this.selectedPeriodId();
    if (!periodId) return;
    this.selectedComparePeriodId.set(comparePeriodId);
    this.loadBoard(periodId, comparePeriodId);
  }

  saveDraft(): void {
    const request = this.buildRequest();
    if (!request || this.saving()) return;
    this.saving.set(true);
    this.evaluationService
      .saveRankingBoardDraft(request)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (board) => {
          this.applyBoard(board);
          this.showToast(this.localization.evaluations.departmentRanking.draftSaved, 'success');
        },
        error: () =>
          this.showToast(this.localization.evaluations.departmentRanking.saveError, 'error'),
      });
  }

  exportExcel(): void {
    const periodId = this.selectedPeriodId();
    const departmentId = this.board()?.departmentId;
    if (!periodId || !departmentId || this.exporting()) return;
    this.exporting.set(true);
    this.evaluationService
      .exportRankingBoard(periodId, departmentId)
      .pipe(
        finalize(() => this.exporting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (blob) => saveFile(blob, `xep-hang-phong-${this.board()?.periodCode ?? 'ky'}.xlsx`),
        error: () =>
          this.showToast(this.localization.evaluations.departmentRanking.exportError, 'error'),
      });
  }

  toggleAdjusting(): void {
    if (this.adjusting()) {
      this.confirmCancelOpen.set(true);
      return;
    }
    const periodId = this.selectedPeriodId();
    const departmentId = this.board()?.departmentId;
    if (!periodId || !departmentId || this.saving()) return;
    this.saving.set(true);
    this.evaluationService
      .autoAssignRankingBoard({ departmentId, periodId })
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (board) => {
          this.applyBoard(board);
          this.adjusting.set(true);
        },
        error: () =>
          this.showToast(this.localization.evaluations.departmentRanking.saveError, 'error'),
      });
  }

  closeConfirmCancel(): void {
    this.confirmCancelOpen.set(false);
  }

  onScoreOrderSelected(value: string): void {
    this.scoreOrder.set(value as ScoreOrder);
    this.compositeSort.set('none');
  }

  onCompositeSortSelected(value: string): void {
    this.compositeSort.set(value as CompositeSort);
  }

  onStatusFilterSelected(value: string): void {
    this.statusFilter.set(value);
  }

  confirmCancelAdjusting(): void {
    this.confirmCancelOpen.set(false);
    this.adjusting.set(false);
    const request = this.buildCancelRequest();
    if (!request || this.saving()) {
      this.pendingTier.set(new Map());
      this.pendingReason.set(new Map());
      return;
    }
    this.saving.set(true);
    this.evaluationService
      .saveRankingBoardDraft(request)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (board) => this.applyBoard(board),
        error: () =>
          this.showToast(this.localization.evaluations.departmentRanking.saveError, 'error'),
      });
  }

  openConfirmSend(): void {
    if (this.canSend()) this.confirmSendOpen.set(true);
  }

  closeConfirmSend(): void {
    this.confirmSendOpen.set(false);
  }

  confirmSend(): void {
    const request = this.buildRequest();
    if (!request || this.sending()) return;
    this.sending.set(true);
    this.evaluationService
      .sendRankingBoardToDirector(request)
      .pipe(
        finalize(() => {
          this.sending.set(false);
          this.confirmSendOpen.set(false);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.showToast(this.localization.evaluations.departmentRanking.sentToast, 'success');
          this.reloadBoard();
        },
        error: (error: { status?: number }) => {
          const message =
            error.status === 409
              ? this.localization.evaluations.departmentRanking.sendBlocked
              : this.localization.evaluations.departmentRanking.sendError;
          this.showToast(message, 'error');
          this.reloadBoard();
        },
      });
  }

  finalTierOf(row: RankingBoardStaffRow): RankingTierCode | null {
    return this.pendingTier().get(row.employeeId) ?? row.finalTier;
  }

  reasonOf(row: RankingBoardStaffRow): string {
    return this.pendingReason().get(row.employeeId) ?? row.adjustmentReason ?? '';
  }

  isTierChanged(row: RankingBoardStaffRow): boolean {
    const final = this.finalTierOf(row);
    return final !== null && final !== row.systemTier;
  }

  isReasonMissing(row: RankingBoardStaffRow): boolean {
    return this.isTierChanged(row) && this.reasonOf(row).trim() === '';
  }

  setFinalTier(row: RankingBoardStaffRow, event: Event): void {
    const tier = (event.target as HTMLSelectElement).value as RankingTierCode;
    this.pendingTier.update((map) => new Map(map).set(row.employeeId, tier));
  }

  setReason(row: RankingBoardStaffRow, event: Event): void {
    const reason = (event.target as HTMLInputElement).value;
    this.pendingReason.update((map) => new Map(map).set(row.employeeId, reason));
  }

  openEmployeeForm(row: RankingBoardStaffRow): void {
    const periodId = this.selectedPeriodId();
    if (!periodId) return;
    this.formTarget.save({ employeeId: row.employeeId, periodId });
    void this.router.navigate(['/evaluations/form']);
  }

  onTimeText(row: RankingBoardStaffRow): string {
    return this.localization.evaluations.departmentRanking.hoverOnTime
      .replace('{onTime}', String(row.onTimeCompletedCount))
      .replace('{total}', String(row.completedWithDeadlineCount));
  }

  criteriaTitleText(row: RankingBoardStaffRow): string {
    return this.localization.evaluations.departmentRanking.hoverCriteria
      .replace('{achieved}', String(row.achievedCriteria ?? this.emptyValue))
      .replace('{total}', String(row.totalCriteria));
  }

  riskTitleText(row: RankingBoardStaffRow): string {
    return this.localization.evaluations.departmentRanking.hoverRiskTitle.replace(
      '{count}',
      String(row.riskEvents.length),
    );
  }

  placePopup(event: MouseEvent, popup: HTMLElement): void {
    const anchor = (event.currentTarget as HTMLElement).getBoundingClientRect();
    popup.style.top = `${anchor.bottom + 6}px`;
    popup.style.left = `${anchor.right}px`;
    popup.style.transform = 'translateX(-100%)';
  }

  deltaTone(delta: number | null, higherIsBetter: boolean): 'good' | 'bad' | null {
    if (delta === null || delta === 0) return null;
    return delta > 0 === higherIsBetter ? 'good' : 'bad';
  }

  tierClass(tier: RankingTierCode | null): string {
    return tier ? TIER_CLASS[tier] : '';
  }

  private onPeriodSelected(periodId: string): void {
    this.selectedPeriodId.set(periodId);
    this.selectedComparePeriodId.set(null);
    this.loadBoard(periodId, null);
  }

  private loadBoard(periodId: string, comparePeriodId: string | null): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.evaluationService
      .rankingBoard(periodId, null, comparePeriodId)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (board) => {
          this.applyBoard(board);
          this.restoreAdjusting(board);
        },
        error: () =>
          this.errorMessage.set(this.localization.evaluations.departmentRanking.loadError),
      });
  }

  private applyBoard(board: RankingBoardOverview): void {
    this.board.set(board);
    this.selectedComparePeriodId.set(board.comparePeriodId);
    this.pendingTier.set(new Map());
    this.pendingReason.set(new Map());
  }

  private restoreAdjusting(board: RankingBoardOverview): void {
    this.adjusting.set(
      board.staffRows.some(
        (row) =>
          (row.finalTier !== null && row.finalTier !== row.systemTier) || !!row.adjustmentReason,
      ),
    );
  }

  private reloadBoard(): void {
    const periodId = this.selectedPeriodId();
    if (periodId) this.loadBoard(periodId, this.selectedComparePeriodId());
  }

  private buildCancelRequest(): SaveRankingBoardRequest | null {
    const board = this.board();
    if (!board) return null;
    const adjustments = board.staffRows
      .filter(
        (row) =>
          row.systemTier !== null && (row.finalTier !== row.systemTier || !!row.adjustmentReason),
      )
      .map((row) => ({
        employeeId: row.employeeId,
        finalTier: row.systemTier as RankingTierCode,
        reason: null,
      }));
    return { departmentId: board.departmentId, periodId: board.periodId, adjustments };
  }

  private buildRequest(): SaveRankingBoardRequest | null {
    const board = this.board();
    if (!board) return null;
    const adjustments = this.staffRows().flatMap((row) => {
      const finalTier = this.finalTierOf(row);
      if (!finalTier) return [];
      return [
        {
          employeeId: row.employeeId,
          finalTier,
          reason: this.isTierChanged(row) ? this.reasonOf(row).trim() || null : null,
        },
      ];
    });
    return { departmentId: board.departmentId, periodId: board.periodId, adjustments };
  }

  private showToast(message: string, type: 'success' | 'error'): void {
    this.toastType.set(type);
    this.toastMessage.set(message);
  }
}
