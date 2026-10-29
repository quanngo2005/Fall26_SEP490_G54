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
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { initials } from '../../shared/ui/display';
import { UserAccountFormModalComponent } from './user-account-form-modal.component';

type DetailTab = 'info' | 'history';
type FormMode = 'create' | 'edit' | null;

/** SCR-ADM-01: user account list; SCR-ADM-02: details drawer; modals M1 live in the form component. */
@Component({
  selector: 'app-user-account-list',
  imports: [
    FormsModule,
    RouterLink,
    AppDatePipe,
    HeaderComponent,
    SidebarComponent,
    UserAccountFormModalComponent,
  ],
  templateUrl: './user-account-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './user-account-list.component.scss',
    './user-account-drawer.scss',
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
  protected readonly detail = signal<UserAccountDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailFailed = signal(false);
  protected readonly tab = signal<DetailTab>('info');
  protected readonly formMode = signal<FormMode>(null);

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())),
  );
  protected readonly pages = computed(() => {
    const count = this.pageCount();
    const start = Math.max(1, Math.min(this.page() - 1, count - 2));
    return Array.from({ length: Math.min(3, count) }, (_, index) => start + index);
  });
  protected readonly range = computed(() => {
    const total = this.totalCount();
    const from = total === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1;
    const to = Math.min(total, this.page() * this.pageSize());
    return this.localization.userAccounts.range
      .replace('{from}', String(from))
      .replace('{to}', String(to))
      .replace('{total}', String(total));
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
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (!this.formMode()) this.closeDrawer();
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

  protected setStatusCard(status: UserAccountStatus): void {
    this.status.set(this.status() === status ? '' : status);
    this.filterChanged();
  }

  protected goTo(page: number): void {
    if (page < 1 || page > this.pageCount() || page === this.page()) return;
    this.page.set(page);
    this.load();
  }

  protected openDrawer(account: UserAccountSummary): void {
    if (account.id === this.selectedId()) return;
    this.selectedId.set(account.id);
    this.tab.set('info');
    this.fetchDetail(account.id);
  }

  protected closeDrawer(): void {
    this.selectedId.set(null);
    this.detail.set(null);
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
    if (this.detail()?.id === account.id) {
      this.formMode.set('edit');
      return;
    }
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

  protected statusLabel(status: UserAccountStatus): string {
    const t = this.localization.userAccounts;
    return {
      ACTIVE: t.statusActive,
      LOCKED: t.statusLocked,
      EMPLOYEE_TERMINATED: t.statusTerminated,
    }[status];
  }

  protected statusClass(status: UserAccountStatus): string {
    return { ACTIVE: 'is-active', LOCKED: 'is-locked', EMPLOYEE_TERMINATED: 'is-terminated' }[
      status
    ];
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
