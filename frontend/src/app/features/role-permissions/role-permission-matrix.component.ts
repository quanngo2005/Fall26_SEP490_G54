import { Component, computed, DestroyRef, HostListener, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  PERMISSION_ACTIONS,
  PERMISSION_SCOPES,
  PermissionAction,
  PermissionCell,
  PermissionChange,
  PermissionModule,
  PermissionScope,
  RoleOption,
  RolePermissionMatrix,
} from '../../core/role-permission.models';
import { RolePermissionService } from '../../core/role-permission.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';

type CellKey = `${string}|${PermissionAction}`;

interface Notice {
  kind: 'success' | 'error';
  text: string;
}

const NOTICE_MS = 5000;

@Component({
  selector: 'app-role-permission-matrix',
  imports: [FormsModule, HeaderComponent, SidebarComponent],
  templateUrl: './role-permission-matrix.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './role-permission-matrix.component.scss',
  ],
})
export class RolePermissionMatrixComponent extends LocalizedComponent {
  private readonly api = inject(RolePermissionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private noticeTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly actions = PERMISSION_ACTIONS;
  protected readonly scopes = PERMISSION_SCOPES;

  protected readonly roles = signal<RoleOption[]>([]);
  protected readonly roleId = signal('');
  protected readonly matrix = signal<RolePermissionMatrix | null>(null);
  /** Pending edits keyed by resource|action; value is the new scope. */
  protected readonly edits = signal<ReadonlyMap<CellKey, PermissionScope | null>>(new Map());
  protected readonly collapsed = signal<ReadonlySet<string>>(new Set());
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);
  protected readonly notice = signal<Notice | null>(null);

  protected readonly keyword = signal('');
  protected readonly moduleFilter = signal('');
  protected readonly grantedOnly = signal(false);

  protected readonly selectedRole = computed(
    () => this.roles().find((role) => role.id === this.roleId()) ?? null,
  );
  protected readonly modules = computed(() => this.matrix()?.modules ?? []);
  protected readonly dirtyCount = computed(() => this.edits().size);

  protected readonly visibleModules = computed<PermissionModule[]>(() => {
    const keyword = this.keyword().trim().toLowerCase();
    const moduleId = this.moduleFilter();
    const grantedOnly = this.grantedOnly();
    return this.modules()
      .filter((module) => !moduleId || module.module === moduleId)
      .map((module) => ({
        ...module,
        resources: module.resources.filter(
          (row) =>
            (!keyword ||
              row.name.toLowerCase().includes(keyword) ||
              row.resource.toLowerCase().includes(keyword)) &&
            (!grantedOnly || row.cells.some((cell) => this.scopeOf(row.resource, cell) !== null)),
        ),
      }))
      .filter((module) => module.resources.length > 0);
  });

  protected readonly collapsedModules = computed(() =>
    this.visibleModules().filter((module) => this.collapsed().has(module.module)),
  );
  protected readonly expandedModules = computed(() =>
    this.visibleModules().filter((module) => !this.collapsed().has(module.module)),
  );

