import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  catchError,
  combineLatest,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  startWith,
  Subject,
  switchMap,
} from 'rxjs';
import { EmployeeService } from '../../core/employee.service';
import { AppShellComponent } from '../../shared/layout/app-shell.component';
import {
  DataTableCellDirective,
  DataTableComponent,
  EmptyStateComponent,
  FormErrorComponent,
  ModalComponent,
  SkeletonComponent,
  StatusBadgeComponent,
  StatusTone,
  TableColumn,
  ToastService,
} from '../../shared/ui';
import { EmployeeListResponse, EmployeeStatus } from './employee-list.model';

type ViewState = 'loading' | 'success' | 'error';

@Component({
  selector: 'app-employee-list',
  imports: [
    ReactiveFormsModule,
    AppShellComponent,
    DataTableComponent,
    DataTableCellDirective,
    EmptyStateComponent,
    FormErrorComponent,
    ModalComponent,
    SkeletonComponent,
    StatusBadgeComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './employee-list.component.html',
  styleUrl: './employee-list.component.scss',
})
export class EmployeeListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly employeeService = inject(EmployeeService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  protected readonly viewState = signal<ViewState>('loading');
  protected readonly data = signal<EmployeeListResponse | null>(null);
  protected readonly errorMessage = signal('');

  // Filters & query state
  protected readonly search = signal('');
  protected readonly selectedDepartment = signal('');
  protected readonly selectedPosition = signal('');
  protected readonly selectedStatus = signal('');
  protected readonly selectedManager = signal('');
  protected readonly currentPage = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly sortBy = signal('fullName');
  protected readonly sortDirection = signal<'asc' | 'desc'>('asc');

  // Interactive controls state
  protected readonly isAddModalOpen = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly isMenuOpen = signal(false);

  private readonly reload$ = new Subject<void>();
  private readonly searchInput$ = new Subject<string>();

  // Add Employee Form
  protected readonly addForm = this.fb.group({
    fullName: ['', [Validators.required, Validators.minLength(3)]],
    code: ['', [Validators.required, Validators.pattern(/^NV-\d{5}$/)]],
    email: ['', [Validators.required, Validators.email]],
    departmentId: ['', Validators.required],
    positionId: ['', Validators.required],
  });

  protected readonly tableColumns: readonly TableColumn[] = [
    { key: 'fullName', header: 'Cán bộ / Mã NV', sortable: true },
    { key: 'departmentName', header: 'Phòng ban', sortable: true },
    { key: 'positionTitle', header: 'Vị trí chức danh', sortable: true },
    { key: 'managerName', header: 'Quản lý trực tiếp' },
    { key: 'activeWorkCount', header: 'Việc active', align: 'end', sortable: true },
    { key: 'status', header: 'Trạng thái', sortable: true },
    { key: 'actions', header: 'Chi tiết', align: 'end' },
  ];

  protected readonly activeFilterCount = computed(() => {
    let count = 0;
    if (this.search()) count++;
    if (this.selectedDepartment()) count++;
    if (this.selectedPosition() && this.selectedPosition() !== 'all') count++;
    if (this.selectedStatus() && this.selectedStatus() !== 'all') count++;
    if (this.selectedManager()) count++;
    return count;
  });

  constructor() {
    // Sync initial query params from URL
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const dept = params.get('departmentId');
      if (dept) this.selectedDepartment.set(dept);
      const pos = params.get('positionId');
      if (pos) this.selectedPosition.set(pos);
      const status = params.get('status');
      if (status) this.selectedStatus.set(status);
      const s = params.get('search');
      if (s) this.search.set(s);
      const p = params.get('page');
      if (p) this.currentPage.set(Number(p) || 1);
    });

    // Debounce search input
    this.searchInput$
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((term) => {
        this.search.set(term);
        this.currentPage.set(1);
        this.triggerLoad();
      });

    // Main data loading stream
    combineLatest([this.route.queryParamMap, this.reload$.pipe(startWith(undefined))])
      .pipe(
        switchMap(() => this.fetchData()),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  protected onSearchInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.searchInput$.next(val);
  }

  protected onDepartmentChange(event: Event): void {
    const val = (event.target as HTMLSelectElement).value;
    this.selectedDepartment.set(val);
    this.currentPage.set(1);
    this.updateQueryParams();
  }

  protected onPositionChange(event: Event): void {
    const val = (event.target as HTMLSelectElement).value;
    this.selectedPosition.set(val);
    this.currentPage.set(1);
    this.updateQueryParams();
  }

  protected selectPositionChip(posId: string): void {
    const target = posId === 'all' ? '' : posId;
    this.selectedPosition.set(target);
    this.currentPage.set(1);
    this.updateQueryParams();
  }

  protected onStatusChange(event: Event): void {
    const val = (event.target as HTMLSelectElement).value;
    this.selectedStatus.set(val);
    this.currentPage.set(1);
    this.updateQueryParams();
  }

  protected selectStatusCard(status: EmployeeStatus): void {
    if (this.selectedStatus() === status) {
      this.selectedStatus.set(''); // toggle off
    } else {
      this.selectedStatus.set(status);
    }
    this.currentPage.set(1);
    this.updateQueryParams();
  }

  protected onManagerChange(event: Event): void {
    const val = (event.target as HTMLSelectElement).value;
    this.selectedManager.set(val);
    this.currentPage.set(1);
    this.updateQueryParams();
  }

  protected resetFilters(): void {
    this.search.set('');
    this.selectedDepartment.set('');
    this.selectedPosition.set('');
    this.selectedStatus.set('');
    this.selectedManager.set('');
    this.currentPage.set(1);
    this.updateQueryParams();
  }

  protected onPageChange(page: number): void {
    this.currentPage.set(page);
    this.updateQueryParams();
  }

  protected onSort(columnKey: string): void {
    if (this.sortBy() === columnKey) {
      this.sortDirection.update((dir) => (dir === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortBy.set(columnKey);
      this.sortDirection.set('asc');
    }
    this.triggerLoad();
  }

  protected navigateToEmployee(employeeId: string): void {
    void this.router.navigate(['/employee-profile'], {
      queryParams: {
        employeeId,
        departmentId: this.selectedDepartment() || undefined,
      },
    });
  }

  protected navigateToDepartment(departmentId: string): void {
    void this.router.navigate(['/departments', departmentId]);
  }

  protected exportReport(): void {
    this.toast.info('Đang kết xuất báo cáo danh bạ cán bộ nhân viên...');
  }

  protected toggleAdminMenu(): void {
    this.isMenuOpen.update((v) => !v);
  }

  protected syncHrms(): void {
    this.isMenuOpen.set(false);
    this.toast.success('Đã gửi yêu cầu đồng bộ HRMS. Dữ liệu đang được cập nhật...');
  }

  protected openAddModal(): void {
    this.addForm.reset({
      departmentId: this.selectedDepartment() || '',
      positionId: '',
    });
    this.isAddModalOpen.set(true);
  }

  protected closeAddModal(): void {
    if (!this.isSaving()) {
      this.isAddModalOpen.set(false);
    }
  }

  protected submitAddForm(): void {
    if (this.addForm.invalid) {
      this.addForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    // Simulate save request
    setTimeout(() => {
      this.isSaving.set(false);
      this.isAddModalOpen.set(false);
      this.toast.success(`Đã thêm cán bộ ${this.addForm.value.fullName} thành công!`);
      this.triggerLoad();
    }, 600);
  }

  protected statusTone(status: EmployeeStatus): StatusTone {
    switch (status) {
      case 'active':
        return 'success';
      case 'on_leave':
        return 'neutral';
      case 'probation':
        return 'warning';
      case 'inactive':
        return 'danger';
      default:
        return 'neutral';
    }
  }

  protected initials(name: string): string {
    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(-2)
      .map((part) => part[0])
      .join('')
      .toUpperCase();
  }

  protected retry(): void {
    this.triggerLoad();
  }

  private triggerLoad(): void {
    this.reload$.next();
  }

  private updateQueryParams(): void {
    const queryParams: Record<string, string | null> = {
      search: this.search() || null,
      departmentId: this.selectedDepartment() || null,
      positionId: this.selectedPosition() || null,
      status: this.selectedStatus() || null,
      managerId: this.selectedManager() || null,
      page: this.currentPage() > 1 ? String(this.currentPage()) : null,
    };

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
    });
  }

  private fetchData() {
    this.viewState.set('loading');
    this.errorMessage.set('');

    return this.employeeService
      .getEmployees({
        search: this.search(),
        departmentId: this.selectedDepartment(),
        positionId: this.selectedPosition(),
        status: this.selectedStatus(),
        managerId: this.selectedManager(),
        page: this.currentPage(),
        pageSize: this.pageSize(),
        sortBy: this.sortBy(),
        sortDirection: this.sortDirection(),
      })
      .pipe(
        map((response) => {
          this.data.set(response);
          this.viewState.set('success');
        }),
        catchError((error: unknown) => {
          const msg =
            error instanceof HttpErrorResponse
              ? `Lỗi tải danh sách: ${error.status} ${error.statusText}`
              : 'Không thể kết nối đến máy chủ.';
          this.errorMessage.set(msg);
          this.viewState.set('error');
          return of(null);
        }),
      );
  }
}
