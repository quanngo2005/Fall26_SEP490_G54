import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, of, Subject, switchMap } from 'rxjs';
import { DepartmentService } from '../../core/department.service';
import { DepartmentSummary } from '../../core/department.models';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { TargetAssignment } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { TargetCellComponent } from './target-cell.component';
import {
  availableYears,
  buildBranchTable,
  fetchAllPages,
  findQuarterPeriod,
  findYearPeriod,
  usedKpis,
} from './target-overview.utils';

const QUARTERS = [1, 2, 3, 4] as const;
const PAGE_SIZES = [10, 20, 50] as const;
/** Employees listed under an expanded department before the "n more" line. */
const MEMBER_PREVIEW = 3;

@Component({
  selector: 'app-branch-target-overview',
  imports: [FormsModule, TargetCellComponent],
  templateUrl: './branch-target-overview.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './target-overview.scss',
  ],
})
export class BranchTargetOverviewComponent extends LocalizedComponent implements OnInit {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly evaluationApi = inject(EvaluationService);
  private readonly kpiApi = inject(KpiDefinitionService);
  private readonly departmentApi = inject(DepartmentService);
  private readonly employeeApi = inject(EmployeeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly periodChange = new Subject<string | null>();

  protected readonly quarters = QUARTERS;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly memberPreview = MEMBER_PREVIEW;

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  private readonly periods = signal<EvaluationPeriodOption[]>([]);
  private readonly kpis = signal<KpiDefinition[]>([]);
  private readonly departments = signal<DepartmentSummary[]>([]);
  private readonly employees = signal<EmployeeDetail[]>([]);
  private readonly assignments = signal<TargetAssignment[]>([]);

  protected readonly year = signal<number | null>(null);
  /** 0 = the whole year. */
  protected readonly quarter = signal(0);
  protected readonly search = signal('');
  protected readonly departmentId = signal('');
  protected readonly position = signal('');
  protected readonly pageSize = signal<number>(PAGE_SIZES[2]);
  protected readonly page = signal(1);
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  protected readonly years = computed(() => availableYears(this.periods()));
  protected readonly columns = computed(() => usedKpis(this.kpis(), this.assignments()));
  protected readonly table = computed(() =>
    buildBranchTable({
      kpis: this.columns(),
      assignments: this.assignments(),
      departments: this.departments(),
      employees: this.employees(),
      search: this.search(),
      departmentId: this.departmentId(),
      position: this.position(),
    }),
  );
  protected readonly departmentOptions = computed(() => {
    const branch = this.departments().find((d) => d.levelType === 'BRANCH');
    return this.departments().filter((d) => branch && d.parentDepartmentId === branch.id);
  });
  protected readonly positionOptions = computed(() =>
    [...new Set(this.employees().map((e) => e.jobPositionTitle))].sort(),
  );
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.table().departments.length / this.pageSize())),
  );
  protected readonly pageRows = computed(() => {
    const start = (Math.min(this.page(), this.totalPages()) - 1) * this.pageSize();
    return this.table().departments.slice(start, start + this.pageSize());
  });
  protected readonly employeeCount = computed(() =>
    this.table().departments.reduce((sum, row) => sum + row.memberCount, 0),
  );

  constructor() {
    super();
    this.periodChange
      .pipe(
        switchMap((periodId) =>
          periodId
            ? this.targetApi.list({ evaluationPeriodId: periodId }).pipe(
                catchError(() => {
                  this.failed.set(true);
                  return of([] as TargetAssignment[]);
                }),
              )
            : of([] as TargetAssignment[]),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((rows) => {
        this.assignments.set(rows);
        this.loading.set(false);
      });
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    forkJoin({
      periods: this.evaluationApi.periods(),
      kpis: fetchAllPages((page, pageSize) => this.kpiApi.list({ page, pageSize })),
      departments: fetchAllPages((page, pageSize) => this.departmentApi.list({ page, pageSize })),
      employees: fetchAllPages((page, pageSize) =>
        this.employeeApi.list({ page, pageSize, status: 'ACTIVE' }),
      ),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ periods, kpis, departments, employees }) => {
          this.periods.set(periods);
          this.kpis.set(kpis);
          this.departments.set(departments);
          this.employees.set(employees);
          this.year.set(this.year() ?? this.years()[0] ?? null);
          this.reloadAssignments();
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected selectYear(year: number): void {
    this.year.set(year);
    this.reloadAssignments();
  }

  protected selectQuarter(quarter: number): void {
    this.quarter.set(quarter);
    this.reloadAssignments();
  }

  protected toggle(departmentId: string): void {
    const next = new Set(this.expanded());
    if (!next.delete(departmentId)) {
      next.add(departmentId);
    }
    this.expanded.set(next);
  }

  protected setPageSize(size: number): void {
    this.pageSize.set(Number(size));
    this.page.set(1);
  }

  protected resetPage(): void {
    this.page.set(1);
  }

  protected columnLabel(kpi: KpiDefinition): string {
    return `${kpi.name} (${kpi.unit})`;
  }

  private reloadAssignments(): void {
    const year = this.year();
    const period =
      year === null
        ? undefined
        : this.quarter() === 0
          ? findYearPeriod(this.periods(), year)
          : findQuarterPeriod(this.periods(), year, this.quarter());
    this.loading.set(true);
    this.failed.set(false);
    this.periodChange.next(period?.id ?? null);
  }
}
