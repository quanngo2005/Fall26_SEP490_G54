import { CdkDrag, CdkDragDrop, CdkDropList, DragDropModule } from '@angular/cdk/drag-drop';
import { Component, DestroyRef, NgZone, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  debounceTime,
  map,
  merge,
  of,
  switchMap,
  tap,
} from 'rxjs';
import { DepartmentMember } from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { JobPositionSummary } from '../../core/job-position.models';
import { JobPositionService } from '../../core/job-position.service';
import { WorkTypeDetail } from '../../core/work-type.models';
import { WorkTypeService } from '../../core/work-type.service';
import { WorkFilter, WorkListItem, WorkPriority, WorkStatus } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { NavigationService } from '../../shared/layout/navigation.service';
import { initials } from '../../shared/ui/display';
import {
  BOARD_COLUMNS,
  BoardColumnKey,
  DueRange,
  QuickFilter,
  TEAM_COLUMNS,
  WorkScope,
  businessClock,
  businessMonth,
  canComplete,
  canStart,
  columnKeyOf,
  dateOnly,
  daysOverdue,
  inDueRange,
  isDueOn,
  quickFilterColumns,
  timeLeftToday,
} from './work-board.utils';
import { WorkDetailDrawerComponent } from './work-detail-drawer.component';

interface ColumnState {
  items: WorkListItem[];
  totalCount: number;
  page: number;
  loading: boolean;
  loadingMore: boolean;
  failed: boolean;
  /** Only the Done column starts collapsed. */
  expanded: boolean;
}

interface ListState {
  items: WorkListItem[];
  totalCount: number;
  page: number;
  loading: boolean;
  failed: boolean;
}

interface Toast {
  kind: 'success' | 'error';
  text: string;
}

const COLUMN_PAGE_SIZE = 50;
const LIST_PAGE_SIZE = 20;
const DONE_COLLAPSED_LIMIT = 2;
const TOAST_MS = 5000;
const CLOCK_TICK_MS = 30_000;
const SEARCH_DEBOUNCE_MS = 300;

const emptyColumn = (loading: boolean): ColumnState => ({
  items: [],
  totalCount: 0,
  page: 1,
  loading,
  loadingMore: false,
  failed: false,
  expanded: false,
});

/**
 * SCR-WORK-01 / SCR-WORK-02: the kanban board (default) or list of works, fed by `GET /works`.
 * An employee sees their own work. A manager (someone with direct reports) sees the whole department's by
 * default and can switch to their own. The rest of the screen is the same.
 */
