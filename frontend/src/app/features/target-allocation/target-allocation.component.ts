import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { DepartmentSummary } from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import {
  TargetAssignment,
  TargetBreakdown,
  TargetFeedbackStatus,
} from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppNumberPipe } from '../../shared/pipes';
import {
  employeeFeedback,
  fetchAllPages,
  findQuarterPeriod,
  periodQuarter,
  periodYear,
} from '../target-overview/target-overview.utils';
import { remainingOf, splitByWeights, weekdaysBetween } from './target-allocation.utils';

/** Target of an employee by employee id, then KPI id; null = not entered. */
type CellValues = Record<string, Record<string, number | null>>;

interface Shortfall {
  kpi: KpiDefinition;
  /** Positive = short, negative = over. */
  remaining: number;
}

/**
 * SCR-STRAT-05: a department manager hands the department's quarter target of each summed KPI to the
 * employees, one by one. A team's row is only the total of its employees: saving first splits the
 * department's target between its own employees and its teams, then each team's total among its employees.
 */
@Component({
  selector: 'app-target-allocation',
  imports: [FormsModule, RouterLink, HeaderComponent, SidebarComponent, AppNumberPipe],
  templateUrl: './target-allocation.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './target-allocation.component.scss',
  ],
})
export class TargetAllocationComponent extends LocalizedComponent implements OnInit {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly evaluationApi = inject(EvaluationService);
  private readonly kpiApi = inject(KpiDefinitionService);
  private readonly employeeApi = inject(EmployeeService);
  private readonly departmentApi = inject(DepartmentService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);
  protected readonly notice = signal<{ kind: 'success' | 'error'; text: string } | null>(null);

  protected readonly departmentName = signal('');
  private departmentId = '';
  private readonly kpis = signal<KpiDefinition[]>([]);
  /** Employees directly in the department. */
  protected readonly employees = signal<EmployeeDetail[]>([]);
  protected readonly teams = signal<DepartmentSummary[]>([]);
  /** Employees of each team by team id. */
  private readonly teamMembers = signal<Record<string, EmployeeDetail[]>>({});
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  protected readonly periodId = signal('');
  private readonly assignments = signal<TargetAssignment[]>([]);

  protected readonly values = signal<CellValues>({});
  protected readonly position = signal('');
  protected readonly selectedId = signal<string | null>(null);
  protected readonly selectedKpiId = signal('');
  protected readonly breakdown = signal<{
    state: 'idle' | 'loading' | 'failed' | 'ready';
    data: TargetBreakdown | null;
  }>({ state: 'idle', data: null });
  /** Cells the manager typed by hand (`employeeId|kpiId`): balancing leaves them as they are. */
  private readonly pins = signal<ReadonlySet<string>>(new Set());
  /** Adjusting unlocks what is already split; otherwise it is read-only. */
  protected readonly editing = signal(false);
  protected readonly reason = signal('');

  protected readonly period = computed(() => this.periods().find((p) => p.id === this.periodId()));
  protected readonly periodLabel = computed(() => {
    const period = this.period();
    return period
      ? `${this.localization.targetAllocation.quarter} ${periodQuarter(period)}/${periodYear(period)}`
      : '';
  });
  protected readonly workDays = computed(() => {
    const period = this.period();
    return period ? weekdaysBetween(period.startDate, period.endDate) : 0;
  });

  /** The department's own summed quarter targets, one per KPI. */
  protected readonly columns = computed(() => {
    const own = new Map(
      this.assignments()
        .filter((a) => a.departmentId === this.departmentId && a.status === 'ACTIVE')
        .map((a) => [a.kpiDefinitionId, a]),
    );
    return this.kpis()
      .filter((kpi) => kpi.aggregationType === 'SUM' && own.has(kpi.id))
      .map((kpi) => ({ kpi, parent: own.get(kpi.id) as TargetAssignment }));
  });
  /** Every employee that holds a share, directly or through a team. */
  private readonly everyone = computed(() => [
    ...this.employees(),
    ...Object.values(this.teamMembers()).flat(),
  ]);
  private readonly saved = computed(() =>
    this.assignments().filter(
      (a) => a.employeeId && a.status !== 'SUPERSEDED' && a.status !== 'CLOSED',
    ),
  );
  /** KPIs whose department target was already split (to employees or to teams). */
  private readonly allocatedKpis = computed(() => {
    const parents = new Map(this.columns().map(({ kpi, parent }) => [parent.id, kpi.id]));
    return new Set(
      this.assignments()
        .filter((a) => a.status === 'ACTIVE' && parents.has(a.parentAssignmentId ?? ''))
        .map((a) => parents.get(a.parentAssignmentId as string)),
    );
  });
  protected readonly anyAllocated = computed(() => this.allocatedKpis().size > 0);
  protected readonly canSplit = computed(() =>
    this.columns().some(({ kpi }) => !this.isLocked(kpi.id)),
  );

