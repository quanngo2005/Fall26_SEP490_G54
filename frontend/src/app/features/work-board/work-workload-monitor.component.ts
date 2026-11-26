import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  expand,
  forkJoin,
  map,
  of,
  reduce,
  switchMap,
} from 'rxjs';
import { DepartmentMember, DepartmentSummary } from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { WorkListItem, WorkStatus } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { businessClock, dateOnly, daysOverdue } from './work-board.utils';

export type MonitorPeriod = 'all' | 'week' | 'month' | 'quarter';

export interface FunnelStageRow {
  key: 'new' | 'inProgress' | 'submitted' | 'done';
  label: string;
  count: number;
  percent: number;
  showPercent: boolean;
}

export interface EmployeeLoadRow {
  employeeId: string;
  fullName: string;
  jobPositionTitle: string;
  openCount: number;
  overdueCount: number;
  loadPercent: number;
  isHighLoad: boolean;
}

export interface OverdueAlertRow {
  item: WorkListItem;
  daysLate: number;
  dueDateFormatted: string | null;
}

const MONITOR_PAGE_SIZE = 100;
const MS_PER_DAY = 86_400_000;
const IN_PROGRESS_GROUP: ReadonlySet<WorkStatus> = new Set([
  'NEW',
  'IN_PROGRESS',
  'HANDED_OVER',
  'REJECTED',
  'SUBMITTED',
]);
const COMPLETED_GROUP: ReadonlySet<WorkStatus> = new Set(['VERIFIED', 'DONE']);
const OPEN_GROUP: ReadonlySet<WorkStatus> = new Set([
  'NEW',
  'IN_PROGRESS',
  'HANDED_OVER',
  'REJECTED',
  'SUBMITTED',
  'OVERDUE',
]);
const FUNNEL_STARTED_GROUP: ReadonlySet<WorkStatus> = new Set([
  'IN_PROGRESS',
  'HANDED_OVER',
  'REJECTED',
  'OVERDUE',
  'SUBMITTED',
  'VERIFIED',
  'DONE',
]);
const FUNNEL_SUBMITTED_GROUP: ReadonlySet<WorkStatus> = new Set(['SUBMITTED', 'VERIFIED', 'DONE']);
const DEADLINE_EXEMPT_GROUP: ReadonlySet<WorkStatus> = new Set([
  'SUBMITTED',
  'VERIFIED',
  'DONE',
  'CANCELLED',
]);

function dayNumber(date: string): number {
  const [year, month, day] = date.split('-').map(Number);
  return Math.floor(Date.UTC(year, month - 1, day) / MS_PER_DAY);
}

/** Checks whether a work belongs to the selected calendar period based on Vietnam time (UTC+7). */
export function isWorkInMonitorPeriod(
  item: Pick<WorkListItem, 'plannedStartDate' | 'plannedEndDate' | 'updatedAt'>,
  period: MonitorPeriod,
  today: string,
): boolean {
  if (period === 'all') return true;
  const refDate =
    item.plannedEndDate ?? item.plannedStartDate ?? businessClock(new Date(item.updatedAt)).date;
  if (!refDate) return false;
  if (period === 'month') return refDate.slice(0, 7) === today.slice(0, 7);
  if (period === 'quarter') {
    const refYear = Number(refDate.slice(0, 4));
    const refMonth = Number(refDate.slice(5, 7));
    const curYear = Number(today.slice(0, 4));
    const curMonth = Number(today.slice(5, 7));
    return refYear === curYear && Math.floor((refMonth - 1) / 3) === Math.floor((curMonth - 1) / 3);
  }
  const current = dayNumber(today);
  const weekStart = current - ((current + 3) % 7);
  const target = dayNumber(refDate);
  return target >= weekStart && target <= weekStart + 6;
}

export function isMonitorWorkOverdue(
  item: Pick<WorkListItem, 'status' | 'plannedEndDate'>,
  today: string,
): boolean {
  if (item.status === 'OVERDUE') return true;
  if (DEADLINE_EXEMPT_GROUP.has(item.status)) return false;
  return daysOverdue(item.plannedEndDate, today) > 0;
}

/**
 * SCR-WRK-06: Department Workload & Funnel Monitor.
 * Aggregates department works (`GET /works`) and members (`GET /departments/{id}/members`) on the client.
 * Stage funnel merges Verified + Done into 4 stages (`New`, `In progress`, `Submitted`, `Done`).
 */