@Component({
  selector: 'app-work-board',
  standalone: true,
  imports: [
    FormsModule,
    DragDropModule,
    HeaderComponent,
    SidebarComponent,
    WorkDetailDrawerComponent,
  ],
  templateUrl: './work-board.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './work-board.component.scss',
    './work-board.component.part2.scss',
    './work-board.component.part3.scss',
  ],
})
export class WorkBoardComponent extends LocalizedComponent {
  private readonly works = inject(WorkService);
  private readonly employees = inject(EmployeeService);
  private readonly departments = inject(DepartmentService);
  private readonly jobPositionApi = inject(JobPositionService);
  private readonly workTypeApi = inject(WorkTypeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly zone = inject(NgZone);

  private readonly boardReload = new Subject<void>();
  private readonly listReload = new Subject<void>();
  private readonly keywordInput = new Subject<void>();
  private toastTimer: ReturnType<typeof setTimeout> | undefined;
  private suppressClick = false;
  /** Bumped on every board reload so a late "load more" answer cannot land in a newer board. */
  private boardVersion = 0;

  /** Whose work is shown; a manager can flip it, an employee only ever has mine. */
  protected readonly scope = signal<WorkScope>('mine');
  /** A manager can open the department's work; everyone else only has their own. */
  protected readonly isManager = inject(NavigationService).isManager;
  protected readonly isTeam = computed(() => this.scope() === 'team');
  private me: Pick<EmployeeDetail, 'id' | 'departmentId'> | null = null;
  protected readonly columnDefs = computed(() => (this.isTeam() ? TEAM_COLUMNS : BOARD_COLUMNS));
  /** The one move a card allows: my board starts a work, the department board completes one. */
  private readonly action = computed(
    (): { status: 'InProgress' | 'Done'; column: BoardColumnKey; allows: typeof canStart } =>
      this.isTeam()
        ? { status: 'Done', column: 'done', allows: canComplete }
        : { status: 'InProgress', column: 'inProgress', allows: canStart },
  );
  protected readonly priorities: readonly WorkPriority[] = ['Critical', 'High', 'Medium', 'Low'];

  protected readonly view = signal<'board' | 'list'>('board');
  protected readonly profileLoading = signal(true);
  protected readonly profileFailed = signal(false);
  protected readonly columns = signal<Record<BoardColumnKey, ColumnState>>(this.emptyColumns(true));
  protected readonly list = signal<ListState>({
    items: [],
    totalCount: 0,
    page: 1,
    loading: false,
    failed: false,
  });
  protected readonly workTypes = signal<WorkTypeDetail[]>([]);
  protected readonly members = signal<DepartmentMember[]>([]);
  protected readonly positions = signal<JobPositionSummary[]>([]);
  protected readonly due = signal<DueRange>('all');
  protected readonly quick = signal<QuickFilter | null>(null);
  protected readonly positionId = signal('');
  protected readonly selectedId = signal<string | null>(null);
  protected readonly toast = signal<Toast | null>(null);
  protected readonly now = signal(new Date());

  protected keyword = '';
  protected workTypeId = '';
  protected priority: WorkPriority | '' = '';
  protected employeeId = '';

  protected readonly clock = computed(() => businessClock(this.now()));

  /** Filters that can only be applied to works that are already loaded, so counts must follow them. */
  private readonly clientFiltered = computed(
    () => this.due() !== 'all' || this.quick() === 'dueToday' || this.positionId() !== '',
  );

  protected readonly boardView = computed(() => {
    const keys = quickFilterColumns(this.scope(), this.quick());
    const columns = this.columns();
    const filtered = this.clientFiltered();
    return this.columnDefs()
      .filter((def) => !keys || keys.includes(def.key))
      .map((def) => {
        const state = columns[def.key];
        const matching = state.items.filter((item) => this.matchesClientFilters(item));
        const count = filtered ? matching.length : state.totalCount;
        const collapsed =
          def.key === 'done' && !state.expanded && matching.length > DONE_COLLAPSED_LIMIT;
        return {
          def,
          state,
          shown: collapsed ? matching.slice(0, DONE_COLLAPSED_LIMIT) : matching,
          count,
          collapsed,
          hiddenCount: collapsed ? Math.max(0, count - DONE_COLLAPSED_LIMIT) : 0,
          canLoadMore: !collapsed && state.items.length < state.totalCount,
        };
      });
  });

  protected readonly listItems = computed(() =>
    this.list().items.filter((item) => this.matchesClientFilters(item)),
  );

  protected readonly listPages = computed(() =>
    Math.max(1, Math.ceil(this.list().totalCount / LIST_PAGE_SIZE)),
  );

  /** Number shown next to the filters. */
  protected readonly visibleCount = computed(() => {
    if (this.view() === 'list') {
      return this.clientFiltered() ? this.listItems().length : this.list().totalCount;
    }
    return this.boardView().reduce((sum, column) => sum + column.count, 0);
  });

  protected readonly kpis = computed(() => {
    const columns = this.columns();
    const today = this.clock().date;
    const month = today.slice(0, 7);
    const openKeys = quickFilterColumns(this.scope(), 'open') ?? [];
    const open = openKeys.reduce((sum, key) => sum + columns[key].totalCount, 0);
    let dueToday = 0;
    let dueTodayPartial = false;
    const assignees = new Set<string>();
    for (const key of openKeys) {
      const state = columns[key];
      dueToday += state.items.filter((item) => isDueOn(item.plannedEndDate, today)).length;
      dueTodayPartial ||= state.items.length < state.totalCount;
      // Counted from the loaded cards, so it can undercount a department with more than one page per column.
      for (const item of state.items) assignees.add(item.assigneeEmployeeId);
    }
    const overdue = columns.overdue;
    const lateDays = overdue.items.reduce(
      (max, item) => Math.max(max, daysOverdue(item.plannedEndDate, today)),
      0,
    );
    const done = columns.done;
    const doneThisMonth = done.items.filter((item) => businessMonth(item.updatedAt) === month);
    return {
      open,
      dueToday,
      dueTodayPartial,
      employeesCount: assignees.size,
      overdue: overdue.totalCount,
      lateDays,
      doneThisMonth: doneThisMonth.length,
      doneThisMonthPartial:
        done.items.length < done.totalCount && doneThisMonth.length === done.items.length,
      doneTotal: done.totalCount,
    };
  });

  protected readonly kpiLoading = computed(() =>
    Object.values(this.columns()).some((state) => state.loading && state.items.length === 0),
  );

  protected readonly timeLeft = computed(() => {
    const { hours, minutes } = timeLeftToday(this.clock());
    return this.i18n.translate('work.kpi.timeLeft', {
      hours,
      minutes: String(minutes).padStart(2, '0'),
    });
  });

  protected readonly refreshing = computed(
    () =>
      Object.values(this.columns()).some((state) => state.loading && state.items.length > 0) ||
      (this.view() === 'list' && this.list().loading && this.list().items.length > 0),
  );

  constructor() {
    super();
    this.listenForBoardReloads();
    this.listenForListReloads();
    this.keywordInput
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.applyServerFilters());

    // Outside the zone so a perpetual interval never keeps the app "unstable"; the signal write
    // still schedules change detection.
    const timer = this.zone.runOutsideAngular(() =>
      setInterval(() => this.now.set(new Date()), CLOCK_TICK_MS),
    );
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      clearTimeout(this.toastTimer);
    });

    this.optional(this.workTypeApi.list({ pageSize: 100 }), (page) =>
      this.workTypes.set(page.items),
    );
    this.loadProfile();
  }

  /** Subscribes to a request the screen can live without: a failure just leaves its list empty. */
  private optional<T>(request: Observable<T>, onData: (data: T) => void): void {
    request
      .pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(onData);
  }

  protected loadProfile(): void {
    this.profileLoading.set(true);
    this.profileFailed.set(false);
    this.employees
      .me()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (me) => {
          this.me = me;
          const manager = this.isManager();
          if (manager) {
            this.optional(this.departments.members(me.departmentId, { pageSize: 100 }), (page) =>
              this.members.set(page.items),
            );
            this.optional(this.jobPositionApi.list({ pageSize: 100 }), (page) =>
              this.positions.set(page.items),
            );
          }
          this.profileLoading.set(false);
          this.chooseScope(manager ? 'team' : 'mine');
        },
        error: () => {
          this.profileFailed.set(true);
          this.profileLoading.set(false);
          this.columns.set(this.emptyColumns(false));
        },
      });
  }

  private emptyColumns(loading: boolean): Record<BoardColumnKey, ColumnState> {
    return Object.fromEntries(
      this.columnDefs().map((def) => [def.key, emptyColumn(loading)]),
    ) as Record<BoardColumnKey, ColumnState>;
  }

  /**
   * Switches between my own work and the department's. The two use different columns and filters, so the
   * screen starts over with the new ones.
   */
  protected chooseScope(scope: WorkScope): void {
    if (scope === 'team' && !this.isManager()) return;
    this.scope.set(scope);
    this.quick.set(null);
    this.positionId.set('');
    this.employeeId = '';
    this.priority = '';
    this.columns.set(this.emptyColumns(true));
    this.list.update((state) => ({ ...state, items: [], totalCount: 0, page: 1 }));
    this.boardReload.next();
    if (this.view() === 'list') this.listReload.next();
  }

  // ---- Filters and view -----------------------------------------------------------------------

  protected setView(mode: 'board' | 'list'): void {
    if (this.view() === mode) return;
    this.view.set(mode);
    if (mode === 'list') this.reloadListFromFirstPage();
  }

  protected onKeywordChange(value: string): void {
    this.keyword = value;
    this.keywordInput.next();
  }

  protected applyServerFilters(): void {
    if (!this.me) return;
    this.list.update((state) => ({ ...state, page: 1 }));
    this.boardReload.next();
    if (this.view() === 'list') this.listReload.next();
  }

  protected setDue(range: DueRange): void {
    this.due.set(range);
  }

  protected toggleQuick(filter: QuickFilter): void {
    this.quick.update((current) => (current === filter ? null : filter));
    if (this.view() === 'list') this.reloadListFromFirstPage();
  }

  protected retry(): void {
    if (this.profileFailed()) {
      this.loadProfile();
      return;
    }
    this.applyServerFilters();
  }

  private reloadListFromFirstPage(): void {
    this.list.update((state) => ({ ...state, page: 1 }));
    this.listReload.next();
  }

  protected changeListPage(delta: number): void {
    const page = this.list().page + delta;
    if (page < 1 || page > this.listPages()) return;
    this.list.update((state) => ({ ...state, page }));
    this.listReload.next();
  }

  private matchesClientFilters(item: WorkListItem): boolean {
    const today = this.clock().date;
    if (!inDueRange(item.plannedEndDate, this.due(), today)) return false;
    if (this.quick() === 'dueToday' && !isDueOn(item.plannedEndDate, today)) return false;
    const position = this.positionId();
    if (!position) return true;
    // Work of someone outside the loaded member list cannot be matched to a position, so it is not shown for one.
    return this.members().find((m) => m.id === item.assigneeEmployeeId)?.jobPositionId === position;
  }

  // ---- Loading --------------------------------------------------------------------------------

  private baseFilter(): WorkFilter {
    const me = this.me;
    return {
      ...(this.isTeam()
        ? {
            departmentId: me?.departmentId,
            includeChildDepartments: true,
            assigneeEmployeeId: this.employeeId,
          }
        : { assigneeEmployeeId: me?.id, priority: this.priority || undefined }),
      keyword: this.keyword.trim() || undefined,
      workTypeId: this.workTypeId || undefined,
    };
  }

  private listenForBoardReloads(): void {
    this.boardReload
      .pipe(
        tap(() => {
          this.boardVersion += 1;
          this.columns.update((columns) => {
            const next = { ...columns };
            for (const def of this.columnDefs()) {
              next[def.key] = { ...columns[def.key], loading: true, failed: false };
            }
            return next;
          });
        }),
        switchMap(() =>
          merge(
            ...this.columnDefs().map((def) =>
              this.works
                .list({
                  ...this.baseFilter(),
                  status: def.statuses,
                  page: 1,
                  pageSize: COLUMN_PAGE_SIZE,
                })
                .pipe(
                  map((result) => ({ def, result })),
                  catchError(() => of({ def, result: null })),
                ),
            ),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ def, result }) =>
        this.patchColumn(
          def.key,
          result
            ? {
                items: result.items,
                totalCount: result.totalCount,
                page: 1,
                loading: false,
                loadingMore: false,
                failed: false,
              }
            : { items: [], totalCount: 0, loading: false, loadingMore: false, failed: true },
        ),
      );
  }

  private listenForListReloads(): void {
    this.listReload
      .pipe(
        switchMap(() => {
          if (!this.me) return EMPTY;
          this.list.update((state) => ({ ...state, loading: true, failed: false }));
          return this.works
            .list({
              ...this.baseFilter(),
              status: this.listStatuses(),
              page: this.list().page,
              pageSize: LIST_PAGE_SIZE,
            })
            .pipe(
              tap((result) =>
                this.list.update((state) => ({
                  ...state,
                  items: result.items,
                  totalCount: result.totalCount,
                  loading: false,
                })),
              ),
              catchError(() => {
                this.list.update((state) => ({ ...state, failed: true, loading: false }));
                return EMPTY;
              }),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  /** The list view narrows by the quick filter's columns, so it shows what the board would. */
  private listStatuses(): WorkStatus[] | undefined {
    const keys = quickFilterColumns(this.scope(), this.quick());
    if (!keys) return undefined;
    return this.columnDefs()
      .filter((def) => keys.includes(def.key))
      .flatMap((def) => [...def.statuses]);
  }

  private patchColumn(key: BoardColumnKey, patch: Partial<ColumnState>): void {
    this.columns.update((columns) => ({ ...columns, [key]: { ...columns[key], ...patch } }));
  }

  protected loadMore(key: BoardColumnKey): void {
    const state = this.columns()[key];
    const def = this.columnDefs().find((column) => column.key === key);
    if (!def || state.loadingMore) return;
    const version = this.boardVersion;
    this.patchColumn(key, { loadingMore: true });
    this.works
      .list({
        ...this.baseFilter(),
        status: def.statuses,
        page: state.page + 1,
        pageSize: COLUMN_PAGE_SIZE,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          if (version !== this.boardVersion) return;
          const known = new Set(this.columns()[key].items.map((item) => item.id));
          this.patchColumn(key, {
            items: [
              ...this.columns()[key].items,
              ...result.items.filter((item) => !known.has(item.id)),
            ],
            totalCount: result.totalCount,
            page: state.page + 1,
            loadingMore: false,
          });
        },
        error: () => {
          if (version !== this.boardVersion) return;
          this.patchColumn(key, { loadingMore: false });
          this.showToast('error', this.localization.work.toast.loadMoreFailed);
        },
      });
  }

  protected expand(key: BoardColumnKey): void {
    this.patchColumn(key, { expanded: true });
  }

  // ---- Cards ----------------------------------------------------------------------------------

  protected canAct(item: WorkListItem): boolean {
    return this.action().allows(item);
  }

  /** A deadline only matters while the assignee still owes a result. */
  private deadlineApplies(status: WorkStatus): boolean {
    return !['Submitted', 'Verified', 'Done', 'Cancelled'].includes(status);
  }

  protected badgeOf(item: WorkListItem): {
    kind: 'overdue' | 'today' | 'priority';
    days: number;
    priority: WorkPriority;
  } {
    const today = this.clock().date;
    const late = daysOverdue(item.plannedEndDate, today);
    if (item.status === 'Overdue' || (late > 0 && this.deadlineApplies(item.status))) {
      return { kind: 'overdue', days: Math.max(1, late), priority: item.priority };
    }
    if (isDueOn(item.plannedEndDate, today) && this.deadlineApplies(item.status)) {
      return { kind: 'today', days: 0, priority: item.priority };
    }
    return { kind: 'priority', days: 0, priority: item.priority };
  }

  /** Secondary chip: the sub-status when a column mixes statuses, or the priority the badge replaced. */
  protected chipOf(item: WorkListItem): { text: string; tone: string } | null {
    const text = this.localization.work.status;
    switch (item.status) {
      case 'Submitted':
        return { text: text.Submitted, tone: 'info' };
      case 'Verified':
        return { text: text.Verified, tone: this.isTeam() ? 'info' : 'success' };
      case 'HandedOver':
        return { text: text.HandedOver, tone: 'warning' };
      case 'Rejected':
        return { text: text.Rejected, tone: 'danger' };
      case 'Done':
        return { text: text.Done, tone: 'success' };
      default:
        break;
    }
    if (
      !this.isTeam() &&
      this.badgeOf(item).kind !== 'priority' &&
      ['High', 'Critical'].includes(item.priority)
    ) {
      return { text: this.localization.work.priority[item.priority], tone: 'danger' };
    }
    return null;
  }

  protected dueText(item: WorkListItem): string {
    return item.plannedEndDate
      ? this.i18n.translate('work.card.due', { date: this.day(item.plannedEndDate) })
      : this.localization.work.card.noDue;
  }

  protected day(value: string | null): string {
    return value
      ? this.i18n.formatDate(dateOnly(value))
      : this.localization.department.notAvailable;
  }

  /** Empty when the name is unknown, so the avatar shows nothing rather than an invented label. */
  protected initialsOf(name: string | null | undefined): string {
    return name ? initials(name) : '';
  }

  protected openDetail(item: WorkListItem): void {
    if (this.suppressClick) return;
    this.selectedId.set(item.id);
  }

  protected closeDetail(): void {
    this.selectedId.set(null);
  }

  protected onDragStarted(): void {
    this.suppressClick = true;
  }

  protected onDragEnded(): void {
    setTimeout(() => (this.suppressClick = false));
  }

  // ---- Drag and drop / start / complete -------------------------------------------------------

  /** The only move is into the action's column: "start" goes to In progress, "complete" to Done. */
  protected readonly canEnter = (
    drag: CdkDrag<WorkListItem>,
    drop: CdkDropList<BoardColumnKey>,
  ): boolean => drop.data === this.action().column && this.canAct(drag.data);

  protected onDrop(event: CdkDragDrop<BoardColumnKey, BoardColumnKey, WorkListItem>): void {
    if (event.previousContainer === event.container) return;
    if (this.canEnter(event.item, event.container)) this.changeStatus(event.item.data);
  }

  /** Moves the card at once and puts it back, with a message, when the API refuses. */
  protected changeStatus(item: WorkListItem): void {
    const { status: target, column: to } = this.action();
    const from = columnKeyOf(this.columnDefs(), item.status);
    if (!from || !this.canAct(item)) return;
    const before = this.columns();
    this.columns.update((columns) => {
      const moved = { ...item, status: target };
      const stays = from === to;
      return {
        ...columns,
        [from]: {
          ...columns[from],
          items: columns[from].items.filter((c) => c.id !== item.id),
          totalCount: columns[from].totalCount - (stays ? 0 : 1),
        },
        [to]: {
          ...columns[to],
          items: [moved, ...columns[to].items.filter((c) => c.id !== item.id)],
          totalCount: columns[to].totalCount + (stays ? 0 : 1),
        },
      };
    });

    this.works
      .changeStatus(item.id, { status: target })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.columns.update((columns) => ({
            ...columns,
            [to]: {
              ...columns[to],
              items: columns[to].items.map((c) =>
                c.id === item.id ? { ...c, status: detail.status, updatedAt: detail.updatedAt } : c,
              ),
            },
          }));
          this.showToast(
            'success',
            this.i18n.translate(this.isTeam() ? 'work.toast.completed' : 'work.toast.started', {
              code: item.code,
            }),
          );
        },
        error: (error: { status?: number }) => {
          this.columns.set(before);
          this.showToast('error', this.changeStatusError(error.status, item));
        },
      });
  }

  private changeStatusError(status: number | undefined, item: WorkListItem): string {
    const toast = this.localization.work.toast;
    if (status === 403) return this.isTeam() ? toast.completeForbidden : toast.moveForbidden;
    if (status === 409 && !this.isTeam()) {
      return this.i18n.translate('work.toast.moveConflict', { code: item.code });
    }
    return this.isTeam() ? toast.completeFailed : toast.moveFailed;
  }

  // ---- Toast ----------------------------------------------------------------------------------

  private showToast(kind: Toast['kind'], text: string): void {
    clearTimeout(this.toastTimer);
    this.toast.set({ kind, text });
    this.toastTimer = setTimeout(() => this.toast.set(null), TOAST_MS);
  }

  protected dismissToast(): void {
    clearTimeout(this.toastTimer);
    this.toast.set(null);
  }
}
