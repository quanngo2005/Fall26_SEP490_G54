import { Component, computed, DestroyRef, HostListener, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { DepartmentService } from '../../core/department.service';
import { DepartmentSummary } from '../../core/department.models';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { RoleOption } from '../../core/role-permission.models';
import { RolePermissionService } from '../../core/role-permission.service';
import {
  UserAccountDetail,
  UserAccountFilter,
  UserAccountStatus,
  UserAccountSummary,
  UserAccountSummaryCounts,
} from '../../core/user-account.models';
import { UserAccountService } from '../../core/user-account.service';
import { AppDatePipe } from '../../shared/pipes';
import { HeaderComponent, LayoutIconComponent, SidebarComponent } from '../../shared/layout';
import {
  initials,
  SelectOption,
  StatusBadgeComponent,
  StatusBadgeTone,
  TablePaginationComponent,
  UiSelectComponent,
} from '../../shared/ui';
import { UserAccountFormModalComponent } from './user-account-form-modal.component';
import {
  AccountAction,
  UserAccountActionDialogComponent,
} from './user-account-action-dialog.component';

type DetailTab = 'info' | 'history';
type FormMode = 'create' | 'edit' | null;

/** Slightly longer than the 180ms exit animation so the node is removed only once it is fully transparent. */
const DRAWER_EXIT_MS = 200;

/** Pause after the last keystroke before the list is re-queried. */
const SEARCH_DEBOUNCE_MS = 300;

/** SCR-ADM-01: user account list; SCR-ADM-02: details drawer; modals M1 live in the form component. */
@Component({
  selector: 'app-user-account-list',
  imports: [
    FormsModule,
    RouterLink,
    AppDatePipe,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    StatusBadgeComponent,
    TablePaginationComponent,
    UiSelectComponent,
    UserAccountFormModalComponent,
    UserAccountActionDialogComponent,
  ],
  templateUrl: './user-account-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './user-account-list.component.scss',
  ],
})
export class UserAccountListComponent extends LocalizedComponent {
  private readonly api = inject(UserAccountService);
  private readonly roleApi = inject(RolePermissionService);
  private readonly departmentApi = inject(DepartmentService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly pageSizes = [20, 50, 100];
  protected readonly dateTime = 'DD/MM/YYYY HH:mm';

  protected readonly items = signal<UserAccountSummary[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly counts = signal<UserAccountSummaryCounts | null>(null);
  protected readonly roles = signal<RoleOption[]>([]);
  protected readonly departments = signal<DepartmentSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly filtersFailed = signal(false);

  protected readonly keyword = signal('');
  protected readonly roleId = signal('');
  protected readonly departmentId = signal('');
  protected readonly status = signal<UserAccountStatus | ''>('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);

  protected readonly selectedId = signal<string | null>(null);
  protected readonly closing = signal(false);
  private closeTimer: ReturnType<typeof setTimeout> | undefined;
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  protected readonly detail = signal<UserAccountDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailFailed = signal(false);
  protected readonly tab = signal<DetailTab>('info');
  protected readonly formMode = signal<FormMode>(null);
  protected readonly action = signal<AccountAction | null>(null);

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())),
  );

  protected readonly hasFilters = computed(
    () => !!(this.keyword().trim() || this.roleId() || this.departmentId() || this.status()),
  );

  protected readonly roleOptions = computed<SelectOption[]>(() => {
    const t = this.localization.userAccounts;
    return [
      { value: '', label: t.roleAll },
      ...this.roles().map((role) => ({ value: role.id, label: role.name })),
    ];
  });

  protected readonly departmentOptions = computed<SelectOption[]>(() => {
    const t = this.localization.userAccounts;
    return [
      { value: '', label: t.departmentAll },
      ...this.departments().map((dept) => ({
        value: dept.id,
        label: dept.name,
      })),
    ];
  });

  protected readonly statusOptions = computed<SelectOption<UserAccountStatus | ''>[]>(() => {
    const t = this.localization.userAccounts;
    return [
      { value: '', label: t.statusAll },
      { value: 'ACTIVE', label: t.statusActive },
      { value: 'LOCKED', label: t.statusLocked },
      { value: 'EMPLOYEE_TERMINATED', label: t.statusTerminated },
    ];
  });

