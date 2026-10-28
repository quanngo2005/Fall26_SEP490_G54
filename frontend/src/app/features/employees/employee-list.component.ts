import { Component, computed, DestroyRef, HostListener, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { forkJoin, Subscription } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { EmployeeService } from '../../core/employee.service';
import {
  EmployeeDepartmentOption,
  EmployeeDetail,
  EmployeeJobPositionOption,
} from '../../core/employee.models';
import { PagedResult } from '../../core/paged-result.model';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { EmployeeDetailDrawerComponent } from './employee-detail-drawer.component';
import { EmployeeListItem } from '../../core/employee-management.models';

@Component({
  selector: 'app-employee-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    SidebarComponent,
    HeaderComponent,
    LayoutIconComponent,
    EmployeeDetailDrawerComponent,
  ],
  templateUrl: './employee-list.component.html',
  styleUrls: ['./employee-list.component.scss', './employee-list.layout.scss'],
})
export class EmployeeListComponent extends LocalizedComponent {
  private readonly api = inject(EmployeeService);
  private readonly destroyRef = inject(DestroyRef);
  private listRequest?: Subscription;
  private optionsRequest?: Subscription;
  private requestedPage?: number;
  protected readonly result = signal<PagedResult<EmployeeDetail> | null>(null);
  protected readonly keyword = signal('');
  protected readonly selectedDepartment = signal('');
  protected readonly selectedPosition = signal('');
  protected readonly selectedEmployeeId = signal<string | null>(null);
  protected readonly departments = signal<EmployeeDepartmentOption[]>([]);
  protected readonly positions = signal<EmployeeJobPositionOption[]>([]);
  protected readonly loading = signal(false);
  protected readonly failure = signal<'offline' | 'forbidden' | 'error' | null>(null);
  protected readonly optionsLoading = signal(false);
  protected readonly optionsFailed = signal(false);
  protected readonly page = computed(() => this.result()?.page ?? 1);
  protected readonly pageCount = computed(() => {
    const result = this.result();
    return result ? Math.ceil(result.totalCount / result.pageSize) : 0;
  });
  protected readonly paginationPages = computed(() => {
    const count = this.pageCount();
    if (!count) return [];
    // Keep the existing two button slots; both navigation directions stay reachable.
    return [...new Set([Math.max(1, this.page() - 1), Math.min(count, this.page() + 1)])];
  });
  protected readonly allEmployees = computed<EmployeeListItem[]>(() => {
    const text = this.localization;
    return (this.result()?.items ?? []).map((employee) => ({
      id: employee.id,
      code: employee.employeeCode,
      fullName: employee.fullName,
      departmentCode: employee.departmentCode,
      departmentName: employee.departmentName,
      positionCode: employee.jobPositionCode,
      positionTitle: employee.jobPositionTitle,
      status: employee.status,
      account: employee.username ?? text.employeeList.accountNone,
      managerName: employee.managerName ?? text.employee.notAvailable,
      joinDate: text.employee.notAvailable,
      email: employee.email,
      phone: employee.phone ?? text.employee.notAvailable,
      lastSyncAt: employee.lastSyncAt,
    }));
  });
  protected readonly filteredEmployees = computed(() =>
    this.loading() || this.failure() ? [] : this.allEmployees(),
  );
  protected readonly selectedEmployee = computed(() => {
    const id = this.selectedEmployeeId();
    return this.filteredEmployees().find((employee) => employee.id === id) ?? null;
  });
  protected readonly paginationInfoText = computed(() => {
    if (this.loading() || this.failure()) return this.localization.employee.notAvailable;
    const result = this.result();
    if (!result) return this.localization.employee.notAvailable;
    const from = result.items.length ? (result.page - 1) * result.pageSize + 1 : 0;
    return this.i18n.translate('employeeList.paginationInfo', {
      from,
      to: result.items.length ? from + result.items.length - 1 : 0,
      total: result.totalCount,
    });
  });
  protected readonly listStateText = computed(() => {
    const text = this.localization.employeeList;
    if (this.loading()) return text.loading;
    switch (this.failure()) {
      case 'offline':
        return text.offline;
      case 'forbidden':
        return text.forbidden;
      case 'error':
        return text.loadError;
      default:
        return text.emptyList;
    }
  });
  protected readonly listNoteText = computed(() => {
    const text = this.localization.employeeList;
    if (this.optionsFailed()) return text.optionsError;
    if (this.optionsLoading()) return text.optionsLoading;
    return text.listScopeNote;
  });

