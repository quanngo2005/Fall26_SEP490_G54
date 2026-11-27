import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { forkJoin, of, switchMap } from 'rxjs';
import { EvaluationService } from '../../../core/evaluation.service';
import { EvaluationFormTargetService } from '../../../core/evaluation-form-target.service';
import { KpiDefinitionService } from '../../../core/kpi-definition.service';
import { KpiDefinition } from '../../../core/kpi-definition.models';
import {
  BranchApprovalOverview,
  CalibrationEmployeeRow,
  CalibrationOverview,
  DepartmentApprovalSummary,
  EvaluationPeriodOption,
  FocusKpiSummary,
  RankingSlotQuotaItem,
  RankingTierCode,
  SubmitCalibrationRequest,
} from '../../../core/evaluation.models';
import { I18nService } from '../../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../../shared/layout';
import { LayoutIconComponent } from '../../../shared/layout/layout-icon.component';
import {
  ComparePeriodMode,
  CompareSliderComponent,
  SelectOption,
  StatusBadgeComponent,
  UiSelectComponent,
} from '../../../shared/ui';
import { findPreviousPeriod, findSameLastYearPeriod } from '../../../core/period-options';
import {
  DepartmentRankingTierTableComponent,
  RankingBoardTierRow,
} from '../department-ranking/department-ranking-tier-table/department-ranking-tier-table.component';

export interface PositionTabItem {
  id: string | null;
  title: string;
  count: number;
  hiddenCount: number;
}