  constructor() {
    super();
    this.roleApi
      .roles()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (roles) => this.roles.set(roles),
        error: () => this.filtersFailed.set(true),
      });
    this.departmentApi
      .list({ pageSize: 100 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => this.departments.set(result.items),
        error: () => this.filtersFailed.set(true),
      });
    this.loadCounts();
    this.load();
    this.destroyRef.onDestroy(() => {
      clearTimeout(this.searchTimer);
      clearTimeout(this.closeTimer);
    });
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (!this.formMode() && !this.action()) this.closeDrawer();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    const filter: UserAccountFilter = {
      keyword: this.keyword().trim(),
      roleId: this.roleId(),
      departmentId: this.departmentId(),
      status: this.status() || undefined,
      page: this.page(),
      pageSize: this.pageSize(),
    };
    this.api
      .list(filter)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.failed.set(true);
        },
      });
  }

  protected filterChanged(): void {
    this.page.set(1);
    this.load();
  }

  /** Search runs while typing, after a short pause; Enter runs it immediately. */
  protected onKeywordInput(value: string): void {
    this.keyword.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.filterChanged(), SEARCH_DEBOUNCE_MS);
  }

  protected flushSearch(): void {
    clearTimeout(this.searchTimer);
    this.filterChanged();
  }

  protected clearSearch(): void {
    clearTimeout(this.searchTimer);
    this.keyword.set('');
    this.filterChanged();
  }

  protected clearFilters(): void {
    clearTimeout(this.searchTimer);
    this.keyword.set('');
    this.roleId.set('');
    this.departmentId.set('');
    this.status.set('');
    this.filterChanged();
  }

  protected onRoleFilter(id: string): void {
    this.roleId.set(id);
    this.filterChanged();
  }

  protected onDepartmentFilter(id: string): void {
    this.departmentId.set(id);
    this.filterChanged();
  }

  protected onStatusFilter(value: UserAccountStatus | ''): void {
    this.status.set(value);
    this.filterChanged();
  }

  protected onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.filterChanged();
  }

  protected setStatusCard(status: UserAccountStatus): void {
    this.status.set(this.status() === status ? '' : status);
    this.filterChanged();
  }

  protected goTo(page: number): void {
    if (page < 1 || page > this.pageCount() || page === this.page()) return;
    this.page.set(page);
    this.load();
  }

  protected toggleRow(account: UserAccountSummary): void {
    if (account.id === this.selectedId()) {
      this.closeDrawer();
      return;
    }
    this.openDrawer(account);
  }

  protected openDrawer(account: UserAccountSummary): void {
    if (account.id === this.selectedId()) return;
    clearTimeout(this.closeTimer);
    this.closing.set(false);
    this.selectedId.set(account.id);
    this.tab.set('info');
    this.fetchDetail(account.id);
  }

  protected closeDrawer(): void {
    if (this.selectedId() === null) return;
    // Keep the drawer mounted (and `detail` intact) until its exit animation has finished, then
    // remove it ourselves. Angular's animate.leave strips the leave class before removing the node,
    // which replays the enter animation as a flash.
    this.selectedId.set(null);
    this.closing.set(true);
    clearTimeout(this.closeTimer);
    this.closeTimer = setTimeout(() => this.closing.set(false), DRAWER_EXIT_MS);
  }

  protected retryDetail(): void {
    const id = this.selectedId();
    if (id) this.fetchDetail(id);
  }

  protected openCreate(): void {
    this.formMode.set('create');
  }

  protected openEdit(account: UserAccountSummary): void {
    if (account.status === 'EMPLOYEE_TERMINATED') return;
    if (this.selectedId() === account.id && this.detail()?.id === account.id) {
      this.formMode.set('edit');
      return;
    }
    clearTimeout(this.closeTimer);
    this.closing.set(false);
    this.selectedId.set(account.id);
    this.tab.set('info');
    this.fetchDetail(account.id, () => this.formMode.set('edit'));
  }

  protected closeForm(): void {
    this.formMode.set(null);
  }

  protected onSaved(account: UserAccountDetail): void {
    if (this.formMode() === 'edit' || this.selectedId() === account.id) this.detail.set(account);
    this.loadCounts();
    this.load();
  }

  /** A lock/unlock returns the fresh account; a password reset does not, so the drawer is re-read quietly. */
  protected onActionDone(updated: UserAccountDetail | null): void {
    const id = this.detail()?.id;
    this.action.set(null);
    if (updated) {
      this.detail.set(updated);
    } else if (id) {
      this.api
        .detail(id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (fresh) => this.detail.update((cur) => (cur?.id === id ? fresh : cur)),
        });
    }
    this.loadCounts();
    this.load();
  }

  protected statusLabel(status: UserAccountStatus): string {
    const t = this.localization.userAccounts;
    return {
      ACTIVE: t.statusActive,
      LOCKED: t.statusLocked,
      EMPLOYEE_TERMINATED: t.statusTerminated,
    }[status];
  }

  protected statusTone(status: UserAccountStatus): StatusBadgeTone {
    return {
      ACTIVE: 'success' as const,
      LOCKED: 'warning' as const,
      EMPLOYEE_TERMINATED: 'danger' as const,
    }[status];
  }

  protected initials(name: string): string {
    return initials(name);
  }

  private loadCounts(): void {
    this.api
      .summary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (counts) => this.counts.set(counts), error: () => this.counts.set(null) });
  }

  private fetchDetail(id: string, onLoaded?: () => void): void {
    this.detail.set(null);
    this.detailLoading.set(true);
    this.detailFailed.set(false);
    this.api
      .detail(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          if (this.selectedId() !== id) return;
          this.detail.set(detail);
          this.detailLoading.set(false);
          onLoaded?.();
        },
        error: () => {
          if (this.selectedId() !== id) return;
          this.detailLoading.set(false);
          this.detailFailed.set(true);
        },
      });
  }
}
