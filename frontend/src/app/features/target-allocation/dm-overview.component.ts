import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { forkJoin, of, switchMap } from 'rxjs';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { KpiGroupTableComponent } from '../../shared/target/kpi-group-table/kpi-group-table.component';
import { PaceBarComponent } from '../../shared/target/pace-bar/pace-bar.component';
import { TargetMoneyPipe } from '../../shared/target/pipes/target-money.pipe';
import { KpiGroupTableGroup, KpiGroupTableRow } from '../../shared/target/target-shared.models';
import {
  availableYears,
  fetchAllPages,
  findYearPeriod,
} from '../target-overview/target-overview.utils';
import { TargetAllocationApiService } from './target-allocation-api.service';
import { EmployeeAllocation } from './target-allocation.models';

type PeriodFilter = 'YEAR' | 'Q1' | 'Q2' | 'Q3' | 'Q4';

interface PositionSummary {
  readonly label: string;
  readonly matched: number;
  readonly total: number;
}

/**
 * W9 (Target Allocation v3): the department manager's tier-3 overview — summary cards, KPI table
 * grouped by KPI group with an "Allocated" bar and "Remaining", and the employee feedback panel —
 * before diving into the KPI x employee grid (W10, `./target-allocation.component.ts`).
 * `docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md`, Figma node 3486:17071.
 */