import { RouteStateService } from '../../../core/route-state.service';
@Component({
  selector: 'app-department-ranking-approval',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    StatusBadgeComponent,
    UiSelectComponent,
    CompareSliderComponent,
    DepartmentRankingTierTableComponent,
  ],
  templateUrl: './department-ranking-approval.component.html',
  styleUrls: [
    '../../../shared/ui/ui-controls.scss',
    '../../../shared/ui/workspace-page.scss',
    '../department-ranking/department-ranking.component.scss',
    '../department-ranking/department-ranking.popup.scss',
    '../evaluations.layout.scss',
    './department-ranking-approval.component.scss',
  ],
})
export class DepartmentRankingApprovalComponent implements OnInit {
  private readonly evaluationService = inject(EvaluationService);
  private readonly formTarget = inject(EvaluationFormTargetService);
  private readonly kpiDefinitionService = inject(KpiDefinitionService);
  private readonly i18n = inject(I18nService);
  private readonly routeState = inject(RouteStateService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  periodId = signal<string>('');
  selectedPeriodType = signal<string>('MONTH');
  selectedComparePeriodId = signal<string | null>(null);
  periods = signal<EvaluationPeriodOption[]>([]);

  selectedDepartmentId = signal<string | null>(null);
  selectedPositionId = signal<string | null>(null);

  branchOverview = signal<BranchApprovalOverview | null>(null);
  calibrationData = signal<CalibrationOverview | null>(null);
  departmentQuotas = signal<RankingSlotQuotaItem[]>([]);
  savingQuota = signal<boolean>(false);

  // View Mode: 'all' (tất cả phòng - mặc định) vs 'single' (từng phòng)
  viewMode = signal<'single' | 'all'>('all');
  expandedDeptIds = signal<Set<string>>(new Set<string>());
  deptCalibrations = signal<Map<string, CalibrationOverview>>(new Map());
  loadingDeptCalibrations = signal<Set<string>>(new Set<string>());

  // Period Total Quota (Quota tổng của kỳ)
  periodQuota = signal<RankingSlotQuotaItem[]>([]);
  periodQuotaDraft = signal<Record<string, number | null>>({
    A1: null,
    A2: null,
    A3: null,
    B: null,
    C: null,
  });
  isEditingPeriodQuota = signal<boolean>(false);
  savingPeriodQuota = signal<boolean>(false);

  // Focus KPIs (KPI trọng tâm của kỳ)
  focusKpis = signal<FocusKpiSummary[]>([]);
  displayedFocusKpis = computed<FocusKpiSummary[]>(() =>
    this.viewMode() === 'single'
      ? (this.calibrationData()?.departmentFocusKpis ?? [])
      : this.focusKpis(),
  );
  availableKpis = signal<KpiDefinition[]>([]);
  isEditingFocusKpis = signal<boolean>(false);
  focusKpiDraftIds = signal<string[]>([]);
  savingFocusKpis = signal<boolean>(false);

  currentPeriod = computed<EvaluationPeriodOption | undefined>(() => {
    const pid = this.periodId();
    return this.periods().find((p) => p.id === pid);
  });

  isPeriodLocked = computed<boolean>(() => {
    const period = this.currentPeriod();
    if (!period) return false;
    if (period.status === 'LOCKED' || period.status === 'CLOSED') {
      return true;
    }
    if (period.endDate) {
      const todayStr = new Date().toISOString().slice(0, 10);
      return period.endDate < todayStr;
    }
    return false;
  });

  loadingOverview = signal<boolean>(false);
  loadingCalibration = signal<boolean>(false);
  errorMessage = signal<string | null>(null);
  toastMessage = signal<string | null>(null);
  toastType = signal<'success' | 'error'>('success');
  private toastTimer?: ReturnType<typeof setTimeout>;

  // Reject modal
  isRejectModalOpen = signal<boolean>(false);
  rejectReason = signal<string>('');
  rejectError = signal<string | null>(null);
  rejectSubmitting = signal<boolean>(false);

  // Approve modal
  isApproveModalOpen = signal<boolean>(false);
  approveNote = signal<string>('');
  approveSubmitting = signal<boolean>(false);

  // Finalize / Publish modal
  isLockModalOpen = signal<boolean>(false);
  lockSignatureToken = signal<string>('');
  lockApprovalComments = signal<string>('');
  lockError = signal<string | null>(null);
  lockSubmitting = signal<boolean>(false);

  // Branch-wide BM adjustment & anchored Duyệt popover
  readonly tierChoices: RankingTierCode[] = ['A1', 'A2', 'A3', 'B', 'C'];
  isAdjustingBranch = signal<boolean>(false);
  branchDraftTier = signal<Map<string, RankingTierCode>>(new Map());
  branchDraftReason = signal<Map<string, string>>(new Map());
  savingBranchDraft = signal<boolean>(false);
  approvePopoverOpen = signal<boolean>(false);

  get localization() {
    return this.i18n.localization;
  }

  protected readonly t = computed(() => {
    return this.i18n.localization.evaluations.departmentApproval;
  });

  headerTitle = computed<string>(() => {
    return (
      this.branchOverview()?.period?.name || this.t()['pageTitle'] || 'Phê duyệt xếp hạng chi nhánh'
    );
  });

  protected readonly tLock = computed(() => {
    return this.i18n.localization.evaluations.branchPeriods;
  });

  readonly periodTypes = computed(() => [...new Set(this.periods().map((p) => p.periodType))]);

  periodTypeLabel(type: string): string {
    const dict = (this.localization.evaluations.departmentRanking.periodTypes ?? {}) as Record<
      string,
      string
    >;
    return dict[type] ?? type;
  }

  readonly periodTypeOptions = computed<SelectOption[]>(() =>
    this.periodTypes().map((type) => ({
      value: type,
      label: this.periodTypeLabel(type),
    })),
  );

  readonly filteredPeriods = computed(() =>
    this.periods().filter((p) => p.periodType === this.selectedPeriodType()),
  );

  readonly periodOptions = computed<SelectOption[]>(() =>
    this.filteredPeriods().map((p) => ({ value: p.id, label: p.name })),
  );

  readonly compareMode = signal<ComparePeriodMode>('PREVIOUS');

  readonly previousPeriod = computed(() =>
    findPreviousPeriod(this.currentPeriod(), this.comparePeriods()),
  );

  readonly sameLastYearPeriod = computed(() =>
    findSameLastYearPeriod(this.currentPeriod(), this.periods()),
  );

  readonly hasPreviousPeriod = computed(() => !!this.previousPeriod());
  readonly hasSameLastYearPeriod = computed(() => !!this.sameLastYearPeriod());

  readonly comparePeriods = computed(() =>
    this.filteredPeriods().filter((p) => p.id !== this.periodId()),
  );

  readonly comparePeriodOptions = computed<SelectOption[]>(() => [
    {
      value: '',
      label: this.localization.evaluations.departmentRanking.compareNone,
    },
    ...this.comparePeriods().map((period) => ({
      value: period.id,
      label: period.name,
    })),
  ]);

  selectedDepartment = computed<DepartmentApprovalSummary | null>(() => {
    const deptId = this.selectedDepartmentId();
    const depts = this.branchOverview()?.departments ?? [];
    return depts.find((d) => d.departmentId === deptId) ?? null;
  });

  departmentSelectOptions = computed<SelectOption[]>(() => {
    return (this.branchOverview()?.departments ?? []).map((d) => {
      const statusLabel =
        d.status === 'APPROVED' || d.status === 'FINAL'
          ? ` (${this.t()['statusApproved'] || 'Đã duyệt'})`
          : d.status === 'IN_REVIEW'
            ? ` (${this.t()['statusPending'] || 'Chờ duyệt'})`
            : ` (${this.t()['statusNotSubmitted'] || 'Chưa gửi'})`;
      return {
        value: d.departmentId,
        label: `${d.departmentCode} · ${d.departmentName}${statusLabel}`,
      };
    });
  });

  tierTableRows = computed<RankingBoardTierRow[]>(() => {
    const slots = this.departmentQuotas();
    const roster = this.calibrationData()?.rankedRoster ?? [];
    const total = roster.length;
    const isSubmitted = this.isDepartmentSubmitted();

    const tierCodes = ['A1', 'A2', 'A3', 'B', 'C'];
    return tierCodes.map((tier) => {
      const slot = slots.find((s) => s.tierCode === tier);
      const headcount = slot ? slot.headcount : null;
      const actualCount = isSubmitted ? roster.filter((r) => r.calibratedTier === tier).length : 0;
      const share = headcount != null && total > 0 ? (headcount / total) * 100 : null;

      const isTierA = tier.startsWith('A');
      const isFilled = isTierA ? (headcount != null ? actualCount <= headcount : true) : true;

      const periodQuotaMax = this.getPeriodQuotaHeadcount(tier);
      const isOverPeriodQuota =
        headcount != null && periodQuotaMax != null && headcount > periodQuotaMax;

      return {
        tier,
        share,
        headcount,
        actualCount,
        isFilled,
        periodQuotaMax,
        isOverPeriodQuota,
        isFlex: false,
      };
    });
  });

  totalQuotaSlots = computed<number | string>(() => {
    const slots = this.departmentQuotas();
    const total = slots.reduce((sum, s) => sum + (s.headcount ?? 0), 0);
    return total > 0 ? total : '-';
  });

  totalQuotaActual = computed<number | string>(() => {
    return this.calibrationData()?.rankedRoster?.length ?? '-';
  });

  allSlotsFilled = computed<boolean>(() => {
    const rows = this.tierTableRows();
    return rows.length > 0 && rows.filter((r) => !r.isFlex).every((r) => r.isFilled);
  });

  tierBlockers = computed<string[]>(() => {
    const q = this.calibrationData()?.quota;
    if (!q) return [];
    if (!q.isCompliant && q.complianceNote) {
      return [q.complianceNote];
    }
    return [];
  });

  positionTabs = computed<PositionTabItem[]>(() => {
    const roster = this.calibrationData()?.rankedRoster ?? [];
    const hidden = this.calibrationData()?.disputedPendingCount ?? 0;

    const map = new Map<string, { title: string; count: number }>();
    for (const emp of roster) {
      if (emp.jobPositionId) {
        const cur = map.get(emp.jobPositionId);
        if (cur) {
          cur.count++;
        } else {
          map.set(emp.jobPositionId, {
            title: emp.jobPositionTitle || 'Khác',
            count: 1,
          });
        }
      }
    }

    if (map.size <= 1) return [];

    const tabs: PositionTabItem[] = [
      {
        id: null,
        title: this.t()['allPositions'] || 'Tất cả vị trí',
        count: roster.length,
        hiddenCount: hidden,
      },
    ];
    for (const [id, val] of map.entries()) {
      tabs.push({
        id,
        title: val.title,
        count: val.count,
        hiddenCount: 0,
      });
    }

    return tabs;
  });

  filteredRoster = computed<CalibrationEmployeeRow[]>(() => {
    const roster = this.calibrationData()?.rankedRoster ?? [];
    const posId = this.selectedPositionId();
    if (!posId) return roster;
    return roster.filter((r) => r.jobPositionId === posId);
  });

  canApproveCurrentDept = computed<boolean>(() => {
    const dept = this.selectedDepartment();
    return dept != null && dept.status === 'IN_REVIEW';
  });

  isDepartmentSubmitted = computed<boolean>(() => {
    const dept = this.selectedDepartment();
    return (
      dept != null &&
      (dept.status === 'IN_REVIEW' || dept.status === 'APPROVED' || dept.status === 'FINAL')
    );
  });

  canRejectCurrentDept = computed<boolean>(() => {
    const dept = this.selectedDepartment();
    return dept != null && dept.status === 'IN_REVIEW';
  });

  canPublish = computed<boolean>(() => {
    const ov = this.branchOverview();
    return ov != null && ov.canFinalize;
  });

  publishHintText = computed<string>(() => {
    const depts = this.branchOverview()?.departments ?? [];
    const approved = depts.filter((d) => d.status === 'APPROVED' || d.status === 'FINAL').length;
    const total = depts.length;
    return (this.t()['publishHint'] || 'Mở khi {approved}/{total} phòng đã duyệt và kiểm tra đạt.')
      .replace('{approved}', String(approved))
      .replace('{total}', String(total));
  });

  isBranchFinalized = computed<boolean>(() => {
    if (this.isPeriodLocked()) return true;
    const depts = this.branchOverview()?.departments ?? [];
    return depts.length > 0 && depts.every((d) => d.status === 'FINAL');
  });

  hasSubmittedDepartments = computed<boolean>(() => {
    const depts = this.branchOverview()?.departments ?? [];
    return depts.some((d) => this.isDeptSubmitted(d.status));
  });

  hasPendingBranchChanges = computed<boolean>(() => {
    return this.branchDraftTier().size > 0 || this.branchDraftReason().size > 0;
  });

  branchFinalizeWarnings = computed<string[]>(() => {
    const warnings: string[] = [];
    const ov = this.branchOverview();
    if (!ov) return warnings;

    const depts = ov.departments ?? [];
    const unsubmitted = depts.filter((d) => !this.isDeptSubmitted(d.status));
    if (unsubmitted.length > 0) {
      const names = unsubmitted.map((d) => d.departmentName).join(', ');
      warnings.push(`Còn ${unsubmitted.length} phòng ban chưa gửi xếp hạng: ${names}.`);
    }

    if ((ov.disputedPendingCount ?? 0) > 0) {
      warnings.push(`Còn ${ov.disputedPendingCount} khiếu nại điểm đang chờ xử lý.`);
    }

    if (this.periodQuota().length === 0) {
      warnings.push('Chưa thiết lập Tổng Quota của kỳ.');
    } else {
      const ratioErr = this.periodQuotaRatioError();
      if (ratioErr) {
        warnings.push(ratioErr);
      }
    }

    const totalActualA =
      this.getBranchActualTierCount('A1') +
      this.getBranchActualTierCount('A2') +
      this.getBranchActualTierCount('A3');
    const maxA = this.recommendedTierASlots();
    if (maxA > 0 && totalActualA > maxA) {
      warnings.push(
        `Tổng hạng A thực tế (${totalActualA}) vượt trần ${this.tierAPercent()}% chi nhánh (tối đa ${maxA} slot).`,
      );
    }

    for (const tier of this.tierChoices) {
      const periodMax = this.getPeriodQuotaHeadcount(tier);
      const actual = this.getBranchActualTierCount(tier);
      if (periodMax != null && actual > periodMax) {
        warnings.push(
          `Hạng ${tier}: ${actual} nhân sự vượt Tổng Quota của kỳ (${periodMax} slot).`,
        );
      }
    }

    const missingReasonCount = this.countBranchMissingReasons();
    if (missingReasonCount > 0) {
      warnings.push(`Có ${missingReasonCount} nhân sự đã điều chỉnh hạng nhưng chưa nhập lý do.`);
    }

    if (warnings.length === 0 && !ov.canFinalize) {
      warnings.push('Chưa đủ điều kiện chốt toàn bộ kỳ đánh giá.');
    }

    return warnings;
  });

  totalBranchEmployees = computed<number>(() => {
    return this.branchOverview()?.totalEmployees ?? 0;
  });

  tierAPercent = computed<number>(() => this.branchOverview()?.branchCapPercent ?? 20);
  tierBPercent = computed<number>(() => this.branchOverview()?.tierBTargetPercent ?? 60);
  tierCPercent = computed<number>(() => this.branchOverview()?.tierCTargetPercent ?? 15);

  recommendedTierASlots = computed<number>(() => {
    const total = this.totalBranchEmployees();
    return total > 0 ? Math.max(1, Math.ceil((total * this.tierAPercent()) / 100)) : 0;
  });

  recommendedTierBSlots = computed<number>(() => {
    const total = this.totalBranchEmployees();
    return total > 0 ? Math.ceil((total * this.tierBPercent()) / 100) : 0;
  });

  recommendedTierCSlots = computed<number>(() => {
    const total = this.totalBranchEmployees();
    return total > 0 ? Math.ceil((total * this.tierCPercent()) / 100) : 0;
  });

  totalBranchTierBSlots = computed<number>(() => {
    if (this.isEditingPeriodQuota()) {
      return this.periodQuotaDraft()['B'] ?? 0;
    }
    return this.periodQuota().find((s) => s.tierCode === 'B')?.headcount ?? 0;
  });

  totalBranchTierCSlots = computed<number>(() => {
    if (this.isEditingPeriodQuota()) {
      return this.periodQuotaDraft()['C'] ?? 0;
    }
    return this.periodQuota().find((s) => s.tierCode === 'C')?.headcount ?? 0;
  });

  isPeriodTierAOverCap = computed<boolean>(() => {
    const total = this.totalBranchEmployees();
    return total > 0 && this.totalBranchTierASlots() > this.recommendedTierASlots();
  });

  periodQuotaRatioError = computed<string | null>(() => {
    const total = this.totalBranchEmployees();
    if (total <= 0) return null;

    if (this.totalBranchTierASlots() > this.recommendedTierASlots()) {
      return `Tổng hạng A (${this.totalBranchTierASlots()}) vượt tỉ lệ ${this.tierAPercent()}% (tối đa ${this.recommendedTierASlots()} slot).`;
    }
    if (this.totalBranchTierBSlots() > this.recommendedTierBSlots()) {
      return `Hạng B (${this.totalBranchTierBSlots()}) vượt tỉ lệ ${this.tierBPercent()}% (tối đa ${this.recommendedTierBSlots()} slot).`;
    }
    if (this.totalBranchTierCSlots() > this.recommendedTierCSlots()) {
      return `Hạng C (${this.totalBranchTierCSlots()}) vượt tỉ lệ ${this.tierCPercent()}% (tối đa ${this.recommendedTierCSlots()} slot).`;
    }
    if (this.totalBranchQuotaSlots() > total) {
      return `Tổng quota A/B/C (${this.totalBranchQuotaSlots()}) vượt tổng nhân viên (${total} NV).`;
    }
    return null;
  });

  totalBranchTierASlots = computed<number>(() => {
    if (this.isEditingPeriodQuota()) {
      const draft = this.periodQuotaDraft();
      return ['A1', 'A2', 'A3'].reduce((sum, tier) => sum + (draft[tier] ?? 0), 0);
    }
    return this.periodQuota()
      .filter((s) => s.tierCode.startsWith('A'))
      .reduce((sum, s) => sum + (s.headcount ?? 0), 0);
  });

  totalBranchQuotaSlots = computed<number>(() => {
    if (this.isEditingPeriodQuota()) {
      const draft = this.periodQuotaDraft();
      return ['A1', 'A2', 'A3', 'B', 'C'].reduce((sum, tier) => sum + (draft[tier] ?? 0), 0);
    }
    return this.periodQuota().reduce((sum, s) => sum + (s.headcount ?? 0), 0);
  });

  totalDeptQuotaHeadcount = computed<number>(() => {
    return this.departmentQuotas().reduce((sum, s) => sum + (s.headcount ?? 0), 0);
  });

  selectedDeptEmployeesCount = computed<number>(() => {
    return (
      this.selectedDepartment()?.totalEmployees ?? this.calibrationData()?.rankedRoster?.length ?? 0
    );
  });

  deptQuotaTierExceededErrors = computed<string[]>(() => {
    const errors: string[] = [];
    const rows = this.tierTableRows();
    for (const r of rows) {
      if (r.isOverPeriodQuota && r.headcount != null && r.periodQuotaMax != null) {
        errors.push(
          `Hạng ${r.tier}: ${r.headcount} slot vượt trần Quota tổng (${r.periodQuotaMax} slot).`,
        );
      }
    }
    return errors;
  });

  isDeptQuotaExceeded = computed<boolean>(() => {
    return this.deptQuotaTierExceededErrors().length > 0;
  });

  deptQuotaIndicatorState = computed<'balanced' | 'under' | 'over'>(() => {
    if (this.isDeptQuotaExceeded()) return 'over';
    return 'balanced';
  });

  onPeriodTypeSelected(type: string): void {
    this.selectedPeriodType.set(type);
    const first = this.periods().find((p) => p.periodType === type);
    if (first) {
      this.onPeriodValueSelected(first.id);
    }
  }

  onPeriodValueSelected(newPeriodId: string): void {
    if (!newPeriodId || newPeriodId === this.periodId()) return;
    this.isEditingPeriodQuota.set(false);
    this.periodId.set(newPeriodId);
    this.selectedComparePeriodId.set(null);
    void this.router.navigate(['/branch-performance/evaluations/approval'], {
      state: { periodId: newPeriodId },
    });
    this.loadBranchOverview();
  }

  onComparePeriodSelected(comparePeriodId: string): void {
    this.selectedComparePeriodId.set(comparePeriodId || null);
  }

  onCompareModeChanged(mode: ComparePeriodMode): void {
    this.compareMode.set(mode);
    const target = mode === 'SAME_LAST_YEAR' ? this.sameLastYearPeriod() : this.previousPeriod();
    this.selectedComparePeriodId.set(target?.id ?? null);
  }

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParamMap;
    const qViewMode = qp.get('viewMode');
    const qDeptId = qp.get('departmentId');
    const qPeriodId = qp.get('periodId');
    if (qDeptId) {
      this.selectedDepartmentId.set(qDeptId);
    }
    if (qViewMode === 'single' || qViewMode === 'all') {
      this.viewMode.set(qViewMode);
    } else if (qDeptId) {
      this.viewMode.set('single');
    }
    if (qPeriodId) {
      this.periodId.set(qPeriodId);
    }

    this.evaluationService
      .periods()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (periods) => {
          this.periods.set(periods);
          const pid = this.periodId();
          if (pid) {
            const match = periods.find((p) => p.id === pid);
            if (match) {
              this.selectedPeriodType.set(match.periodType);
            }
            this.loadBranchOverview();
          } else if (periods.length > 0) {
            const first = periods.find((p) => p.status === 'OPEN') ?? periods[0];
            this.periodId.set(first.id);
            this.selectedPeriodType.set(first.periodType);
            this.loadBranchOverview();
          }
        },
      });

    this.routeState
      .watch('periodId')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((pid) => {
        if (pid) {
          this.periodId.set(pid);
          const match = this.periods().find((p) => p.id === pid);
          if (match) {
            this.selectedPeriodType.set(match.periodType);
          }
          this.loadBranchOverview();
        }
      });

    this.destroyRef.onDestroy(() => {
      if (this.toastTimer) clearTimeout(this.toastTimer);
    });
  }

  loadBranchOverview(): void {
    const pid = this.periodId();
    if (!pid) return;

    this.loadingOverview.set(true);
    this.errorMessage.set(null);
    this.loadBranchPeriodQuota(pid);
    this.loadPeriodFocusKpis(pid);

    this.evaluationService.getBranchApprovals(pid).subscribe({
      next: (overview) => {
        this.branchOverview.set(overview);
        this.loadingOverview.set(false);

        // Auto select first department if none selected
        if (!this.selectedDepartmentId() && overview.departments.length > 0) {
          this.selectDepartment(overview.departments[0].departmentId);
        } else if (this.selectedDepartmentId()) {
          this.loadDepartmentCalibration(this.selectedDepartmentId()!);
          this.loadDepartmentQuotas(this.selectedDepartmentId()!);
        }
      },
      error: () => {
        this.errorMessage.set(this.t()['errorLoad'] || 'Không thể tải dữ liệu xếp hạng phòng ban.');
        this.loadingOverview.set(false);
      },
    });
  }

  selectDepartment(deptId: string): void {
    this.selectedDepartmentId.set(deptId);
    this.selectedPositionId.set(null);
    this.loadDepartmentCalibration(deptId);
    this.loadDepartmentQuotas(deptId);
  }

  loadDepartmentQuotas(departmentId: string): void {
    const pid = this.periodId() || null;
    this.evaluationService.getDepartmentSlotQuotas(departmentId, pid).subscribe({
      next: (res) => {
        if (res?.slots && res.slots.length > 0) {
          this.departmentQuotas.set(res.slots);
        } else {
          this.departmentQuotas.set([
            { tierCode: 'A1', headcount: null },
            { tierCode: 'A2', headcount: null },
            { tierCode: 'A3', headcount: null },
            { tierCode: 'B', headcount: null },
            { tierCode: 'C', headcount: null },
          ]);
        }
      },
      error: () => {
        this.departmentQuotas.set([
          { tierCode: 'A1', headcount: null },
          { tierCode: 'A2', headcount: null },
          { tierCode: 'A3', headcount: null },
          { tierCode: 'B', headcount: null },
          { tierCode: 'C', headcount: null },
        ]);
      },
    });
  }

  onQuotaHeadcountChange(event: { tier: string; headcount: number | null }): void {
    if (this.isPeriodLocked()) return;
    this.departmentQuotas.update((slots) =>
      slots.map((s) => (s.tierCode === event.tier ? { ...s, headcount: event.headcount } : s)),
    );
  }

  submitDepartmentQuota(): void {
    const deptId = this.selectedDepartmentId();
    if (!deptId || this.savingQuota() || this.isPeriodLocked() || this.isDeptQuotaExceeded())
      return;
    this.savingQuota.set(true);

    const slots = this.departmentQuotas();
    const pid = this.periodId() || null;
    this.evaluationService
      .updateDepartmentSlotQuotas(deptId, { departmentId: deptId, slots, periodId: pid })
      .subscribe({
        next: (res) => {
          this.savingQuota.set(false);
          if (res?.slots) {
            this.departmentQuotas.set(res.slots);
          }
          this.showToast('Cập nhật quota thành công cho Trưởng phòng.', 'success');
          this.loadDepartmentCalibration(deptId);
        },
        error: (err: { error?: { message?: string } }) => {
          this.savingQuota.set(false);
          this.showToast(err?.error?.message || 'Lỗi khi cập nhật quota.', 'error');
        },
      });
  }

  selectPosition(posId: string | null): void {
    this.selectedPositionId.set(posId);
  }

  loadDepartmentCalibration(deptId: string): void {
    const pid = this.periodId();
    if (!pid || !deptId) return;

    this.loadingCalibration.set(true);
    this.evaluationService.getCalibration(pid, deptId, undefined, true).subscribe({
      next: (calib) => {
        this.calibrationData.set(calib);
        this.loadingCalibration.set(false);
      },
      error: () => {
        this.calibrationData.set(null);
        this.loadingCalibration.set(false);
      },
    });
  }

  setViewMode(mode: 'single' | 'all'): void {
    this.viewMode.set(mode);
  }

  toggleExpandDepartment(deptId: string): void {
    const set = new Set(this.expandedDeptIds());
    if (set.has(deptId)) {
      set.delete(deptId);
    } else {
      set.add(deptId);
      if (!this.deptCalibrations().has(deptId)) {
        this.loadDepartmentCalibrationForExpand(deptId);
      }
    }
    this.expandedDeptIds.set(set);
  }

  isDepartmentExpanded(deptId: string): boolean {
    return this.expandedDeptIds().has(deptId);
  }

  isDeptSubmitted(status: string): boolean {
    return status === 'IN_REVIEW' || status === 'APPROVED' || status === 'FINAL';
  }

  getDeptRoster(deptId: string): CalibrationEmployeeRow[] {
    return this.deptCalibrations().get(deptId)?.rankedRoster ?? [];
  }

  isDeptRosterLoading(deptId: string): boolean {
    return this.loadingDeptCalibrations().has(deptId);
  }

  startBranchAdjusting(): void {
    if (this.isBranchFinalized()) return;
    this.isAdjustingBranch.set(true);

    const submittedDepts = (this.branchOverview()?.departments ?? []).filter((d) =>
      this.isDeptSubmitted(d.status),
    );
    const expanded = new Set(this.expandedDeptIds());
    for (const dept of submittedDepts) {
      expanded.add(dept.departmentId);
      if (!this.deptCalibrations().has(dept.departmentId)) {
        this.loadDepartmentCalibrationForExpand(dept.departmentId);
      }
    }
    this.expandedDeptIds.set(expanded);
  }

  cancelBranchAdjusting(): void {
    this.isAdjustingBranch.set(false);
    this.branchDraftTier.set(new Map());
    this.branchDraftReason.set(new Map());
  }

  getEmpDraftTier(emp: CalibrationEmployeeRow): RankingTierCode {
    return (
      this.branchDraftTier().get(emp.employeeId) ??
      ((emp.calibratedTier || emp.proposedTier || 'B') as RankingTierCode)
    );
  }

  getEmpDraftReason(emp: CalibrationEmployeeRow): string {
    return this.branchDraftReason().get(emp.employeeId) ?? emp.adjustmentReason ?? '';
  }

  isEmpTierChanged(emp: CalibrationEmployeeRow): boolean {
    const draftTier = this.getEmpDraftTier(emp);
    const baseTier = emp.proposedTier || emp.calibratedTier || 'B';
    return draftTier !== baseTier;
  }

  isBranchEmpTierLocked(emp: CalibrationEmployeeRow, tier: RankingTierCode): boolean {
    const maxTier = (emp as CalibrationEmployeeRow & { maxAllowedTier?: string | null })
      .maxAllowedTier;
    if (!maxTier) return false;
    const order: Record<string, number> = { A1: 1, A2: 2, A3: 3, B: 4, C: 5 };
    const tierOrder = order[tier] ?? 99;
    const maxOrder = order[maxTier] ?? 1;
    return tierOrder < maxOrder;
  }

  onBranchEmpTierChange(empId: string, tier: string): void {
    if (this.isBranchFinalized()) return;
    this.branchDraftTier.update((m) => {
      const next = new Map(m);
      next.set(empId, tier as RankingTierCode);
      return next;
    });
  }

  onBranchEmpReasonChange(empId: string, reason: string): void {
    if (this.isBranchFinalized()) return;
    this.branchDraftReason.update((m) => {
      const next = new Map(m);
      next.set(empId, reason);
      return next;
    });
  }

  getDeptTierCount(dept: DepartmentApprovalSummary, tier: RankingTierCode): number {
    if (!this.isDeptSubmitted(dept.status)) return 0;
    const calib = this.deptCalibrations().get(dept.departmentId);
    if (calib) {
      return calib.rankedRoster.filter((emp) => this.getEmpDraftTier(emp) === tier).length;
    }
    switch (tier) {
      case 'A1':
        return dept.tierA1Count ?? 0;
      case 'A2':
        return dept.tierA2Count ?? 0;
      case 'A3':
        return dept.tierA3Count ?? 0;
      case 'B':
        return dept.tierBCount ?? 0;
      case 'C':
        return dept.tierCCount ?? 0;
    }
  }

  getBranchActualTierCount(tier: RankingTierCode): number {
    const depts = this.branchOverview()?.departments ?? [];
    return depts.reduce((sum, d) => sum + this.getDeptTierCount(d, tier), 0);
  }

  private countBranchMissingReasons(): number {
    let missing = 0;
    const depts = this.branchOverview()?.departments ?? [];
    for (const dept of depts) {
      if (!this.isDeptSubmitted(dept.status)) continue;
      const roster = this.getDeptRoster(dept.departmentId);
      for (const emp of roster) {
        if (this.isEmpTierChanged(emp) && !this.getEmpDraftReason(emp).trim()) {
          missing++;
        }
      }
    }
    return missing;
  }

  private buildBranchAdjustmentRequests(): SubmitCalibrationRequest[] {
    const pid = this.periodId();
    if (!pid) return [];

    const draftTiers = this.branchDraftTier();
    const draftReasons = this.branchDraftReason();
    const depts = this.branchOverview()?.departments ?? [];
    const requests: SubmitCalibrationRequest[] = [];

    for (const dept of depts) {
      if (!this.isDeptSubmitted(dept.status)) continue;
      const calib = this.deptCalibrations().get(dept.departmentId);
      if (!calib) continue;

      const deptModified = calib.rankedRoster.some(
        (emp) => draftTiers.has(emp.employeeId) || draftReasons.has(emp.employeeId),
      );
      if (!deptModified) continue;

      requests.push({
        departmentId: dept.departmentId,
        periodId: pid,
        isDraft: false,
        adjustments: calib.rankedRoster.map((emp) => ({
          employeeId: emp.employeeId,
          calibratedTier: this.getEmpDraftTier(emp),
          reason: this.isEmpTierChanged(emp) ? this.getEmpDraftReason(emp).trim() || null : null,
          coachingFlag: emp.coachingFlag,
        })),
      });
    }

    return requests;
  }

  saveBranchDraft(): void {
    if (this.isBranchFinalized() || this.savingBranchDraft()) return;
    if (this.countBranchMissingReasons() > 0) {
      this.showToast('Vui lòng nhập lý do điều chỉnh cho các nhân sự đã đổi hạng.', 'error');
      return;
    }

    const requests = this.buildBranchAdjustmentRequests();
    if (requests.length === 0) {
      this.isAdjustingBranch.set(false);
      this.showToast('Đã lưu tiến độ xếp hạng (trạng thái Đã gửi).', 'success');
      return;
    }

    this.savingBranchDraft.set(true);
    forkJoin(requests.map((req) => this.evaluationService.saveBranchAdjustments(req))).subscribe({
      next: () => {
        this.savingBranchDraft.set(false);
        this.isAdjustingBranch.set(false);
        this.branchDraftTier.set(new Map());
        this.branchDraftReason.set(new Map());
        this.deptCalibrations.set(new Map());
        this.showToast('Đã lưu tiến độ xếp hạng (trạng thái Đã gửi).', 'success');
        this.loadBranchOverview();
        for (const deptId of this.expandedDeptIds()) {
          this.loadDepartmentCalibrationForExpand(deptId);
        }
      },
      error: (err: { error?: { message?: string } }) => {
        this.savingBranchDraft.set(false);
        this.showToast(err?.error?.message || 'Lỗi khi lưu điều chỉnh xếp hạng.', 'error');
      },
    });
  }

  openApprovePopover(): void {
    if (this.isBranchFinalized() || this.lockSubmitting()) return;
    this.approvePopoverOpen.set(true);
  }

  closeApprovePopover(): void {
    this.approvePopoverOpen.set(false);
  }

  confirmFinalApprove(): void {
    const pid = this.periodId();
    if (!pid || this.isBranchFinalized() || this.lockSubmitting()) return;
    if (this.countBranchMissingReasons() > 0) {
      this.approvePopoverOpen.set(false);
      this.showToast('Vui lòng nhập lý do điều chỉnh cho các nhân sự đã đổi hạng.', 'error');
      return;
    }

    this.lockSubmitting.set(true);
    const requests = this.buildBranchAdjustmentRequests();
    const save$ =
      requests.length > 0
        ? forkJoin(requests.map((req) => this.evaluationService.saveBranchAdjustments(req)))
        : of([]);

    save$.pipe(switchMap(() => this.evaluationService.finalize({ periodId: pid }))).subscribe({
      next: (res) => {
        this.lockSubmitting.set(false);
        this.approvePopoverOpen.set(false);
        this.isAdjustingBranch.set(false);
        this.branchDraftTier.set(new Map());
        this.branchDraftReason.set(new Map());
        this.deptCalibrations.set(new Map());
        this.showToast(
          res.message || 'Đã ghi nhận kết quả cuối cùng và khóa kỳ đánh giá.',
          'success',
        );
        this.evaluationService.periods().subscribe({
          next: (list) => this.periods.set(list ?? []),
        });
        this.loadBranchOverview();
        for (const deptId of this.expandedDeptIds()) {
          this.loadDepartmentCalibrationForExpand(deptId);
        }
      },
      error: (err: { error?: { message?: string } }) => {
        this.lockSubmitting.set(false);
        this.approvePopoverOpen.set(false);
        this.showToast(
          err?.error?.message || 'Chưa thể ghi nhận kết quả cuối cùng. Vui lòng kiểm tra cảnh báo.',
          'error',
        );
      },
    });
  }

  loadDepartmentCalibrationForExpand(deptId: string): void {
    const pid = this.periodId();
    if (!pid || !deptId) return;

    this.loadingDeptCalibrations.update((s) => new Set(s).add(deptId));
    this.evaluationService.getCalibration(pid, deptId, undefined, true).subscribe({
      next: (calib) => {
        this.deptCalibrations.update((map) => new Map(map).set(deptId, calib));
        this.loadingDeptCalibrations.update((s) => {
          const next = new Set(s);
          next.delete(deptId);
          return next;
        });
      },
      error: () => {
        this.loadingDeptCalibrations.update((s) => {
          const next = new Set(s);
          next.delete(deptId);
          return next;
        });
      },
    });
  }

  loadBranchPeriodQuota(periodId: string): void {
    if (!periodId) return;
    this.evaluationService.getBranchPeriodQuota(periodId).subscribe({
      next: (res) => {
        if (res?.slots && res.slots.length > 0) {
          this.periodQuota.set(res.slots);
          const draft: Record<string, number | null> = {};
          res.slots.forEach((s) => {
            draft[s.tierCode] = s.headcount;
          });
          this.periodQuotaDraft.set(draft);
        } else {
          this.periodQuota.set([]);
          this.periodQuotaDraft.set({ A1: null, A2: null, A3: null, B: null, C: null });
        }
      },
      error: () => {
        this.periodQuota.set([]);
        this.periodQuotaDraft.set({ A1: null, A2: null, A3: null, B: null, C: null });
      },
    });
  }

  startEditPeriodQuota(): void {
    if (this.isPeriodLocked()) return;
    const current = this.periodQuota();
    const draft: Record<string, number | null> = { A1: null, A2: null, A3: null, B: null, C: null };
    current.forEach((s) => {
      draft[s.tierCode] = s.headcount;
    });
    this.periodQuotaDraft.set(draft);
    this.isEditingPeriodQuota.set(true);
  }

  cancelEditPeriodQuota(): void {
    this.isEditingPeriodQuota.set(false);
  }

  suggestPeriodQuotaFromRatios(): void {
    const total = this.totalBranchEmployees();
    if (total <= 0) return;

    const maxA = this.recommendedTierASlots();
    const totalA = Math.min(maxA, Math.max(1, Math.round((total * this.tierAPercent()) / 100)));
    let a1 = 0;
    let a2 = 0;
    let a3 = 0;
    if (totalA === 1) {
      a1 = 1;
    } else if (totalA === 2) {
      a1 = 1;
      a2 = 1;
    } else {
      a1 = Math.max(1, Math.floor(totalA * 0.25));
      a2 = Math.max(1, Math.floor(totalA * 0.35));
      a3 = Math.max(0, totalA - a1 - a2);
    }

    const b = Math.min(
      total - totalA,
      Math.max(0, Math.round((total * this.tierBPercent()) / 100)),
    );
    const remaining = Math.max(0, total - totalA - b);
    const c = Math.min(remaining, Math.max(0, Math.round((total * this.tierCPercent()) / 100)));

    this.periodQuotaDraft.set({ A1: a1, A2: a2, A3: a3, B: b, C: c });
  }

  onPeriodQuotaInputChange(tier: string, value: string): void {
    const val = value.trim() === '' ? null : Math.max(0, parseInt(value, 10) || 0);
    this.periodQuotaDraft.update((d) => ({ ...d, [tier]: val }));
  }

  getPeriodQuotaHeadcount(tier: string): number | null {
    const slot = this.periodQuota().find((s) => s.tierCode === tier);
    return slot?.headcount ?? null;
  }

  savePeriodQuota(): void {
    const pid = this.periodId();
    if (!pid || this.savingPeriodQuota() || this.isPeriodLocked()) return;
    const ratioErr = this.periodQuotaRatioError();
    if (ratioErr) {
      this.showToast(ratioErr, 'error');
      return;
    }
    this.savingPeriodQuota.set(true);

    const draft = this.periodQuotaDraft();
    const slots: RankingSlotQuotaItem[] = ['A1', 'A2', 'A3', 'B', 'C'].map((tier) => ({
      tierCode: tier,
      headcount: draft[tier] != null && draft[tier]! >= 0 ? Number(draft[tier]) : 0,
    }));

    this.evaluationService.updateBranchPeriodQuota(pid, slots).subscribe({
      next: (res) => {
        this.savingPeriodQuota.set(false);
        this.isEditingPeriodQuota.set(false);
        if (res?.slots) {
          this.periodQuota.set(res.slots);
        }
        this.showToast('Lưu tổng Quota của kỳ thành công.', 'success');
      },
      error: (err: { error?: { message?: string } }) => {
        this.savingPeriodQuota.set(false);
        this.showToast(err?.error?.message || 'Lỗi khi lưu tổng Quota của kỳ.', 'error');
      },
    });
  }

  loadPeriodFocusKpis(periodId: string): void {
    if (!periodId) return;
    this.evaluationService.getPeriodFocusKpis(periodId).subscribe({
      next: (res) => {
        this.focusKpis.set(res?.focusKpis ?? []);
      },
      error: () => {
        this.focusKpis.set([]);
      },
    });
  }

  startEditFocusKpis(): void {
    if (this.isPeriodLocked()) return;
    this.focusKpiDraftIds.set(this.focusKpis().map((k) => k.id));
    this.isEditingFocusKpis.set(true);
    if (this.availableKpis().length === 0) {
      this.kpiDefinitionService.list({ status: 'ACTIVE', pageSize: 100 }).subscribe({
        next: (res) => this.availableKpis.set(res.items ?? []),
        error: () => this.availableKpis.set([]),
      });
    }
  }

  cancelEditFocusKpis(): void {
    this.isEditingFocusKpis.set(false);
  }

  isFocusKpiSelected(kpiId: string): boolean {
    return this.focusKpiDraftIds().includes(kpiId);
  }

  toggleFocusKpiDraft(kpiId: string): void {
    const current = this.focusKpiDraftIds();
    if (current.includes(kpiId)) {
      this.focusKpiDraftIds.set(current.filter((id) => id !== kpiId));
      return;
    }
    if (current.length >= 5) {
      this.showToast('Chỉ được chọn tối đa 5 KPI trọng tâm cho mỗi kỳ.', 'error');
      return;
    }
    this.focusKpiDraftIds.set([...current, kpiId]);
  }

  saveFocusKpis(): void {
    const pid = this.periodId();
    if (!pid || this.savingFocusKpis() || this.isPeriodLocked()) return;
    this.savingFocusKpis.set(true);
    this.evaluationService.updatePeriodFocusKpis(pid, this.focusKpiDraftIds()).subscribe({
      next: (res) => {
        this.savingFocusKpis.set(false);
        this.isEditingFocusKpis.set(false);
        this.focusKpis.set(res?.focusKpis ?? []);
        this.showToast('Lưu KPI trọng tâm của kỳ thành công.', 'success');
        this.loadBranchOverview();
      },
      error: (err: { error?: { message?: string } }) => {
        this.savingFocusKpis.set(false);
        this.showToast(err?.error?.message || 'Lỗi khi lưu KPI trọng tâm của kỳ.', 'error');
      },
    });
  }

  openApproveModal(): void {
    this.approveNote.set('');
    this.isApproveModalOpen.set(true);
  }

  closeApproveModal(): void {
    this.isApproveModalOpen.set(false);
    this.approveNote.set('');
  }

  submitApproveDepartment(): void {
    const deptId = this.selectedDepartmentId();
    const pid = this.periodId();
    if (!deptId || !pid) return;

    this.approveSubmitting.set(true);
    this.evaluationService
      .approveDepartment(deptId, { periodId: pid, note: this.approveNote().trim() || null })
      .subscribe({
        next: (res) => {
          this.approveSubmitting.set(false);
          this.closeApproveModal();
          this.showToast(this.t()['approveSuccess'] || res.message, 'success');
          this.loadBranchOverview();
        },
        error: (err: { error?: { message?: string } }) => {
          this.approveSubmitting.set(false);
          this.showToast(err?.error?.message || 'Lỗi phê duyệt phòng ban.', 'error');
        },
      });
  }

  openRejectModal(): void {
    this.rejectReason.set('');
    this.rejectError.set(null);
    this.isRejectModalOpen.set(true);
  }

  closeRejectModal(): void {
    this.isRejectModalOpen.set(false);
    this.rejectReason.set('');
    this.rejectError.set(null);
  }

  submitRejectDepartment(): void {
    const reason = this.rejectReason().trim();
    if (reason.length < 10) {
      this.rejectError.set(
        this.t()['rejectReasonMinLength'] || 'Lý do trả về phải có ít nhất 10 ký tự.',
      );
      return;
    }

    const deptId = this.selectedDepartmentId();
    const pid = this.periodId();
    if (!deptId || !pid) return;

    this.rejectSubmitting.set(true);
    this.rejectError.set(null);

    this.evaluationService.rejectDepartment(deptId, { periodId: pid, reason }).subscribe({
      next: (res) => {
        this.rejectSubmitting.set(false);
        this.closeRejectModal();
        this.showToast(this.t()['rejectSuccess'] || res.message, 'success');
        this.loadBranchOverview();
      },
      error: (err: { error?: { message?: string } }) => {
        this.rejectSubmitting.set(false);
        this.rejectError.set(err?.error?.message || 'Lỗi khi yêu cầu hiệu chuẩn lại.');
      },
    });
  }

  openLockModal(): void {
    this.lockSignatureToken.set('');
    this.lockApprovalComments.set('');
    this.lockError.set(null);
    this.isLockModalOpen.set(true);
  }

  closeLockModal(): void {
    this.isLockModalOpen.set(false);
    this.lockError.set(null);
  }

  submitLockPeriod(): void {
    const pid = this.periodId();
    if (!pid) return;

    this.lockSubmitting.set(true);
    this.lockError.set(null);

    this.evaluationService
      .finalize({
        periodId: pid,
        signatureToken: this.lockSignatureToken().trim() || null,
        approvalComments: this.lockApprovalComments().trim() || null,
      })
      .subscribe({
        next: (res) => {
          this.lockSubmitting.set(false);
          this.closeLockModal();
          this.showToast(
            this.tLock().lockSuccess || res.message || 'Khóa kỳ đánh giá thành công.',
            'success',
          );
          this.loadBranchOverview();
        },
        error: (err: { error?: { message?: string } }) => {
          this.lockSubmitting.set(false);
          this.lockError.set(err?.error?.message || 'Lỗi khi khóa kỳ đánh giá.');
        },
      });
  }

  navigateToTraceability(employeeId: string): void {
    const periodId = this.periodId();
    if (!periodId) return;
    this.formTarget.save({ employeeId, periodId });
    void this.router.navigate(['/evaluations/form']);
  }

  formatDate(dateStr?: string | null): string {
    if (!dateStr) return '';
    try {
      const d = new Date(dateStr);
      return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`;
    } catch {
      return '';
    }
  }

  formatScore(score?: number | null): string {
    if (score == null) return '-';
    return score.toLocaleString('vi-VN', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  }

  formatNumber(value: number | null | undefined, maxDecimals = 1): string {
    if (value === null || value === undefined) return '-';
    return this.i18n.formatNumber(value, maxDecimals);
  }

  private showToast(message: string, type: 'success' | 'error'): void {
    this.toastMessage.set(message);
    this.toastType.set(type);
    if (this.toastTimer) clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toastMessage.set(null), 4000);
  }
}