  protected readonly positions = computed(() => {
    const counts = new Map<string, number>();
    for (const employee of this.everyone()) {
      counts.set(employee.jobPositionTitle, (counts.get(employee.jobPositionTitle) ?? 0) + 1);
    }
    return [...counts]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([title, count]) => ({ title, count }));
  });
  private readonly matchesPosition = (employee: EmployeeDetail) =>
    !this.position() || employee.jobPositionTitle === this.position();
  protected readonly rows = computed(() =>
    this.employees()
      .filter(this.matchesPosition)
      .map((employee) => ({ employee, feedback: this.feedbackOf(employee.id) })),
  );
  /** Teams with the employees that match the position tab; a team without a match is hidden. */
  protected readonly teamGroups = computed(() =>
    this.teams()
      .map((team) => ({
        team,
        members: (this.teamMembers()[team.id] ?? []).filter(this.matchesPosition),
        size: (this.teamMembers()[team.id] ?? []).length,
      }))
      .filter((group) => !this.position() || group.members.length > 0),
  );

  protected readonly shortfalls = computed((): Shortfall[] =>
    this.columns()
      .filter(({ kpi }) => !this.isLocked(kpi.id))
      .map(({ kpi }) => ({ kpi, remaining: this.remaining(kpi.id) }))
      .filter((item) => item.remaining !== 0),
  );
  /** Only KPIs the manager has started filling in, so the page does not open with an error. */
  protected readonly shownShortfalls = computed(() =>
    this.shortfalls().filter(({ kpi }) =>
      this.everyone().some((employee) => this.cell(employee.id, kpi.id) !== null),
    ),
  );
  /** KPIs to write: new splits, and in adjust mode the already split ones whose values changed. */
  private readonly pending = computed(() =>
    this.columns()
      .filter(({ kpi }) => !this.isLocked(kpi.id))
      .map((column) => ({ ...column, adjust: this.allocatedKpis().has(column.kpi.id) }))
      .filter(({ kpi, adjust }) => !adjust || this.changed(kpi.id)),
  );
  protected readonly canSave = computed(() => {
    const adjusting = this.pending().filter(({ adjust }) => adjust);
    return (
      !this.saving() &&
      this.pending().length > 0 &&
      this.shortfalls().length === 0 &&
      adjusting.every(({ kpi }) => !this.blanked(kpi.id)) &&
      (adjusting.length === 0 || this.reason().trim() !== '')
    );
  });

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
            periods: this.evaluationApi.periods(),
            kpis: fetchAllPages((page, pageSize) => this.kpiApi.list({ page, pageSize })),
            teams: fetchAllPages((page, pageSize) =>
              this.departmentApi.list({
                page,
                pageSize,
                parentDepartmentId: me.departmentId,
                status: 'ACTIVE',
              }),
            ).pipe(map((all) => all.filter((d) => d.parentDepartmentId === me.departmentId))),
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
                      ).pipe(map((members) => [team.id, members] as const)),
                    ),
                  )
              ).pipe(map((members) => ({ ...data, me, members: Object.fromEntries(members) }))),
            ),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ periods, kpis, employees, teams, members, me }) => {
          this.departmentId = me.departmentId;
          this.departmentName.set(me.departmentName);
          this.kpis.set(kpis);
          this.employees.set(employees);
          this.teams.set(teams);
          this.teamMembers.set(members);
          const quarters = periods
            .filter((p) => p.periodType === 'QUARTER')
            .sort((a, b) => b.startDate.localeCompare(a.startDate));
          this.periods.set(quarters);
          const query = this.route.snapshot.queryParamMap;
          const wanted = findQuarterPeriod(
            quarters,
            Number(query.get('year')),
            Number(query.get('quarter')),
          );
          const today = new Date().toISOString().slice(0, 10);
          const initial =
            wanted ??
            quarters.find((p) => p.startDate <= today && today <= p.endDate) ??
            quarters[0];
          if (initial) {
            this.selectPeriod(initial.id);
          } else {
            this.loading.set(false);
          }
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected selectPeriod(id: string): void {
    this.periodId.set(id);
    const period = this.periods().find((p) => p.id === id);
    if (period) {
      void this.router.navigate([], {
        queryParams: { year: periodYear(period), quarter: periodQuarter(period) },
        replaceUrl: true,
      });
    }
    this.loading.set(true);
    this.failed.set(false);
    this.notice.set(null);
    this.selectedId.set(null);
    this.editing.set(false);
    this.reason.set('');
    this.pins.set(new Set());
    this.targetApi
      .list({ evaluationPeriodId: id })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (rows) => {
          this.assignments.set(rows);
          this.values.set(this.initialValues());
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected cell(employeeId: string, kpiId: string): number | null {
    return this.values()[employeeId]?.[kpiId] ?? null;
  }

  /**
   * Sets a cell by hand and pins it; the others of the same team (or of the department's own employees)
   * that are not pinned take the difference, so the group keeps the total it had.
   */
  protected edit(employeeId: string, kpiId: string, raw: number | string | null): void {
    const parsed = raw === null || raw === '' ? null : Math.max(0, Math.floor(Number(raw)));
    const peers = this.peersOf(employeeId);
    const total = peers.team ? this.teamTotal(peers.team, kpiId) : this.sumOf(peers.people, kpiId);
    this.setValue(employeeId, kpiId, parsed !== null && Number.isFinite(parsed) ? parsed : null);
    this.setPinned(employeeId, kpiId, parsed !== null);
    if (parsed !== null && total !== null && total > 0) this.balance(peers.people, kpiId, total);
  }

  protected pinned(employeeId: string, kpiId: string): boolean {
    return this.pins().has(`${employeeId}|${kpiId}`);
  }

  protected unpin(employeeId: string, kpiId: string): void {
    this.setPinned(employeeId, kpiId, false);
  }

  /** Spreads what the pinned cells leave of the department's target over everyone else, across teams. */
  protected balanceAll(): void {
    const open = this.columns().filter(({ kpi }) => !this.isLocked(kpi.id));
    for (const { kpi, parent } of open) this.balance(this.everyone(), kpi.id, parent.targetValue);
    const pinned = this.pins().size;
    this.inform(
      this.i18n.translate(
        pinned > 0 ? 'targetAllocation.balancedPinned' : 'targetAllocation.balanced',
        { free: this.everyone().length * open.length - pinned, pinned },
      ),
    );
  }

  /** An allocated KPI stays read-only until the manager chooses to adjust it. */
  protected isLocked(kpiId: string): boolean {
    return !this.editing() && this.allocatedKpis().has(kpiId);
  }

  /**
   * A team's share: the total of its employees' cells, or, while none of them has a value yet, the share
   * the team already holds (null if it has none).
   */
  protected teamTotal(teamId: string, kpiId: string): number | null {
    const cells = (this.teamMembers()[teamId] ?? []).map((member) => this.cell(member.id, kpiId));
    if (cells.some((value) => value !== null)) {
      return cells.reduce<number>((sum, value) => sum + (value ?? 0), 0);
    }
    return this.teamAssignment(teamId, kpiId)?.targetValue ?? null;
  }

  /** What is still to hand out of the department's target for a KPI: positive = short, negative = over. */
  protected remaining(kpiId: string): number {
    const target = this.columns().find(({ kpi }) => kpi.id === kpiId)?.parent.targetValue ?? 0;
    const shares = [
      ...this.employees().map((employee) => this.cell(employee.id, kpiId)),
      ...this.teams().map((team) => this.teamTotal(team.id, kpiId)),
    ];
    return remainingOf(target, shares);
  }

  protected allocated(kpiId: string, target: number): number {
    return target - this.remaining(kpiId);
  }

  /**
   * Equal split of the target between every employee, or one weighted by working days (the same
   * calendar for everyone today).
   */
  protected split(byWorkingDays: boolean): void {
    const everyone = this.everyone();
    this.pins.set(new Set());
    const weights = everyone.map(() => (byWorkingDays ? this.workDays() : 1));
    for (const { kpi, parent } of this.columns()) {
      if (this.isLocked(kpi.id)) continue;
      const parts = splitByWeights(parent.targetValue, weights);
      everyone.forEach((employee, index) => this.setValue(employee.id, kpi.id, parts[index]));
    }
    this.inform(
      this.i18n.translate(
        byWorkingDays ? 'targetAllocation.splitByDaysDone' : 'targetAllocation.splitEqualDone',
        { count: everyone.length },
      ),
    );
  }

  private inform(text: string): void {
    this.notice.set({ kind: 'success', text });
  }

  protected save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    this.notice.set(null);
    const drafts = this.values();
    const reason = this.reason().trim();
    const kpiIds = this.pending().map(({ kpi }) => kpi.id);
    const departmentLevel = this.pending().map(({ kpi, parent, adjust }) => {
      const allocations = [
        ...this.employees().flatMap(({ id }) =>
          this.share({ employeeId: id }, drafts[id]?.[kpi.id]),
        ),
        ...this.teams().flatMap(({ id }) =>
          this.share({ departmentId: id }, this.teamTotal(id, kpi.id)),
        ),
      ];
      return adjust
        ? this.targetApi.reallocate(parent.id, { allocations, changeReason: reason })
        : this.targetApi.allocate(parent.id, { allocations });
    });
    forkJoin(departmentLevel)
      .pipe(
        // The teams' own targets exist (or have a new version) only now, so reload them to split further.
        switchMap(() => this.targetApi.list({ evaluationPeriodId: this.periodId() })),
        switchMap((rows) => {
          const requests = this.teamRequests(rows, drafts, kpiIds, reason);
          return requests.length > 0 ? forkJoin(requests) : of([]);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.selectPeriod(this.periodId());
          this.notice.set({ kind: 'success', text: this.localization.targetAllocation.saved });
        },
        error: () => {
          this.saving.set(false);
          this.notice.set({ kind: 'error', text: this.localization.targetAllocation.saveFailed });
        },
      });
  }

  /** Splits each team's current target among its employees: a first split, or a changed one. */
  private teamRequests(
    rows: readonly TargetAssignment[],
    drafts: CellValues,
    kpiIds: readonly string[],
    reason: string,
  ) {
    return kpiIds.flatMap((kpiId) =>
      this.teams().flatMap((team) => {
        const parent = rows.find(
          (a) => a.departmentId === team.id && a.kpiDefinitionId === kpiId && a.status === 'ACTIVE',
        );
        const allocations = (this.teamMembers()[team.id] ?? []).flatMap(({ id }) =>
          this.share({ employeeId: id }, drafts[id]?.[kpiId]),
        );
        if (!parent || allocations.length === 0) return [];
        const current = rows.filter(
          (a) => a.parentAssignmentId === parent.id && a.status === 'ACTIVE',
        );
        if (current.length === 0) return [this.targetApi.allocate(parent.id, { allocations })];
        const same =
          current.length === allocations.length &&
          allocations.every(({ employeeId, targetValue }) =>
            current.some((a) => a.employeeId === employeeId && a.targetValue === targetValue),
          );
        return same
          ? []
          : [this.targetApi.reallocate(parent.id, { allocations, changeReason: reason })];
      }),
    );
  }

  /** One allocation row for an owner, or nothing while it has no value. */
  private share<Owner extends object>(
    owner: Owner,
    value: number | null | undefined,
  ): (Owner & { targetValue: number })[] {
    return value === null || value === undefined ? [] : [{ ...owner, targetValue: value }];
  }

  protected startAdjust(): void {
    this.editing.set(true);
  }

  protected cancelAdjust(): void {
    this.editing.set(false);
    this.reason.set('');
    this.pins.set(new Set());
    this.values.set(this.initialValues());
  }

  protected select(employeeId: string): void {
    this.selectedId.set(employeeId);
    const first = this.columns()[0];
    this.selectKpi(this.selectedKpiId() || first?.kpi.id || '');
  }

  protected selectKpi(kpiId: string): void {
    this.selectedKpiId.set(kpiId);
    const employeeId = this.selectedId();
    const own = this.saved().find(
      (a) => a.employeeId === employeeId && a.kpiDefinitionId === kpiId,
    );
    if (!own) {
      this.breakdown.set({ state: 'idle', data: null });
      return;
    }
    this.breakdown.set({ state: 'loading', data: null });
    this.targetApi
      .breakdown(own.id)
      .pipe(
        catchError(() => of(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((data) => {
        if (this.selectedId() !== employeeId || this.selectedKpiId() !== kpiId) return;
        this.breakdown.set(data ? { state: 'ready', data } : { state: 'failed', data: null });
      });
  }

  protected closePanel(): void {
    this.selectedId.set(null);
  }

  protected selectedName(): string {
    const id = this.selectedId();
    return this.everyone().find((e) => e.id === id)?.fullName ?? '';
  }

  protected memberFeedback(employeeId: string): TargetFeedbackStatus | null {
    return this.feedbackOf(employeeId);
  }

  protected feedbackLabel(status: TargetFeedbackStatus | null): string {
    const text = this.localization.targetAllocation;
    return status === null
      ? text.feedbackNone
      : {
          CONFIRMED: text.feedbackConfirmed,
          COMMENTED: text.feedbackCommented,
          NOT_VIEWED: text.feedbackNotViewed,
        }[status];
  }

  protected shortfallText({ kpi, remaining }: Shortfall): string {
    return this.i18n.translate(
      remaining > 0 ? 'targetAllocation.sumShort' : 'targetAllocation.sumOver',
      {
        kpi: kpi.name,
        amount: `${this.i18n.formatNumber(Math.abs(remaining))} ${kpi.unit}`,
      },
    );
  }

  /** The team an employee belongs to (null for the department's own employees) with that whole group. */
  private peersOf(employeeId: string): { team: string | null; people: EmployeeDetail[] } {
    const team = Object.entries(this.teamMembers()).find(([, members]) =>
      members.some((member) => member.id === employeeId),
    );
    return team ? { team: team[0], people: team[1] } : { team: null, people: this.employees() };
  }

  /** Total of the people's cells, or null while none has a value. */
  private sumOf(people: readonly EmployeeDetail[], kpiId: string): number | null {
    const cells = people.map((person) => this.cell(person.id, kpiId));
    return cells.some((value) => value !== null)
      ? cells.reduce<number>((sum, value) => sum + (value ?? 0), 0)
      : null;
  }

  /** Gives the people who are not pinned an equal part of what the pinned ones leave of `total`. */
  private balance(people: readonly EmployeeDetail[], kpiId: string, total: number): void {
    const free = people.filter((person) => !this.pinned(person.id, kpiId));
    const pinnedSum = people
      .filter((person) => this.pinned(person.id, kpiId))
      .reduce((sum, person) => sum + (this.cell(person.id, kpiId) ?? 0), 0);
    const parts = splitByWeights(
      Math.max(0, total - pinnedSum),
      free.map(() => 1),
    );
    free.forEach((person, index) => this.setValue(person.id, kpiId, parts[index]));
  }

  private setPinned(employeeId: string, kpiId: string, pinned: boolean): void {
    this.pins.update((all) => {
      const next = new Set(all);
      if (pinned) next.add(`${employeeId}|${kpiId}`);
      else next.delete(`${employeeId}|${kpiId}`);
      return next;
    });
  }

  private teamAssignment(teamId: string, kpiId: string): TargetAssignment | undefined {
    return this.assignments().find(
      (a) => a.departmentId === teamId && a.kpiDefinitionId === kpiId && a.status === 'ACTIVE',
    );
  }

  private savedValue(employeeId: string, kpiId: string): number | null {
    return (
      this.saved().find((a) => a.employeeId === employeeId && a.kpiDefinitionId === kpiId)
        ?.targetValue ?? null
    );
  }

  /** Whether any employee's value for the KPI differs from what is saved. */
  private changed(kpiId: string): boolean {
    return this.everyone().some(
      (employee) => this.cell(employee.id, kpiId) !== this.savedValue(employee.id, kpiId),
    );
  }

  /** An adjustment must list every employee that already has a share, so none may be emptied. */
  private blanked(kpiId: string): boolean {
    return this.everyone().some(
      (employee) =>
        this.savedValue(employee.id, kpiId) !== null && this.cell(employee.id, kpiId) === null,
    );
  }

  private feedbackOf(employeeId: string): TargetFeedbackStatus | null {
    const own = this.saved().filter((a) => a.employeeId === employeeId);
    return own.length === 0 ? null : employeeFeedback(own);
  }

  private setValue(employeeId: string, kpiId: string, value: number | null): void {
    this.values.update((all) => ({
      ...all,
      [employeeId]: { ...all[employeeId], [kpiId]: value },
    }));
  }

  private initialValues(): CellValues {
    const result: CellValues = {};
    for (const row of this.saved()) {
      result[row.employeeId as string] = {
        ...result[row.employeeId as string],
        [row.kpiDefinitionId]: row.targetValue,
      };
    }
    return result;
  }
}
