import {
  Component,
  computed,
  DestroyRef,
  HostListener,
  inject,
  signal,
  WritableSignal,
} from '@angular/core';
import { Observable } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  PermissionAction,
  PermissionScope,
  RoleAccount,
  RolePermissionMatrix,
  RoleSummary,
} from '../../core/role-permission.models';
import { RolePermissionService } from '../../core/role-permission.service';
import { HeaderComponent, LayoutIconComponent, SidebarComponent } from '../../shared/layout';
import {
  SelectOption,
  StatusBadgeComponent,
  StatusBadgeTone,
  TablePaginationComponent,
  UiSelectComponent,
} from '../../shared/ui';

type RoleType = '' | 'system' | 'custom';
type DetailTab = 'info' | 'permissions' | 'accounts' | 'history';

/** One read-only row in the Permissions tab: actions sharing a scope on a resource. */
interface GrantedRow {
  key: string;
  name: string;
  actions: PermissionAction[];
  scope: PermissionScope;
  locked: boolean;
  lockReason: string | null;
}

interface GrantedModule {
  module: string;
  name: string;
  rows: GrantedRow[];
}

interface Remote<T> {
  loading: boolean;
  failed: boolean;
  data: T | null;
}

const idle = <T>(): Remote<T> => ({ loading: false, failed: false, data: null });

/** Slightly longer than the 180ms exit animation so the drawer is removed only once it is transparent. */
const DRAWER_EXIT_MS = 200;

/** SCR-ADM-03: role list with read-only detail drawer (SCR-ADM-03-D1). */
@Component({
  selector: 'app-role-list',
  imports: [
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    StatusBadgeComponent,
    TablePaginationComponent,
    UiSelectComponent,
  ],
  templateUrl: './role-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './role-list.component.scss',
  ],
})
export class RoleListComponent extends LocalizedComponent {
  private readonly api = inject(RolePermissionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private closeTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly roles = signal<RoleSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly keyword = signal('');
  protected readonly typeFilter = signal<RoleType>('');

  protected readonly selectedId = signal<string | null>(null);
  /** Last opened role. Kept after close so the drawer can play its exit animation with content. */
  protected readonly drawerRole = signal<RoleSummary | null>(null);
  protected readonly closing = signal(false);
  protected readonly tab = signal<DetailTab>('permissions');
  protected readonly matrix = signal<Remote<RolePermissionMatrix>>(idle());
  protected readonly accounts = signal<Remote<RoleAccount[]>>(idle());

  protected readonly typeOptions = computed<SelectOption<RoleType>[]>(() => {
    const t = this.localization.roleList;
    return [
      { value: '', label: t.typeAll },
      { value: 'system', label: t.system },
      { value: 'custom', label: t.custom },
    ];
  });

  protected readonly visibleRoles = computed(() => {
    const keyword = this.keyword().trim().toLowerCase();
    const type = this.typeFilter();
    return this.roles().filter(
      (role) =>
        (!type || role.isSystem === (type === 'system')) &&
        (!keyword ||
          role.code.toLowerCase().includes(keyword) ||
          role.name.toLowerCase().includes(keyword)),
    );
  });

  protected readonly summary = computed(() => {
    const roles = this.roles();
    const system = roles.filter((role) => role.isSystem).length;
    return this.localization.roleList.summary
      .replace('{total}', String(roles.length))
      .replace('{system}', String(system))
      .replace('{custom}', String(roles.length - system));
  });

  protected readonly granted = computed<GrantedModule[]>(() => {
    const modules = this.matrix().data?.modules ?? [];
    return modules
      .map((module) => ({
        module: module.module,
        name: module.name,
        rows: module.resources.flatMap((resource) => {
          const byScope = new Map<PermissionScope, GrantedRow>();
          for (const cell of resource.cells) {
            if (!cell.applicable || !cell.scope) continue;
            const row = byScope.get(cell.scope) ?? {
              key: `${resource.resource}|${cell.scope}`,
              name: resource.name,
              actions: [],
              scope: cell.scope,
              locked: false,
              lockReason: null,
            };
            row.actions.push(cell.action);
            if (cell.locked) {
              row.locked = true;
              row.lockReason ??= cell.lockReason ?? null;
            }
            byScope.set(cell.scope, row);
          }
          return [...byScope.values()];
        }),
      }))
      .filter((module) => module.rows.length > 0);
  });

  constructor() {
    super();
    this.destroyRef.onDestroy(() => clearTimeout(this.closeTimer));
    this.load();
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.close();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (roles) => {
          this.roles.set(roles);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.failed.set(true);
        },
      });
  }

  protected onTypeFilter(value: RoleType): void {
    this.typeFilter.set(value);
  }

  protected toggleRow(role: RoleSummary): void {
    if (role.id === this.selectedId()) {
      this.close();
      return;
    }
    this.open(role);
  }

  protected open(role: RoleSummary): void {
    if (role.id === this.selectedId()) return;
    clearTimeout(this.closeTimer);
    this.closing.set(false);
    this.drawerRole.set(role);
    this.selectedId.set(role.id);
    this.matrix.set(idle());
    this.accounts.set(idle());
    this.selectTab(this.tab());
  }

  protected close(): void {
    if (this.selectedId() === null) return;
    // Keep the drawer mounted until its exit animation has finished, then remove it.
    this.selectedId.set(null);
    this.closing.set(true);
    clearTimeout(this.closeTimer);
    this.closeTimer = setTimeout(() => this.closing.set(false), DRAWER_EXIT_MS);
  }

  protected selectTab(tab: DetailTab): void {
    this.tab.set(tab);
    const roleId = this.selectedId();
    if (!roleId) return;
    if (tab === 'permissions' && !this.matrix().data && !this.matrix().loading) {
      this.fetch(this.matrix, () => this.api.matrix(roleId), roleId);
    }
    if (tab === 'accounts' && !this.accounts().data && !this.accounts().loading) {
      this.fetch(this.accounts, () => this.api.accounts(roleId), roleId);
    }
  }

  protected retryTab(): void {
    if (this.tab() === 'permissions') this.matrix.set(idle());
    if (this.tab() === 'accounts') this.accounts.set(idle());
    this.selectTab(this.tab());
  }

  protected setupPermissions(): void {
    const roleId = this.selectedId();
    if (roleId) this.router.navigate(['/role-permissions'], { queryParams: { roleId } });
  }

  protected statusTone(role: RoleSummary): StatusBadgeTone {
    return role.status === 'Active' ? 'success' : 'neutral';
  }

  protected actionLabels(actions: PermissionAction[]): string {
    const labels = this.localization.permissionMatrix.actions;
    return actions.map((action) => labels[action]).join(', ');
  }

  protected scopeLabel(scope: PermissionScope): string {
    return this.localization.permissionMatrix.scopes[scope];
  }

  private fetch<T>(
    target: WritableSignal<Remote<T>>,
    request: () => Observable<T>,
    roleId: string,
  ): void {
    target.set({ loading: true, failed: false, data: null });
    request()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          if (this.selectedId() === roleId) target.set({ loading: false, failed: false, data });
        },
        error: () => {
          if (this.selectedId() === roleId)
            target.set({ loading: false, failed: true, data: null });
        },
      });
  }
}
