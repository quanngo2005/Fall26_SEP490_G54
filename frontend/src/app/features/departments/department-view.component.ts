import { Location } from '@angular/common';
import { HttpParams } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, EMPTY, finalize, forkJoin, Subject, switchMap } from 'rxjs';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentService } from '../../core/department.service';
import {
  COMPARE_MODES,
  DepartmentMemberFilter,
  DepartmentMembers,
  DepartmentOverview,
  DepartmentSummary,
  MemberSort,
  MemberStatus,
  PerformanceCompareMode,
} from '../../core/department.models';
import { PerfAttentionComponent } from '../performance-dashboard/perf-attention.component';
import {
  formatArrow,
  formatValue,
  NOT_AVAILABLE,
  toneOf,
} from '../performance-dashboard/perf-display';
import { PerfKpiBelowPlanComponent } from '../performance-dashboard/perf-kpi-below-plan.component';
import { PerfKpiGroupTableComponent } from '../performance-dashboard/perf-kpi-group-table.component';
import { PerfMemberTableComponent } from '../performance-dashboard/perf-member-table.component';
import { PerfMetricCardComponent } from '../performance-dashboard/perf-metric-card.component';
import { PerfProfileGroupsComponent } from '../performance-dashboard/perf-profile-groups.component';
import { PerfTrendChartComponent } from '../performance-dashboard/perf-trend-chart.component';
import { PerfWorkProgressComponent } from '../performance-dashboard/perf-work-progress.component';
import { kpiOnPlan, saveFile } from '../performance-dashboard/perf-page';

/**
 * SCR-PRF-02 Department performance (department manager). Every figure comes from
 * `GET /departments/{id}/overview` and `/members` (docs/api/DEPARTMENT_CONTRACT.md); changing the department, period
 * or comparison reloads both, member filters reload the member list only. Errors show a retry state, never sample data.
 */
