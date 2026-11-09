import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, EMPTY, finalize, forkJoin, map, of, Subject, switchMap } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { JobPositionSummary } from '../../core/job-position.models';
import { JobPositionService } from '../../core/job-position.service';
import { KpiOption, WorkTypeDetail, WorkTypeStatus } from '../../core/work-type.models';
import { WorkTypeService } from '../../core/work-type.service';
import { OPEN_WORK_STATUSES } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { WorkTypeDetailDrawerComponent } from './work-type-detail-drawer.component';

/** SCR-CFG-02: system administrator work type list. */
@Component({
  selector: 'app-work-type-list',
  standalone: true,
  imports: [FormsModule, HeaderComponent, SidebarComponent, WorkTypeDetailDrawerComponent],
  templateUrl: './work-type-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './work-type-list.component.scss',
  ],
})
export class WorkTypeListComponent extends LocalizedComponent {
  private readonly api = inject(WorkTypeService);
  private readonly positionsApi = inject(JobPositionService);
  private readonly worksApi = inject(WorkService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  protected readonly allWorkTypes = signal<WorkTypeDetail[]>([]);
  protected readonly positions = signal<JobPositionSummary[]>([]);
  protected readonly positionWorkTypeIds = signal<Record<string, readonly string[]>>({});
  protected readonly workTypePositionNames = signal<Record<string, readonly string[]>>({});
  protected readonly kpiOptions = signal<KpiOption[]>([]);
  protected readonly openWorkCounts = signal<Record<string, number>>({});

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly selectedPositionId = signal<string>('');
  protected readonly selectedKpiId = signal<string>('');
  protected readonly filterKeyword = signal<string>('');
  protected readonly filterStatus = signal<WorkTypeStatus | ''>('ACTIVE');
  protected readonly currentPage = signal(1);
  protected readonly currentPageSize = signal(20);

  protected readonly pageSizes = [10, 20, 50];
  protected keyword = '';
  protected status: WorkTypeStatus | '' = 'ACTIVE';
  protected kpiId = '';
  protected page = 1;
  protected pageSize = 20;

  protected readonly activeCount = computed(
    () => this.allWorkTypes().filter((item) => item.status === 'ACTIVE').length,
  );
  protected readonly inactiveCount = computed(
    () => this.allWorkTypes().filter((item) => item.status !== 'ACTIVE').length,
  );
  protected readonly subtitleText = computed(() =>
    this.i18n.translate('workTypes.subtitleCounts', {
      active: this.activeCount(),
      inactive: this.inactiveCount(),
    }),
  );

  protected readonly filteredWorkTypes = computed(() => {
    const keyword = this.filterKeyword().toLowerCase();
    const status = this.filterStatus();
    const posId = this.selectedPositionId();
    const kpiId = this.selectedKpiId();
    const posWorkIds = posId ? new Set(this.positionWorkTypeIds()[posId] ?? []) : null;

    return this.allWorkTypes().filter((item) => {
      if (status && item.status !== status) return false;
      if (posWorkIds && !posWorkIds.has(item.id)) return false;
      if (kpiId && !item.kpis.some((k) => k.kpiDefinitionId === kpiId)) return false;
      if (keyword) {
        const matchCode = item.code.toLowerCase().includes(keyword);
        const matchName = item.name.toLowerCase().includes(keyword);
        const matchDesc = item.description?.toLowerCase().includes(keyword) ?? false;
        if (!matchCode && !matchName && !matchDesc) return false;
      }
      return true;
    });
  });

  protected readonly totalCount = computed(() => this.filteredWorkTypes().length);
  protected readonly workTypes = computed(() => {
    const start = (this.currentPage() - 1) * this.currentPageSize();
    return this.filteredWorkTypes().slice(start, start + this.currentPageSize());
  });
  protected readonly selected = computed(
    () => this.allWorkTypes().find((item) => item.id === this.selectedId()) ?? null,
  );

  protected totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount() / this.currentPageSize()));
  }

  protected readonly rangeText = computed(() => {
    const total = this.totalCount();
    const first = total === 0 ? 0 : (this.currentPage() - 1) * this.currentPageSize() + 1;
    const last = Math.min(total, first + this.workTypes().length - 1);
    return this.i18n.translate('workTypes.range', { first, last, total });
  });

  constructor() {
    super();
    this.loadLookups();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          return this.api.list({ page: 1, pageSize: 100 }).pipe(
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
        this.allWorkTypes.set(result.items);
        this.loadOpenCounts(result.items);
      });
    this.reload.next();
  }

  protected load(): void {
    this.reload.next();
  }

  protected applyFilter(): void {
    this.page = 1;
    this.currentPage.set(1);
    this.currentPageSize.set(this.pageSize);
    this.filterKeyword.set(this.keyword.trim());
    this.filterStatus.set(this.status);
    this.selectedKpiId.set(this.kpiId);
  }

  protected selectPositionTab(positionId: string): void {
    this.selectedPositionId.set(positionId);
    this.page = 1;
    this.currentPage.set(1);
  }

  protected clearFilter(): void {
    this.keyword = '';
    this.status = 'ACTIVE';
    this.kpiId = '';
    this.selectedPositionId.set('');
    this.applyFilter();
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.page) return;
    this.page = page;
    this.currentPage.set(page);
  }

  protected positionCount(position: JobPositionSummary): number {
    const mapped = this.positionWorkTypeIds()[position.id];
    return mapped ? mapped.length : position.workTypeCount;
  }

  protected positionLabel(workType: WorkTypeDetail): string {
    const names = this.workTypePositionNames()[workType.id];
    return names && names.length > 0 ? names.join(', ') : this.localization.workTypes.notAvailable;
  }

  protected resultUnitLabel(workType: WorkTypeDetail): string {
    const firstKpi = workType.kpis[0];
    if (!firstKpi) return this.localization.workTypes.notAvailable;
    const opt = this.kpiOptions().find((k) => k.id === firstKpi.kpiDefinitionId);
    return opt?.unit || this.localization.workTypes.notAvailable;
  }

  protected transactionCifLabel(workType: WorkTypeDetail): string {
    const t = this.localization.workTypes;
    const required = workType.productCode != null || workType.kpis.some((k) => k.isRequired);
    return required ? t.cifRequired : t.cifNone;
  }

  protected openCount(workTypeId: string): number | null {
    const count = this.openWorkCounts()[workTypeId];
    return count !== undefined ? count : null;
  }

  protected statusLabel(status: WorkTypeStatus): string {
    const t = this.localization.workTypes;
    return status === 'ACTIVE'
      ? t.statusActive
      : status === 'INACTIVE'
        ? t.statusInactive
        : t.statusArchived;
  }

  protected open(workType: WorkTypeDetail): void {
    this.selectedId.set(this.selectedId() === workType.id ? null : workType.id);
  }

  private loadLookups(): void {
    this.api
      .kpis()
      .pipe(
        catchError(() => of([] as KpiOption[])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((kpis) => this.kpiOptions.set(kpis));

    this.positionsApi
      .list({ status: 'ACTIVE', page: 1, pageSize: 100 })
      .pipe(
        switchMap((res) => {
          this.positions.set(res.items);
          if (res.items.length === 0) return of([]);
          return forkJoin(
            res.items.map((pos) =>
              this.positionsApi.workTypes(pos.id).pipe(
                map((wts) => ({ pos, wts })),
                catchError(() => of({ pos, wts: [] })),
              ),
            ),
          );
        }),
        catchError(() => of([])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((entries) => {
        const byPos: Record<string, string[]> = {};
        const byWt: Record<string, string[]> = {};
        for (const { pos, wts } of entries) {
          byPos[pos.id] = wts.map((w) => w.id);
          for (const wt of wts) {
            const list = byWt[wt.id] ?? (byWt[wt.id] = []);
            if (!list.includes(pos.title)) list.push(pos.title);
          }
        }
        this.positionWorkTypeIds.set(byPos);
        this.workTypePositionNames.set(byWt);
      });
  }

  private loadOpenCounts(items: readonly WorkTypeDetail[]): void {
    if (items.length === 0) return;
    forkJoin(
      items.map((wt) =>
        this.worksApi
          .list({ workTypeId: wt.id, status: OPEN_WORK_STATUSES, page: 1, pageSize: 1 })
          .pipe(
            map((res) => [wt.id, res.totalCount] as const),
            catchError(() => of(null)),
          ),
      ),
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((pairs) => {
        const next: Record<string, number> = {};
        for (const pair of pairs) {
          if (pair) next[pair[0]] = pair[1];
        }
        this.openWorkCounts.set(next);
      });
  }
}