@Component({
  selector: 'app-target-allocation-dm-overview',
  imports: [
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    TargetMoneyPipe,
    KpiGroupTableComponent,
    PaceBarComponent,
  ],
  templateUrl: './dm-overview.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/target/target-page.scss',
    './dm-overview.component.scss',
  ],
})
export class DmOverviewComponent extends LocalizedComponent implements OnInit {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly allocationApi = inject(TargetAllocationApiService);
  private readonly evaluationApi = inject(EvaluationService);
  private readonly kpiApi = inject(KpiDefinitionService);
  private readonly employeeApi = inject(EmployeeService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly departmentName = signal('');
  private departmentId = '';
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  protected readonly year = signal<number | null>(null);
  protected readonly periodFilter = signal<PeriodFilter>('YEAR');
  private readonly kpis = signal<KpiDefinition[]>([]);
  protected readonly rows = signal<EmployeeAllocation[]>([]);
  protected readonly tab = signal<string>('');
  protected readonly filter = signal<'ALL' | 'MISMATCHED'>('ALL');

  protected readonly years = computed(() => availableYears(this.periods()));

  protected readonly tabs = computed(() => [
    ...new Set(this.rows().map((r) => this.positionOf(r))),
  ]);

  protected readonly summaries = computed((): PositionSummary[] =>
    this.tabs().map((label) => {
      const rows = this.rows().filter((r) => this.positionOf(r) === label);
      return { label, matched: rows.filter((r) => this.matched(r)).length, total: rows.length };
    }),
  );

  protected readonly newEmployeeCount = computed(() => {
    const seen = new Map<string, boolean>();
    for (const row of this.rows()) {
      for (const e of row.employees) {
        const isNew = e.previousYear === null && e.previousYearActual === null;
        seen.set(e.employeeId, (seen.get(e.employeeId) ?? true) && isNew);
      }
    }
    return [...seen.values()].filter(Boolean).length;
  });

  protected readonly tabRows = computed(() =>
    this.rows().filter((row) => this.positionOf(row) === (this.tab() || this.tabs()[0])),
  );

  protected readonly mismatchedCount = computed(
    () => this.tabRows().filter((r) => !this.matched(r)).length,
  );

  protected readonly groups = computed((): KpiGroupTableGroup[] => {
    const groups = new Map<string, { name: string; rows: KpiGroupTableRow[] }>();
    for (const row of this.tabRows()) {
      if (this.filter() === 'MISMATCHED' && this.matched(row)) continue;
      const kpi = this.kpis().find((k) => k.id === row.kpiId);
      const groupId = kpi?.kpiGroupId ?? 'other';
      const groupName = kpi?.kpiGroupName ?? this.localization.targetDm.groupOther;
      const entry = groups.get(groupId) ?? { name: groupName, rows: [] as KpiGroupTableRow[] };
      entry.rows.push({ id: row.kpiId, kpiName: row.kpiName, kpiType: row.unit });
      groups.set(groupId, entry);
    }
    return [...groups].map(([id, { name, rows }]) => ({
      id,
      name,
      complete: rows.every((r) => this.matched(this.rows().find((x) => x.kpiId === r.id)!)),
      rows,
    }));
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
          }).pipe(switchMap((data) => of({ ...data, me }))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ periods, kpis, me }) => {
          this.departmentId = me.departmentId;
          this.departmentName.set(me.departmentName);
          this.kpis.set(kpis);
          this.periods.set(periods);
          const query = this.route.snapshot.queryParamMap;
          this.year.set(Number(query.get('year')) || new Date().getFullYear());
          this.loadYear();
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected selectYear(year: number): void {
    this.year.set(year);
    void this.router.navigate([], { queryParams: { year }, replaceUrl: true });
    this.loadYear();
  }

  protected selectPeriodFilter(filter: PeriodFilter): void {
    this.periodFilter.set(filter);
  }

  protected selectTab(label: string): void {
    this.tab.set(label);
  }

  protected setFilter(filter: 'ALL' | 'MISMATCHED'): void {
    this.filter.set(filter);
  }

  protected goToAllocation(): void {
    void this.router.navigate(['/target-allocation'], { queryParams: { year: this.year() } });
  }

  protected rowById(kpiId: string): EmployeeAllocation | undefined {
    return this.rows().find((r) => r.kpiId === kpiId);
  }

  /** The row's department target in the current period filter, for the fixed "Mục tiêu phòng" column. */
  protected targetFor(kpiId: string): number {
    const row = this.rowById(kpiId);
    return row ? this.targetForPeriod(row, this.periodFilter()) : 0;
  }

  /** Allocated % of the department target for the current period filter (0-100+). */
  protected allocatedPercent(row: EmployeeAllocation): number {
    const target = this.targetForPeriod(row, this.periodFilter());
    if (target === 0) return 0;
    const sum = row.employees.reduce((acc, e) => acc + this.valueFor(e, this.periodFilter()), 0);
    return Math.round((sum / target) * 1000) / 10;
  }

  protected remainingAmount(row: EmployeeAllocation): number {
    const target = this.targetForPeriod(row, this.periodFilter());
    const sum = row.employees.reduce((acc, e) => acc + this.valueFor(e, this.periodFilter()), 0);
    return target - sum;
  }

  private targetForPeriod(row: EmployeeAllocation, period: PeriodFilter): number {
    return period === 'YEAR' ? row.department.year : row.department[period.toLowerCase() as 'q1'];
  }

  private valueFor(e: EmployeeAllocation['employees'][number], period: PeriodFilter): number {
    return period === 'YEAR' ? e.year : e[period.toLowerCase() as 'q1'];
  }

  private matched(row: EmployeeAllocation): boolean {
    const kpi = this.kpis().find((k) => k.id === row.kpiId);
    if (kpi?.aggregationType === 'RATIO' || kpi?.aggregationType === 'NO_AGGREGATION') return true;
    return (
      row.remaining.year === 0 &&
      row.remaining.q1 === 0 &&
      row.remaining.q2 === 0 &&
      row.remaining.q3 === 0 &&
      row.remaining.q4 === 0 &&
      row.employees.every((e) => e.matched)
    );
  }

  private positionOf(row: EmployeeAllocation): string {
    return row.employees[0]?.jobPositionName ?? this.localization.targetDm.positionOther;
  }

  private loadYear(): void {
    this.loading.set(true);
    this.failed.set(false);
    const year = this.year();
    const yearPeriod = year === null ? undefined : findYearPeriod(this.periods(), year);
    if (!yearPeriod) {
      this.rows.set([]);
      this.loading.set(false);
      return;
    }
    this.targetApi
      .list({
        departmentId: this.departmentId,
        evaluationPeriodId: yearPeriod.id,
        status: 'ACTIVE',
      })
      .pipe(
        switchMap((assignments) => {
          const anchor = assignments.find((a) => !a.employeeId)?.id ?? null;
          return anchor ? this.allocationApi.getEmployeeAllocations(anchor) : of([]);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (rows) => {
          this.rows.set(rows);
          this.tab.set(this.tabs()[0] ?? '');
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }
}