  constructor() {
    super();
    this.api
      .roles()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (roles) => {
          this.roles.set(roles);
          const requested = this.route.snapshot.queryParamMap.get('roleId');
          const initial = roles.find((role) => role.id === requested) ?? roles[0];
          if (initial) this.loadMatrix(initial.id);
          else this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.failed.set(true);
        },
      });
    this.destroyRef.onDestroy(() => this.clearNoticeTimer());
  }

  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
      event.preventDefault();
      this.save();
    }
  }

  @HostListener('window:beforeunload', ['$event'])
  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirtyCount()) event.preventDefault();
  }

  protected changeRole(roleId: string): void {
    if (roleId === this.roleId()) return;
    if (this.dirtyCount() && !confirm(this.localization.permissionMatrix.confirmDiscard)) {
      // Re-emit the current value so the select snaps back.
      const current = this.roleId();
      this.roleId.set('');
      queueMicrotask(() => this.roleId.set(current));
      return;
    }
    this.loadMatrix(roleId);
  }

  protected retry(): void {
    if (this.roleId()) this.loadMatrix(this.roleId());
    else location.reload();
  }

  protected cell(
    row: { cells: PermissionCell[] },
    action: PermissionAction,
  ): PermissionCell | null {
    return row.cells.find((cell) => cell.action === action) ?? null;
  }

  protected scopeOf(resource: string, cell: PermissionCell): PermissionScope | null {
    const key = this.key(resource, cell.action);
    const edits = this.edits();
    return edits.has(key) ? (edits.get(key) ?? null) : cell.scope;
  }

  protected isDirty(resource: string, action: PermissionAction): boolean {
    return this.edits().has(this.key(resource, action));
  }

  protected rowDirty(resource: string): boolean {
    return this.actions.some((action) => this.isDirty(resource, action));
  }

  protected optionsFor(cell: PermissionCell): readonly PermissionScope[] {
    return cell.allowedScopes?.length ? cell.allowedScopes : this.scopes;
  }

  protected setScope(resource: string, cell: PermissionCell, value: string): void {
    if (cell.locked || !cell.applicable) return;
    const scope = (value || null) as PermissionScope | null;
    const key = this.key(resource, cell.action);
    const next = new Map(this.edits());
    if (scope === cell.scope) next.delete(key);
    else next.set(key, scope);
    this.edits.set(next);
  }

  protected scopeLabel(scope: PermissionScope | null): string {
    return this.localization.permissionMatrix.scopes[scope ?? 'none'];
  }

  protected toggleModule(moduleId: string): void {
    const next = new Set(this.collapsed());
    if (next.has(moduleId)) next.delete(moduleId);
    else next.add(moduleId);
    this.collapsed.set(next);
  }

  protected expandAll(): void {
    this.collapsed.set(new Set());
  }

  protected collapsedTitle(): string {
    return this.collapsedModules()
      .map((module) => module.name)
      .join(' · ');
  }

  protected cancel(): void {
    this.edits.set(new Map());
  }

  protected save(): void {
    const matrix = this.matrix();
    if (!matrix || !this.dirtyCount() || this.saving()) return;
    const changes: PermissionChange[] = [...this.edits()].map(([key, scope]) => {
      const separator = key.lastIndexOf('|');
      return {
        resource: key.slice(0, separator),
        action: key.slice(separator + 1) as PermissionAction,
        scope,
      };
    });
    this.saving.set(true);
    this.api
      .save(matrix.roleId, { version: matrix.version, changes })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.matrix.set(saved);
          this.edits.set(new Map());
          this.showNotice('success', this.localization.permissionMatrix.saved);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          const text =
            error instanceof HttpErrorResponse && error.status === 409
              ? this.localization.permissionMatrix.conflict
              : this.localization.permissionMatrix.saveError;
          this.showNotice('error', text);
        },
      });
  }

  protected dismissNotice(): void {
    this.clearNoticeTimer();
    this.notice.set(null);
  }

  private loadMatrix(roleId: string): void {
    this.roleId.set(roleId);
    this.loading.set(true);
    this.failed.set(false);
    this.api
      .matrix(roleId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (matrix) => {
          if (this.roleId() !== roleId) return;
          this.matrix.set(matrix);
          this.edits.set(new Map());
          this.loading.set(false);
        },
        error: () => {
          if (this.roleId() !== roleId) return;
          this.matrix.set(null);
          this.loading.set(false);
          this.failed.set(true);
        },
      });
  }

  private key(resource: string, action: PermissionAction): CellKey {
    return `${resource}|${action}`;
  }

  private showNotice(kind: Notice['kind'], text: string): void {
    this.clearNoticeTimer();
    this.notice.set({ kind, text });
    this.noticeTimer = setTimeout(() => this.notice.set(null), NOTICE_MS);
  }

  private clearNoticeTimer(): void {
    if (this.noticeTimer) clearTimeout(this.noticeTimer);
    this.noticeTimer = null;
  }
}
