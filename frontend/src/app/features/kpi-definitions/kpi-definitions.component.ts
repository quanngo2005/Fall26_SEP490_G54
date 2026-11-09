import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subject, catchError, forkJoin, of, switchMap, tap } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { JobPositionSummary } from '../../core/job-position.models';
import { JobPositionService } from '../../core/job-position.service';
import {
  KPI_CREDIT_ROLES,
  KPI_DATA_SOURCES,
  KpiDefinition,
  KpiGroupOption,
  KpiStatus,
  KpiSummary,
} from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { businessClock } from '../work-board/work-board.utils';
import { KpiDecision, KpiDecisionModalComponent } from './kpi-decision-modal.component';
import { KpiDetailDrawerComponent, KpiViewerMode } from './kpi-detail-drawer.component';
import {
  EMPTY_FILTERS,
  AGGREGATION_KEYS,
  SavedFilters,
  StatusTab,
  actionsFor,
  groupByProduct,
  isExpiringSoon,
  loadFilters,
  periodShort,
  saveFilters,
  summaryCount,
} from './kpi-definitions.utils';
import {
  KpiFormMode,
  KpiFormModalComponent,
  KpiFormResult,
  KpiFormValues,
} from './kpi-form-modal.component';

const NOTICE_MS = 5000;
const PAGE_SIZES = [20, 50, 100] as const;
const MAKER_CANNOT_BE_CHECKER = 'MAKER_CANNOT_BE_CHECKER';

interface FormState {
  mode: KpiFormMode;
  kpi: KpiDefinition | null;
  prefill: Partial<KpiFormValues> | null;
}

/** SCR-STRAT-02: the KPI catalog. The System Admin maintains it; the Branch Manager approves what is sent. */
@Component({
  selector: 'app-kpi-definitions',
  imports: [
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    KpiFormModalComponent,
    KpiDecisionModalComponent,
    KpiDetailDrawerComponent,
  ],
  templateUrl: './kpi-definitions.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './kpi-definitions.component.scss',
    './kpi-definitions.part2.scss',
  ],
})
export class KpiDefinitionsComponent extends LocalizedComponent {
  private readonly api = inject(KpiDefinitionService);
  private readonly auth = inject(AuthService);
  private readonly jobPositions = inject(JobPositionService);
  private readonly evaluations = inject(EvaluationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  private readonly today = businessClock(new Date()).date;
  private noticeTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly creditRoles = KPI_CREDIT_ROLES;
  protected readonly dataSources = KPI_DATA_SOURCES;
  protected readonly aggregationKey = AGGREGATION_KEYS;
  protected readonly pageSizes = PAGE_SIZES;

  protected readonly kpis = signal<KpiDefinition[]>([]);
  protected readonly groups = signal<KpiGroupOption[]>([]);
  protected readonly positions = signal<JobPositionSummary[]>([]);
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  protected readonly summary = signal<KpiSummary | null>(null);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);

  protected readonly tab = signal<StatusTab>('ALL');
  protected readonly selected = signal<KpiDefinition | null>(null);
  protected readonly form = signal<FormState | null>(null);
  protected readonly decision = signal<{ kpi: KpiDefinition; initial: KpiDecision } | null>(null);
  protected readonly menuFor = signal<string | null>(null);
  protected readonly notice = signal<{ kind: 'success' | 'error'; text: string } | null>(null);

  protected filters: SavedFilters = { ...EMPTY_FILTERS };
  protected page = 1;
  protected pageSize: number = PAGE_SIZES[0];

  /** What the signed-in user can do with the catalog. */
  private readonly permissions = computed(
    () =>
      new Set(
        (this.auth.currentUser()?.permissions ?? []).map((p) =>
          p.toUpperCase().replace(/[.:-]/g, '_'),
        ),
      ),
  );
  protected readonly canManage = computed(() => this.permissions().has('KPI_MANAGE'));
  protected readonly canApprove = computed(() => this.permissions().has('KPI_APPROVE'));
  protected readonly canRequest = computed(() => this.permissions().has('KPI_REQUEST'));
  protected readonly viewerMode = computed<KpiViewerMode>(() =>
    this.canManage() ? 'admin' : this.canApprove() ? 'approver' : 'viewer',
  );
  protected readonly isApprover = computed(() => this.viewerMode() === 'approver');