@Component({
  selector: 'app-work-workload-monitor',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './work-workload-monitor.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './work-workload-monitor.component.scss',
    './work-workload-monitor.component.part2.scss',
  ],
})
export class WorkWorkloadMonitorComponent extends LocalizedComponent {
  private readonly worksApi = inject(WorkService);
  private readonly departmentsApi = inject(DepartmentService);
  private readonly destroyRef = inject(DestroyRef);

  readonly defaultDepartmentId = input.required<string>();
  readonly now = input<Date>(new Date());
  readonly openWork = output<string>();

  private readonly reloadTrigger = new Subject<void>();

  protected readonly departments = signal<DepartmentSummary[]>([]);
  protected readonly selectedDepartmentId = signal('');
  protected readonly period = signal<MonitorPeriod>('all');
  protected readonly overdueFilterOnly = signal(false);

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly workItems = signal<WorkListItem[]>([]);
  protected readonly members = signal<DepartmentMember[]>([]);

  protected readonly periodOptions: readonly { value: MonitorPeriod; key: string }[] = [
    { value: 'all', key: 'work.monitor.periodAll' },
    { value: 'week', key: 'work.monitor.periodWeek' },
    { value: 'month', key: 'work.monitor.periodMonth' },
    { value: 'quarter', key: 'work.monitor.periodQuarter' },
  ];

  protected readonly today = computed(() => businessClock(this.now()).date);

  protected readonly activeWorks = computed(() => {
    const today = this.today();
    const period = this.period();
    return this.workItems().filter(
      (item) => item.status !== 'CANCELLED' && isWorkInMonitorPeriod(item, period, today),
    );
  });

  protected readonly summary = computed(() => {
    const items = this.activeWorks();
    const today = this.today();
    const total = items.length;
    const inProgress = items.filter((item) => IN_PROGRESS_GROUP.has(item.status)).length;
    const completed = items.filter((item) => COMPLETED_GROUP.has(item.status)).length;
    const completedPercent = total > 0 ? Math.round((completed / total) * 100) : 0;
    const overdue = items.filter((item) => isMonitorWorkOverdue(item, today)).length;
    const employeeCount =
      this.members().length > 0
        ? this.members().length
        : new Set(items.map((item) => item.assigneeEmployeeId)).size;

    return {
      total,
      employeeCount,
      inProgress,
      completed,
      completedPercent,
      overdue,
    };
  });

  /** 4-stage funnel over WorkType-linked tasks (Verified merged with Done per requirement). */
  protected readonly funnelStages = computed((): FunnelStageRow[] => {
    const kpiWorks = this.activeWorks().filter((item) => item.workTypeId !== null);
    const total = kpiWorks.length;
    const text = this.localization.work.monitor;
    const toPercent = (count: number) => (total > 0 ? Math.round((count / total) * 100) : 0);

    const startedCount = kpiWorks.filter((item) => FUNNEL_STARTED_GROUP.has(item.status)).length;
    const submittedCount = kpiWorks.filter((item) =>
      FUNNEL_SUBMITTED_GROUP.has(item.status),
    ).length;
    const doneCount = kpiWorks.filter((item) => COMPLETED_GROUP.has(item.status)).length;

    return [
      {
        key: 'new',
        label: text.funnelStageNew,
        count: total,
        percent: total > 0 ? 100 : 0,
        showPercent: false,
      },
      {
        key: 'inProgress',
        label: text.funnelStageInProgress,
        count: startedCount,
        percent: toPercent(startedCount),
        showPercent: true,
      },
      {
        key: 'submitted',
        label: text.funnelStageSubmitted,
        count: submittedCount,
        percent: toPercent(submittedCount),
        showPercent: true,
      },
      {
        key: 'done',
        label: text.funnelStageDone,
        count: doneCount,
        percent: toPercent(doneCount),
        showPercent: true,
      },
    ];
  });

  protected readonly hasFunnelData = computed(() =>
    this.activeWorks().some((item) => item.workTypeId !== null),
  );

