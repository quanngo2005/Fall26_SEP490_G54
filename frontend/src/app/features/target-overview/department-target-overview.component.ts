import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, map, of, Subject, switchMap } from 'rxjs';
import { DepartmentSummary } from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { TargetAssignment, TargetFeedbackStatus } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { AppDatePipe, AppNumberPipe } from '../../shared/pipes';
import { TargetCellComponent } from './target-cell.component';
import {
  availableYears,
  buildFeedbackRows,
  buildQuotaRows,
  fetchAllPages,
  FeedbackRow,
  findQuarterPeriod,
  findYearPeriod,
  includesText,
  periodQuarter,
  periodYear,
  TargetCell,
  usedKpis,
  valuesByOwner,
} from './target-overview.utils';

const QUARTERS = [1, 2, 3, 4] as const;
const PAGE_SIZE = 10;

interface LoadedTargets {
  yearRows: TargetAssignment[];
  quarterRows: TargetAssignment[][];
}

const NO_TARGETS: LoadedTargets = { yearRows: [], quarterRows: [[], [], [], []] };

@Component({
  selector: 'app-department-target-overview',
  imports: [FormsModule, RouterLink, TargetCellComponent, AppNumberPipe, AppDatePipe],
  templateUrl: './department-target-overview.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './target-overview.scss',
  ],
})
export class DepartmentTargetOverviewComponent extends LocalizedComponent implements OnInit {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly evaluationApi = inject(EvaluationService);
  private readonly kpiApi = inject(KpiDefinitionService);
  private readonly employeeApi = inject(EmployeeService);
  private readonly departmentApi = inject(DepartmentService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly selection = new Subject<number>();

  protected readonly quarters = QUARTERS;
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  private readonly periods = signal<EvaluationPeriodOption[]>([]);
  private readonly kpis = signal<KpiDefinition[]>([]);
  /** Employees directly in the department first, then those of each team, team by team. */
  private readonly employees = signal<EmployeeDetail[]>([]);
  private readonly teams = signal<DepartmentSummary[]>([]);
  private readonly teamNames = computed(
    () => new Map(this.teams().map((team) => [team.id, team.name])),
  );
  /** Each team's own share per KPI (same order as `quota`), shown on the team's header row. */
  private readonly teamCells = computed(() => {
    const shares = valuesByOwner(this.quarterRows(), 'departmentId');
    return new Map(
      this.teams().map((team) => [
        team.id,
        this.quota().map((row): TargetCell => shares.get(team.id)?.get(row.kpi.id) ?? 'NONE'),
      ]),
    );
  });
  private readonly targets = signal<LoadedTargets>(NO_TARGETS);
  protected readonly departmentId = signal('');
  protected readonly departmentName = signal('');

  protected readonly year = signal<number | null>(null);
  protected readonly quarter = signal(0);
  protected readonly search = signal('');
  protected readonly position = signal('');
  protected readonly feedbackFilter = signal<TargetFeedbackStatus | ''>('');
  protected readonly page = signal(1);

  /** Latest comment text per employee id, shown in the table. */
  protected readonly comments = signal<Record<string, string>>({});

  protected readonly years = computed(() => availableYears(this.periods()));
  protected readonly quarterRows = computed(
    () => this.targets().quarterRows[this.quarter() - 1] ?? [],
  );
  private readonly columns = computed(() => {
    const { yearRows, quarterRows } = this.targets();
    const own = [...yearRows, ...quarterRows.flat()].filter(
      (row) => row.departmentId === this.departmentId(),
    );
    return usedKpis(this.kpis(), own);
  });
  protected readonly quota = computed(() =>
    buildQuotaRows(
      this.columns(),
      this.departmentId(),
      this.targets().yearRows,
      this.targets().quarterRows,
      this.quarterRows(),
    ),
  );
  protected readonly allocationStatus = computed(() => {
    const own = this.quarterRows().filter((row) => row.employeeId);
    if (own.length === 0) {
      return this.localization.targetOverview.statusNone;
    }
    return own.some((row) => row.status === 'DRAFT')
      ? this.localization.targetOverview.statusDraft
      : this.localization.targetOverview.statusActive;
  });
  protected readonly feedbackRows = computed(() =>
    buildFeedbackRows(this.quota(), this.employees(), this.quarterRows()),
  );
  protected readonly counts = computed(() => {
    const rows = this.feedbackRows();
    const count = (status: TargetFeedbackStatus) =>
      rows.filter((r) => r.feedback === status).length;
    return {
      confirmed: count('CONFIRMED'),
      commented: count('COMMENTED'),
      notViewed: count('NOT_VIEWED'),
    };
  });
  protected readonly positionOptions = computed(() =>
    [...new Set(this.employees().map((e) => e.jobPositionTitle))].sort(),
  );
  private readonly filteredRows = computed(() => {
    const query = this.search().trim().toLowerCase();
    return this.feedbackRows().filter(
      (row) =>
        (!query ||
          includesText(row.employee.fullName, query) ||
          includesText(row.employee.employeeCode, query)) &&
        (!this.position() || row.employee.jobPositionTitle === this.position()) &&
        (!this.feedbackFilter() || row.feedback === this.feedbackFilter()),
    );
  });
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.filteredRows().length / PAGE_SIZE)),
  );
  protected readonly pageNumbers = computed(() =>
    Array.from({ length: this.totalPages() }, (_, index) => index + 1),
  );
  protected readonly pageRows = computed(() => {
    const start = (Math.min(this.page(), this.totalPages()) - 1) * PAGE_SIZE;
    return this.filteredRows().slice(start, start + PAGE_SIZE);
  });
  protected readonly range = computed(() => {
    const start = (Math.min(this.page(), this.totalPages()) - 1) * PAGE_SIZE;
    const total = this.filteredRows().length;
    return { from: total === 0 ? 0 : start + 1, to: start + this.pageRows().length, total };
  });
  protected readonly quarterOptions = computed(() => {
    const year = this.year();
    return QUARTERS.filter((q) => year !== null && findQuarterPeriod(this.periods(), year, q));
  });

  constructor() {
    super();
    this.selection
      .pipe(
        switchMap((year) => {
          const load = (period: EvaluationPeriodOption | undefined) =>
            period ? this.targetApi.list({ evaluationPeriodId: period.id }) : of([]);
          return forkJoin({
            yearRows: load(findYearPeriod(this.periods(), year)),
            quarterRows: forkJoin(
              QUARTERS.map((q) => load(findQuarterPeriod(this.periods(), year, q))),
            ),
          }).pipe(
            catchError(() => {
              this.failed.set(true);
              return of(NO_TARGETS);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((loaded) => {
        this.targets.set(loaded);
        this.loading.set(false);
        this.loadComments();
      });
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.employeeApi
      .me()
      .pipe(
        switchMap((me) =>
          forkJoin({
            teams: fetchAllPages((page, pageSize) =>
              this.departmentApi.list({
                page,
                pageSize,
                parentDepartmentId: me.departmentId,
                status: 'ACTIVE',
              }),
            ).pipe(map((all) => all.filter((d) => d.parentDepartmentId === me.departmentId))),
            periods: this.evaluationApi.periods(),
            kpis: fetchAllPages((page, pageSize) => this.kpiApi.list({ page, pageSize })),
            employees: fetchAllPages((page, pageSize) =>
              this.employeeApi.list({
                page,
                pageSize,
                departmentId: me.departmentId,
                status: 'ACTIVE',
              }),
            ),
          }).pipe(
            switchMap((data) =>
              (data.teams.length === 0
                ? of([])
                : forkJoin(
                    data.teams.map((team) =>
                      fetchAllPages((page, pageSize) =>
                        this.employeeApi.list({
                          page,
                          pageSize,
                          departmentId: team.id,
                          status: 'ACTIVE',
                        }),
                      ),
                    ),
                  )
              ).pipe(map((members) => ({ ...data, me, members: members.flat() }))),
            ),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ periods, kpis, employees, teams, members, me }) => {
          this.periods.set(periods);
          this.kpis.set(kpis);
          this.employees.set([...employees, ...members]);
          this.teams.set(teams);
          this.departmentId.set(me.departmentId);
          this.departmentName.set(me.departmentName);
          const year = this.years()[0] ?? null;
          this.year.set(year);
          this.quarter.set(year === null ? 0 : this.defaultQuarter(periods, year));
          this.reload();
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected selectYear(year: number): void {
    this.year.set(year);
    this.quarter.set(this.defaultQuarter(this.periods(), year));
    this.reload();
  }

  protected selectQuarter(quarter: number): void {
    this.quarter.set(quarter);
    this.page.set(1);
  }

  protected resetPage(): void {
    this.page.set(1);
  }

  /** Fetches the newest comment of every employee who commented, so the table can show it. */
  private loadComments(): void {
    this.comments.set({});
    const commented = this.feedbackRows().filter((row) => row.feedback === 'COMMENTED');
    if (commented.length === 0) return;
    forkJoin(
      commented.map((row) =>
        forkJoin(row.assignmentIds.map((id) => this.targetApi.feedback(id))).pipe(
          map((groups) => {
            const latest = groups
              .flat()
              .filter((item) => item.type === 'COMMENT' && item.comment)
              .sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0];
            return [row.employee.id, latest?.comment ?? ''] as const;
          }),
          catchError(() => of([row.employee.id, ''] as const)),
        ),
      ),
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((entries) => this.comments.set(Object.fromEntries(entries)));
  }

  /** The team whose header goes above `rows[index]`: set when the row starts a new team. */
  protected teamStart(
    rows: readonly FeedbackRow[],
    index: number,
  ): { name: string; cells: TargetCell[] } | null {
    const department = rows[index].employee.departmentId;
    const name = this.teamNames().get(department);
    const starts = name !== undefined && rows[index - 1]?.employee.departmentId !== department;
    return starts ? { name, cells: this.teamCells().get(department) ?? [] } : null;
  }

  protected feedbackLabel(status: TargetFeedbackStatus): string {
    const text = this.localization.targetOverview;
    return {
      CONFIRMED: text.statusConfirmed,
      COMMENTED: text.statusCommented,
      NOT_VIEWED: text.statusNotViewed,
    }[status];
  }

  /** The current quarter when the year has it, otherwise the last quarter that exists. */
  private defaultQuarter(periods: readonly EvaluationPeriodOption[], year: number): number {
    const existing = periods
      .filter((p) => p.periodType === 'QUARTER' && periodYear(p) === year)
      .map((p) => periodQuarter(p));
    const current = Math.floor(new Date().getMonth() / 3) + 1;
    return existing.includes(current) && new Date().getFullYear() === year
      ? current
      : (existing.sort((a, b) => b - a)[0] ?? 0);
  }

  private reload(): void {
    const year = this.year();
    if (year === null) {
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.failed.set(false);
    this.selection.next(year);
  }
}