  protected readonly groupsByProduct = computed(() => groupByProduct(this.kpis()));
  protected readonly quarters = computed(() =>
    this.periods().filter((p) => p.periodType === 'QUARTER'),
  );
  protected readonly products = computed(() => [
    ...new Set(
      this.kpis()
        .map((kpi) => kpi.productName)
        .filter((name): name is string => !!name),
    ),
  ]);
  protected readonly units = computed(() =>
    [...new Set(this.kpis().map((kpi) => kpi.unit))].sort(),
  );

  /** The tabs; a status with no KPI is left out, except the ones the main flow needs. */
  protected readonly tabs = computed<{ id: StatusTab; label: string }[]>(() => {
    const text = this.localization.kpiCatalog;
    const summary = this.summary();
    const all: { id: StatusTab; label: string; always: boolean }[] = [
      { id: 'ALL', label: text.tabAll, always: true },
      { id: 'PENDING_APPROVAL', label: text.tabPending, always: true },
      { id: 'ACTIVE', label: text.tabActive, always: true },
      { id: 'DRAFT', label: text.tabDraft, always: true },
      { id: 'REJECTED', label: text.tabRejected, always: false },
      { id: 'INACTIVE', label: text.tabInactive, always: true },
      { id: 'ARCHIVED', label: text.tabArchived, always: false },
    ];
    return all
      .filter((tab) => tab.always || (summary !== null && summaryCount(summary, tab.id) > 0))
      .map((tab) => ({ id: tab.id, label: tab.label }));
  });

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize)),
  );
  protected readonly pageNumbers = computed(() =>
    Array.from({ length: this.pageCount() }, (_, index) => index + 1).slice(0, 7),
  );

  constructor() {
    super();
    this.filters = loadFilters(this.storage());
    this.reload
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.failed.set(false);
        }),
        switchMap(() =>
          forkJoin({
            list: this.api.list({
              keyword: this.filters.keyword.trim(),
              status: this.tab() === 'ALL' ? undefined : (this.tab() as KpiStatus),
              kpiGroupId: this.filters.groupId,
              jobPositionId: this.filters.positionId,
              creditRole: (this.filters.creditRole || undefined) as
                KpiDefinition['creditRole'] | undefined,
              dataSource: (this.filters.dataSource || undefined) as
                KpiDefinition['dataSource'] | undefined,
              effectiveAtPeriodId: this.filters.periodId,
              page: this.page,
              pageSize: this.pageSize,
            }),
            summary: this.api.summary().pipe(catchError(() => of(null))),
          }).pipe(
            catchError(() => {
              this.failed.set(true);
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (!result) return;
        this.kpis.set(result.list.items);
        this.totalCount.set(result.list.totalCount);
        if (result.summary) this.summary.set(result.summary);
        const open = this.selected();
        if (open) this.selected.set(result.list.items.find((kpi) => kpi.id === open.id) ?? open);
      });

    forkJoin({
      groups: this.api.groups().pipe(catchError(() => of([] as KpiGroupOption[]))),
      positions: this.jobPositions
        .list({ pageSize: 100 })
        .pipe(catchError(() => of({ items: [] as JobPositionSummary[] }))),
      periods: this.evaluations
        .periods()
        .pipe(catchError(() => of([] as EvaluationPeriodOption[]))),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ groups, positions, periods }) => {
        this.groups.set(groups);
        this.positions.set(positions.items);
        this.periods.set(periods);
      });

    this.destroyRef.onDestroy(() => clearTimeout(this.noticeTimer));
    // A Branch Manager comes to the list to decide, so it opens on the KPIs that are waiting.
    if (this.viewerMode() === 'approver') this.tab.set('PENDING_APPROVAL');
    this.refresh();
  }

  // ---- Loading and filters ---------------------------------------------------------------------

  protected refresh(resetPage = false): void {
    if (resetPage) this.page = 1;
    this.reload.next();
  }

  protected chooseTab(tab: StatusTab): void {
    this.tab.set(tab);
    this.refresh(true);
  }

  protected setPageSize(size: number): void {
    this.pageSize = Number(size);
    this.refresh(true);
  }

  protected goToPage(page: number): void {
    this.page = Math.min(Math.max(1, page), this.pageCount());
    this.refresh();
  }

  protected clearFilters(): void {
    this.filters = { ...EMPTY_FILTERS };
    saveFilters(this.storage(), this.filters);
    this.refresh(true);
  }

  protected rememberFilters(): void {
    const saved = saveFilters(this.storage(), this.filters);
    this.show(
      saved ? 'success' : 'error',
      saved
        ? this.localization.kpiCatalog.filtersSaved
        : this.localization.kpiCatalog.filtersNotSaved,
    );
  }

  protected get rangeText(): string {
    const total = this.totalCount();
    const from = total === 0 ? 0 : (this.page - 1) * this.pageSize + 1;
    const to = Math.min(this.page * this.pageSize, total);
    return this.i18n.translate('kpiCatalog.rowsInfo', { from, to, total });
  }

  protected tabCount(tab: StatusTab): number | null {
    const summary = this.summary();
    return summary ? summaryCount(summary, tab) : null;
  }

  // ---- Row helpers -------------------------------------------------------------------------------

  protected actions(kpi: KpiDefinition) {
    return actionsFor(kpi.status);
  }

  protected expiring(kpi: KpiDefinition): boolean {
    return isExpiringSoon(kpi, this.today);
  }

  protected effectiveText(kpi: KpiDefinition): string {
    if (!kpi.effectiveFrom) return this.localization.department.notAvailable;
    const end = kpi.effectiveTo ? periodShort(kpi.effectiveTo) : this.localization.kpiCatalog.noEnd;
    return `${periodShort(kpi.effectiveFrom)} → ${end}`;
  }

  protected positionsText(kpi: KpiDefinition): string {
    return (
      kpi.jobPositions.map((position) => position.title).join(', ') ||
      this.localization.department.notAvailable
    );
  }

  protected statusLabel(status: KpiStatus): string {
    const text = this.localization.kpiCatalog;
    const labels: Record<KpiStatus, string> = {
      DRAFT: text.statusDraft,
      PENDING_APPROVAL: text.statusPending,
      REJECTED: text.statusRejected,
      ACTIVE: text.statusActive,
      INACTIVE: text.statusInactive,
      ARCHIVED: text.statusArchived,
    };
    return labels[status];
  }

  protected dateTime(value: string | null): string {
    return value
      ? this.i18n.formatDate(value, 'DD/MM/YYYY HH:mm')
      : this.localization.department.notAvailable;
  }

  protected productTitle(product: string | null): string {
    return product
      ? this.i18n.translate('kpiCatalog.productGroup', { product })
      : this.localization.kpiCatalog.noProductGroup;
  }

  protected periodLabel(period: EvaluationPeriodOption): string {
    return periodShort({ code: period.code });
  }

  // ---- Opening and closing ------------------------------------------------------------------------

  protected open(kpi: KpiDefinition): void {
    this.menuFor.set(null);
    this.selected.set(kpi);
  }

  protected toggleMenu(event: Event, kpi: KpiDefinition): void {
    event.stopPropagation();
    this.menuFor.update((current) => (current === kpi.id ? null : kpi.id));
  }

  @HostListener('document:click')
  protected onDocumentClick(): void {
    this.menuFor.set(null);
  }

  protected openCreate(): void {
    this.form.set({ mode: 'create', kpi: null, prefill: null });
  }

  protected openRequest(): void {
    this.form.set({ mode: 'request', kpi: null, prefill: null });
  }

  protected openEdit(kpi: KpiDefinition): void {
    this.menuFor.set(null);
    this.selected.set(null);
    this.form.set({ mode: 'edit', kpi, prefill: null });
  }

  protected openDecision(kpi: KpiDefinition, initial: KpiDecision): void {
    this.menuFor.set(null);
    this.decision.set({ kpi, initial });
  }

  // ---- Results of the dialogs and the row actions ---------------------------------------------------

  protected onFormSaved(result: KpiFormResult): void {
    const state = this.form();
    const text = this.localization.kpiCatalog;
    const code = result.kpi.code;
    this.form.set(null);

    if (state?.mode === 'request')
      this.show('success', this.i18n.translate('kpiCatalog.toastRequested', { code }));
    else if (result.submitFailed) this.show('error', text.toastFailed);
    else if (result.submitted)
      this.show('success', this.i18n.translate('kpiCatalog.toastSubmitted', { code }));
    else if (state?.mode === 'edit')
      this.show('success', this.i18n.translate('kpiCatalog.toastUpdated', { code }));
    else this.show('success', this.i18n.translate('kpiCatalog.toastCreated', { code }));

    if (result.pair)
      this.form.set({ mode: 'create', kpi: null, prefill: this.processorPrefill(result.kpi) });
    this.refresh();
  }

  /** The Processor item that goes with a Referrer item starts from the same product, positions, file and period. */
  private processorPrefill(kpi: KpiDefinition): Partial<KpiFormValues> {
    return {
      kpiGroupId: kpi.kpiGroupId,
      positionIds: kpi.jobPositions.map((position) => position.id),
      creditRole: 'PROCESSOR',
      dataSource: kpi.dataSource,
      productName: kpi.productName ?? '',
      unit: kpi.unit,
      direction: kpi.direction,
      aggregationType: kpi.aggregationType,
      fromPeriodId: kpi.effectiveFrom?.id ?? '',
      toPeriodId: kpi.effectiveTo?.id ?? '',
    };
  }

  protected onDecided(event: { decision: KpiDecision; kpi: KpiDefinition }): void {
    const key =
      event.decision === 'APPROVE' ? 'kpiCatalog.toastApproved' : 'kpiCatalog.toastRejected';
    this.decision.set(null);
    this.selected.set(null);
    this.show('success', this.i18n.translate(key, { code: event.kpi.code }));
    this.refresh();
  }

  protected submitForApproval(kpi: KpiDefinition): void {
    this.changeStatus(kpi, 'PENDING_APPROVAL', 'kpiCatalog.toastSubmitted');
  }

  protected setActive(kpi: KpiDefinition, status: 'ACTIVE' | 'INACTIVE'): void {
    this.changeStatus(kpi, status, 'kpiCatalog.toastStatusChanged');
  }

  private changeStatus(kpi: KpiDefinition, status: KpiStatus, toastKey: string): void {
    this.menuFor.set(null);
    if (this.busy()) return;
    this.busy.set(true);
    this.api.changeStatus(kpi.id, status).subscribe({
      next: () => {
        this.busy.set(false);
        this.selected.set(null);
        this.show('success', this.i18n.translate(toastKey, { code: kpi.code }));
        this.refresh();
      },
      error: (error: HttpErrorResponse) => {
        this.busy.set(false);
        this.show('error', this.errorText(error));
        if (error.status === 409 || error.status === 404) this.refresh();
      },
    });
  }

  private errorText(error: HttpErrorResponse): string {
    const text = this.localization.kpiCatalog;
    if (error.status === 422 && error.error?.errorCode === MAKER_CANNOT_BE_CHECKER)
      return text.toastMaker;
    if (error.status === 409) return text.toastConflict;
    if (error.status === 404) return text.toastNotFound;
    return text.toastFailed;
  }

  // ---- Notices and storage ---------------------------------------------------------------------------

  protected dismissNotice(): void {
    clearTimeout(this.noticeTimer);
    this.notice.set(null);
  }

  private show(kind: 'success' | 'error', text: string): void {
    clearTimeout(this.noticeTimer);
    this.notice.set({ kind, text });
    this.noticeTimer = setTimeout(() => this.notice.set(null), NOTICE_MS);
  }

  /** Browser storage can be missing or blocked; callers treat null as "not available". */
  private storage(): Storage | null {
    try {
      return globalThis.localStorage ?? null;
    } catch {
      return null;
    }
  }
}