  protected readonly employeeLoads = computed((): EmployeeLoadRow[] => {
    const today = this.today();
    const mapById = new Map<
      string,
      { fullName: string; jobPositionTitle: string; openCount: number; overdueCount: number }
    >();

    for (const member of this.members()) {
      mapById.set(member.id, {
        fullName: member.fullName,
        jobPositionTitle: member.jobPositionTitle,
        openCount: 0,
        overdueCount: 0,
      });
    }

    for (const item of this.activeWorks()) {
      const existing = mapById.get(item.assigneeEmployeeId) ?? {
        fullName:
          item.assigneeEmployeeName ??
          item.assigneeEmployeeCode ??
          this.localization.work.monitor.unassigned,
        jobPositionTitle: this.localization.department.notAvailable,
        openCount: 0,
        overdueCount: 0,
      };
      if (OPEN_GROUP.has(item.status)) existing.openCount += 1;
      if (isMonitorWorkOverdue(item, today)) existing.overdueCount += 1;
      mapById.set(item.assigneeEmployeeId, existing);
    }

    const rows = Array.from(mapById.entries()).map(([employeeId, entry]) => ({
      employeeId,
      ...entry,
    }));

    rows.sort(
      (a, b) =>
        b.openCount - a.openCount ||
        b.overdueCount - a.overdueCount ||
        a.fullName.localeCompare(b.fullName),
    );

    const maxOpen = rows.reduce((max, row) => Math.max(max, row.openCount), 0);
    return rows.map((row) => {
      const loadPercent = maxOpen > 0 ? Math.round((row.openCount / maxOpen) * 100) : 0;
      return {
        ...row,
        loadPercent,
        isHighLoad: row.overdueCount > 0 || (maxOpen > 0 && loadPercent >= 80),
      };
    });
  });

  protected readonly overdueAlerts = computed((): OverdueAlertRow[] => {
    const today = this.today();
    return this.activeWorks()
      .filter((item) => isMonitorWorkOverdue(item, today))
      .map((item) => ({
        item,
        daysLate: Math.max(1, daysOverdue(item.plannedEndDate, today)),
        dueDateFormatted: item.plannedEndDate
          ? this.i18n.formatDate(dateOnly(item.plannedEndDate))
          : null,
      }))
      .sort((a, b) => b.daysLate - a.daysLate || a.item.code.localeCompare(b.item.code));
  });

  constructor() {
    super();

    this.departmentsApi
      .list({ status: 'ACTIVE', pageSize: 100 })
      .pipe(
        catchError(() =>
          of({ items: [] as DepartmentSummary[], totalCount: 0, page: 1, pageSize: 100 }),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => this.departments.set(page.items));

    this.reloadTrigger
      .pipe(
        switchMap(() => {
          const deptId = this.selectedDepartmentId();
          if (!deptId) return EMPTY;
          this.loading.set(true);
          this.failed.set(false);
          return forkJoin({
            works: this.fetchAllDepartmentWorks(deptId),
            members: this.departmentsApi
              .members(deptId, { pageSize: MONITOR_PAGE_SIZE })
              .pipe(map((res) => res.items)),
          }).pipe(
            catchError(() => {
              this.failed.set(true);
              this.loading.set(false);
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ works, members }) => {
        this.workItems.set(works);
        this.members.set(members);
        this.loading.set(false);
      });

    effect(() => {
      const deptId = this.defaultDepartmentId();
      if (deptId && !this.selectedDepartmentId()) {
        this.selectedDepartmentId.set(deptId);
        this.reloadTrigger.next();
      }
    });
  }

  protected onDepartmentChange(departmentId: string): void {
    this.selectedDepartmentId.set(departmentId);
    this.overdueFilterOnly.set(false);
    this.reloadTrigger.next();
  }

  protected onPeriodChange(value: MonitorPeriod): void {
    this.period.set(value);
  }

  protected toggleOverdueFilter(): void {
    this.overdueFilterOnly.update((current) => !current);
    if (this.overdueFilterOnly()) {
      document
        .getElementById('monitor-overdue-alerts')
        ?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
  }

  protected retry(): void {
    this.reloadTrigger.next();
  }

  protected overdueMetaText(alert: OverdueAlertRow): string {
    const name =
      alert.item.assigneeEmployeeName ??
      alert.item.assigneeEmployeeCode ??
      this.localization.work.monitor.unassigned;
    return alert.dueDateFormatted
      ? this.i18n.translate('work.monitor.overdueMeta', { name, date: alert.dueDateFormatted })
      : this.i18n.translate('work.monitor.overdueMetaNoDate', { name });
  }

  private fetchAllDepartmentWorks(departmentId: string): Observable<WorkListItem[]> {
    return this.worksApi
      .list({
        departmentId,
        includeChildDepartments: true,
        page: 1,
        pageSize: MONITOR_PAGE_SIZE,
      })
      .pipe(
        expand((res) => {
          const loaded = res.page * res.pageSize;
          if (loaded >= res.totalCount || res.items.length === 0) return EMPTY;
          return this.worksApi.list({
            departmentId,
            includeChildDepartments: true,
            page: res.page + 1,
            pageSize: MONITOR_PAGE_SIZE,
          });
        }),
        reduce((acc, res) => [...acc, ...res.items], [] as WorkListItem[]),
      );
  }
}
