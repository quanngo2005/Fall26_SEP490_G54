import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, combineLatest, map, of, startWith, Subject, switchMap } from 'rxjs';
import { DepartmentService } from '../../core/department.service';
import { AppShellComponent } from '../../shared/layout/app-shell.component';
import {
  DataTableCellDirective,
  DataTableComponent,
  EmptyStateComponent,
  FilterBarComponent,
  FilterChange,
  FilterDefinition,
  SkeletonComponent,
  StatusBadgeComponent,
  StatusTone,
  TableColumn,
  ToastService,
} from '../../shared/ui';
import {
  DESTINATION_LABELS,
  DepartmentDestination,
  NavigationContext,
  resolveDestination,
} from './department-navigation';
import {
  DepartmentDetail,
  DepartmentDetailViewState,
  DepartmentStatus,
  EmployeePreview,
} from './department-detail.model';

const STATUS_LABELS: Record<DepartmentStatus, string> = {
  active: 'Đang hoạt động',
  inactive: 'Ngưng hoạt động',
  pending: 'Chờ xử lý',
};
const STATUS_TONES: Record<DepartmentStatus, StatusTone> = {
  active: 'success',
  inactive: 'neutral',
  pending: 'warning',
};

type LoadResult =
  | { readonly state: 'loading' }
  | { readonly state: 'success'; readonly detail: DepartmentDetail }
  | { readonly state: Exclude<DepartmentDetailViewState, 'loading' | 'success'> };

@Component({
  selector: 'app-department-detail',
  imports: [
    DatePipe,
    AppShellComponent,
    DataTableComponent,
    DataTableCellDirective,
    EmptyStateComponent,
    FilterBarComponent,
    SkeletonComponent,
    StatusBadgeComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './department-detail.component.html',
  styleUrl: './department-detail.component.scss',
})
export class DepartmentDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly departmentService = inject(DepartmentService);
  private readonly toast = inject(ToastService);

  private readonly result = signal<LoadResult>({ state: 'loading' });
  private readonly reload = new Subject<void>();
  private departmentId = '';

  protected readonly viewState = computed(() => this.result().state);
  protected readonly detail = computed<DepartmentDetail | null>(() => {
    const result = this.result();
    return result.state === 'success' ? result.detail : null;
  });

  protected readonly menuOpen = signal(false);
  protected readonly hierarchyExpanded = signal(true);
  protected readonly search = signal('');
  protected readonly filterValues = signal<Readonly<Record<string, string>>>({ position: '' });

  protected readonly employeeColumns: readonly TableColumn[] = [
    { key: 'fullName', header: 'Cán bộ / Mã NV', sortable: true },
    { key: 'positionTitle', header: 'Vị trí chức danh', sortable: true },
    { key: 'status', header: 'Trạng thái', sortable: true },
    { key: 'managerName', header: 'Quản lý trực tiếp' },
  ];

  protected readonly employeeFilters = computed<readonly FilterDefinition[]>(() => [
    {
      key: 'position',
      label: 'Lọc theo chức danh',
      allLabel: 'Tất cả chức danh',
      options: (this.detail()?.positions ?? []).map((p) => ({ value: p.id, label: p.name })),
    },
  ]);

  protected readonly filteredEmployees = computed<readonly EmployeePreview[]>(() => {
    const employees = this.detail()?.employeePreview ?? [];
    const term = this.search().toLowerCase();
    const position = this.filterValues()['position'];
    return employees.filter(
      (e) =>
        (!position || e.positionId === position) &&
        (!term || e.fullName.toLowerCase().includes(term) || e.code.toLowerCase().includes(term)),
    );
  });

  protected readonly headcountRate = computed(() => {
    const d = this.detail();
    return d && d.headcountQuota > 0
      ? Math.round((d.employeeCount / d.headcountQuota) * 1000) / 10
      : 0;
  });

  protected readonly employeeTrack = (row: EmployeePreview): string => row.id;
  protected readonly employeeLabel = (row: EmployeePreview): string =>
    `Xem hồ sơ cán bộ ${row.fullName}`;

  constructor() {
    combineLatest([
      this.route.paramMap.pipe(map((params) => params.get('departmentId') ?? '')),
      this.reload.pipe(startWith(undefined)),
    ])
      .pipe(
        switchMap(([id]) => this.load(id)),
        takeUntilDestroyed(),
      )
      .subscribe((result) => this.result.set(result));
  }

  protected retry(): void {
    this.reload.next();
  }

  protected statusLabel(status: DepartmentStatus): string {
    return STATUS_LABELS[status];
  }

  protected statusTone(status: DepartmentStatus): StatusTone {
    return STATUS_TONES[status];
  }

  protected initials(fullName: string): string {
    return fullName
      .split(/\s+/)
      .filter(Boolean)
      .slice(-2)
      .map((part) => part[0])
      .join('')
      .toUpperCase();
  }

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected toggleHierarchy(): void {
    this.hierarchyExpanded.update((expanded) => !expanded);
  }

  protected onSearch(term: string): void {
    this.search.set(term);
  }

  protected onFilter(change: FilterChange): void {
    this.filterValues.update((values) => ({ ...values, [change.key]: change.value }));
  }

  protected resetFilters(): void {
    this.search.set('');
    this.filterValues.set({ position: '' });
  }

  protected go(destination: DepartmentDestination, context: NavigationContext = {}): void {
    this.menuOpen.set(false);
    const current = this.detail()?.id ?? this.departmentId;
    const resolved = { departmentId: current, ...context };
    const target = resolveDestination(destination, resolved);
    if (target) {
      void this.router.navigate([...target.commands], { queryParams: target.queryParams });
    } else {
      this.toast.info(`Màn hình "${DESTINATION_LABELS[destination]}" đang được phát triển.`);
    }
  }

  private load(id: string) {
    this.departmentId = id;
    this.search.set('');
    this.filterValues.set({ position: '' });
    // Never keep showing the previous department while the next one loads.
    return this.departmentService.getDetail(id).pipe(
      map((detail): LoadResult => ({ state: 'success', detail })),
      catchError((error: unknown) => of<LoadResult>({ state: stateFor(error) })),
      startWith<LoadResult>({ state: 'loading' }),
    );
  }
}

function stateFor(error: unknown): 'notFound' | 'forbidden' | 'error' {
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  if (status === 404) {
    return 'notFound';
  }
  return status === 403 ? 'forbidden' : 'error';
}