  constructor() {
    super();
    this.loadOptions();
    this.load();
  }

  protected loadOptions(): void {
    this.optionsRequest?.unsubscribe();
    this.optionsLoading.set(true);
    this.optionsFailed.set(false);
    this.optionsRequest = forkJoin({
      departments: this.api.departments(),
      positions: this.api.jobPositions(),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (options) => {
          this.departments.set(options.departments);
          this.positions.set(options.positions);
          this.optionsLoading.set(false);
        },
        error: () => {
          this.optionsFailed.set(true);
          this.optionsLoading.set(false);
        },
      });
  }

  protected load(page = this.requestedPage): void {
    this.requestedPage = page;
    this.listRequest?.unsubscribe();
    this.selectedEmployeeId.set(null);
    this.loading.set(true);
    this.failure.set(null);
    this.listRequest = this.api
      .list({
        keyword: this.keyword().trim(),
        departmentId: this.selectedDepartment(),
        jobPositionId: this.selectedPosition(),
        page,
        pageSize: this.result()?.pageSize,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.result.set(result);
          if (result.items.length === 0 && result.totalCount > 0 && result.page > 1) {
            this.load(Math.ceil(result.totalCount / result.pageSize));
            return;
          }
          this.requestedPage = result.page;
          this.loading.set(false);
        },
        error: (error: unknown) => {
          const status = error instanceof HttpErrorResponse ? error.status : null;
          this.failure.set(status === 0 ? 'offline' : status === 403 ? 'forbidden' : 'error');
          this.loading.set(false);
        },
      });
  }

  protected applyFilters(): void {
    if (this.optionsFailed()) this.loadOptions();
    this.load(1);
  }

  protected onSearchChange(value: string): void {
    this.keyword.set(value);
    this.applyFilters();
  }

  protected changeDepartment(id: string): void {
    this.selectedDepartment.set(id);
    this.applyFilters();
  }

  protected changePosition(id: string): void {
    this.selectedPosition.set(id);
    this.applyFilters();
  }

  protected selectEmployee(employee: EmployeeListItem): void {
    this.selectedEmployeeId.set(employee.id);
  }

  protected closeDrawer(): void {
    this.selectedEmployeeId.set(null);
  }

  protected navigateEmployee(direction: 'previous' | 'next'): void {
    const list = this.filteredEmployees();
    if (!list.length || !this.selectedEmployee()) return;
    const index = list.findIndex((employee) => employee.id === this.selectedEmployeeId());
    const next =
      direction === 'next' ? (index + 1) % list.length : (index - 1 + list.length) % list.length;
    this.selectedEmployeeId.set(list[next].id);
  }

  protected goToPage(page: number): void {
    if (this.loading() || page < 1 || page > this.pageCount()) return;
    if (this.optionsFailed()) this.loadOptions();
    this.load(page);
  }

  @HostListener('window:keydown', ['$event'])
  protected handleKeyboardNav(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.closeDrawer();
      return;
    }
    if (
      !this.selectedEmployee() ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      event.shiftKey
    )
      return;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      const target = event.target;
      if (
        target instanceof Element &&
        target.closest('input, select, textarea, [contenteditable="true"]')
      )
        return;
      event.preventDefault();
      this.navigateEmployee(event.key === 'ArrowDown' ? 'next' : 'previous');
    }
  }
}