@Component({
  selector: 'app-department-view',
  imports: [
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    PerfMetricCardComponent,
    PerfWorkProgressComponent,
    PerfKpiBelowPlanComponent,
    PerfAttentionComponent,
    PerfTrendChartComponent,
    PerfKpiGroupTableComponent,
    PerfProfileGroupsComponent,
    PerfMemberTableComponent,
  ],
  templateUrl: './department-view.component.html',
  styleUrls: ['../../shared/ui/ui-controls.scss', './department-view.component.scss'],
})
export class DepartmentViewComponent extends LocalizedComponent {
  private readonly api = inject(DepartmentService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly reload = new Subject<void>();
  private readonly reloadMembers = new Subject<void>();
  protected readonly departments = signal<DepartmentSummary[]>([]);
  protected readonly overview = signal<DepartmentOverview | null>(null);
  protected readonly members = signal<DepartmentMembers>({
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 20,
  });
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly membersFailed = signal(false);
  protected readonly exporting = signal(false);
  protected readonly exportFailed = signal(false);
  protected readonly compareModes = COMPARE_MODES;
  protected readonly statuses: readonly MemberStatus[] = ['ACTIVE', 'ON_LEAVE', 'INACTIVE'];
  protected readonly sorts: readonly MemberSort[] = ['CHANGE_DESC', 'TOTAL_ASC', 'CODE'];
  protected departmentId = '';
  protected periodId = '';
  protected compare: PerformanceCompareMode = 'PREVIOUS';
  protected keyword = '';
  protected jobPositionId = '';
  protected status: MemberStatus | '' = '';
  protected sort: MemberSort = 'CHANGE_DESC';
  protected page = 1;

  protected readonly compareName = computed(() => this.overview()?.comparePeriod?.name ?? null);
  protected readonly kpiCount = computed(() => kpiOnPlan(this.overview()));

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          return forkJoin({
            overview: this.api.overview(
              this.departmentId,
              this.periodId || undefined,
              this.compare,
            ),
            members: this.api.members(this.departmentId, this.memberFilter()),
          }).pipe(
            catchError(() => {
              this.failed.set(true);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.overview.set(result.overview);
        this.members.set(result.members);
        this.membersFailed.set(false);
        this.periodId = result.overview.period?.id ?? '';
        this.syncUrl(result.overview);
      });
    this.reloadMembers
      .pipe(
        switchMap(() => {
          this.membersFailed.set(false);
          return this.api.members(this.departmentId, this.memberFilter()).pipe(
            catchError(() => {
              this.membersFailed.set(true);
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((members) => this.members.set(members));
    this.loadDepartments();
  }

  protected loadDepartments(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api
      .list({ pageSize: 100 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.departments.set(list.items);
          const query = this.route.snapshot.queryParamMap;
          const requestedCode = query.get('department');
          const legacyId = query.get('departmentId');
          this.periodId = query.get('periodId') ?? '';
          const compare = query.get('compare');
          this.compare = COMPARE_MODES.find((mode) => mode === compare) ?? 'PREVIOUS';
          this.departmentId =
            list.items.find((d) => d.code === requestedCode)?.id ??
            list.items.find((d) => d.id === legacyId)?.id ??
            list.items.find((d) => d.levelType === 'DEPARTMENT')?.id ??
            list.items[0]?.id ??
            '';
          if (this.departmentId) this.refresh();
          else this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  /** Reloads the overview and the first member page. */
  protected refresh(): void {
    this.page = 1;
    this.reload.next();
  }

  protected changeDepartment(): void {
    this.periodId = '';
    this.refresh();
  }

  protected filterMembers(): void {
    this.page = 1;
    this.reloadMembers.next();
  }

  protected nextPage(delta: number): void {
    this.page += delta;
    this.reloadMembers.next();
  }

  protected totalPages(): number {
    const { totalCount, pageSize } = this.members();
    return Math.max(1, Math.ceil(totalCount / (pageSize || 1)));
  }

  protected scrollTo(id: string): void {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected open(route: string): void {
    void this.router.navigateByUrl(route);
  }

  protected exportExcel(data: DepartmentOverview): void {
    this.exporting.set(true);
    this.exportFailed.set(false);
    this.api
      .exportOverview(this.departmentId, data.period?.id, this.compare)
      .pipe(
        finalize(() => this.exporting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (blob) =>
          saveFile(blob, `hieu-suat-${data.department.code}-${data.period?.code ?? 'na'}.xlsx`),
        error: () => this.exportFailed.set(true),
      });
  }

  protected score(value: number | null | undefined): string {
    return formatValue(this.i18n.currentLanguage(), value);
  }

  protected delta(value: number | null): string | null {
    const name = this.compareName();
    return name == null || value == null
      ? null
      : `${formatArrow(this.i18n.currentLanguage(), value)} ${this.localization.departmentPerformance.vs} ${name}`;
  }

  protected tone(value: number | null) {
    return toneOf(value);
  }

  protected ratio(part: number | null, whole: number | null): string {
    return part == null || whole == null || whole === 0 ? NOT_AVAILABLE : `${part}/${whole}`;
  }

  /** Pending review + overdue, plus escalated once the API has a source for it. */
  protected myQueue(data: DepartmentOverview): number {
    const op = data.operational;
    return op.pendingReview + op.overdue + (op.escalated ?? 0);
  }

  protected compareLabel(mode: PerformanceCompareMode): string {
    const text = this.localization.departmentPerformance;
    return mode === 'SAME_LAST_YEAR' ? text.compareSameLastYear : text.comparePrevious;
  }

  protected statusLabel(status: MemberStatus): string {
    const text = this.localization.departmentPerformance;
    switch (status) {
      case 'ACTIVE':
        return text.statusActive;
      case 'ON_LEAVE':
        return text.statusOnLeave;
      case 'INACTIVE':
        return text.statusInactive;
      default:
        return text.statusTerminated;
    }
  }

  protected sortLabel(sort: MemberSort): string {
    const text = this.localization.departmentPerformance;
    switch (sort) {
      case 'TOTAL_ASC':
        return text.sortTotalAsc;
      case 'CODE':
        return text.sortCode;
      default:
        return text.sortChangeDesc;
    }
  }

  private memberFilter(): DepartmentMemberFilter {
    return {
      keyword: this.keyword || undefined,
      jobPositionId: this.jobPositionId || undefined,
      status: this.status || undefined,
      periodId: this.periodId || undefined,
      compare: this.compare,
      sort: this.sort,
      page: this.page,
    };
  }

  /** Keeps department, period and comparison in the address so links from SCR-PRF-03 open the same view. */
  private syncUrl(data: DepartmentOverview): void {
    const params = new HttpParams({
      fromObject: {
        department: data.department.code,
        ...(data.period ? { periodId: data.period.id } : {}),
        compare: this.compare,
      },
    });
    this.location.replaceState(this.location.path().split('?')[0], params.toString());
  }
}
