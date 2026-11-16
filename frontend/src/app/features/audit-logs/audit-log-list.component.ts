import { Component, computed, DestroyRef, HostListener, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import {
  AUDIT_ACTIONS,
  AuditAction,
  AuditLogDetail,
  AuditLogListItem,
  AuditLogQuery,
} from '../../core/audit-log.models';
import { AuditLogService } from '../../core/audit-log.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, LayoutIconComponent, SidebarComponent } from '../../shared/layout';
import {
  SelectOption,
  StatusBadgeComponent,
  StatusBadgeTone,
  TablePaginationComponent,
  UiSelectComponent,
} from '../../shared/ui';

const PAGE_SIZE = 20;
const DATE_TIME = 'DD/MM/YYYY HH:mm:ss';

/** Slightly longer than the 180ms exit animation so the drawer is removed only once it is transparent. */
const DRAWER_EXIT_MS = 200;

type Sort = 'occurredAt desc' | 'occurredAt asc';

interface ChangeRow {
  column: string;
  oldValue: string;
  newValue: string;
}

/** SCR-ADM-05 (list) and SCR-ADM-05-D1 (details drawer). Contract: docs/api/AUDIT_LOG_CONTRACT.md. */
@Component({
  selector: 'app-audit-log-list',
  imports: [
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    StatusBadgeComponent,
    TablePaginationComponent,
    UiSelectComponent,
  ],
  templateUrl: './audit-log-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './audit-log-list.component.scss',
  ],
})
export class AuditLogListComponent extends LocalizedComponent {
  private readonly api = inject(AuditLogService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private listRequest?: Subscription;
  private detailRequest?: Subscription;
  private closeTimer: ReturnType<typeof setTimeout> | undefined;

  readonly actions = AUDIT_ACTIONS;
  readonly pageSize = PAGE_SIZE;

  readonly keyword = signal('');
  readonly entityName = signal('');
  /** Not exposed in the filter bar; carried only from an incoming link, e.g. the system parameters audit link. */
  readonly entityId = signal('');
  readonly actorId = signal('');
  readonly action = signal<AuditAction | ''>('');
  readonly fromDate = signal('');
  readonly toDate = signal('');
  readonly sort = signal<Sort>('occurredAt desc');
  readonly page = signal(1);

  readonly items = signal<AuditLogListItem[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly failed = signal(false);

  readonly selectedId = signal<string | null>(null);
  readonly closing = signal(false);
  readonly detail = signal<AuditLogDetail | null>(null);
  readonly detailLoading = signal(false);
  readonly detailError = signal<'loadFailed' | 'notFound' | null>(null);

  readonly actionOptions = computed<SelectOption<AuditAction | ''>[]>(() => {
    const t = this.localization.auditLog;
    return [
      { value: '', label: t.actionAll },
      ...AUDIT_ACTIONS.map((action) => ({ value: action, label: t.actions[action] })),
    ];
  });

  readonly sortOptions = computed<SelectOption<Sort>[]>(() => {
    const t = this.localization.auditLog;
    return [
      { value: 'occurredAt desc', label: t.sortNewest },
      { value: 'occurredAt asc', label: t.sortOldest },
    ];
  });

  readonly rangeInvalid = computed(() => {
    const from = this.fromDate();
    const to = this.toDate();
    return !!from && !!to && from > to;
  });
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));
  readonly hasFilters = computed(
    () =>
      !!this.keyword() ||
      !!this.entityName() ||
      !!this.entityId() ||
      !!this.actorId() ||
      !!this.action() ||
      !!this.fromDate() ||
      !!this.toDate(),
  );
  readonly changeRows = computed<ChangeRow[]>(() => {
    const detail = this.detail();
    if (!detail) return [];
    const oldValues = detail.oldValues ?? {};
    const newValues = detail.newValues ?? {};
    const columns =
      detail.changedColumns && detail.changedColumns.length > 0
        ? detail.changedColumns
        : [...new Set([...Object.keys(oldValues), ...Object.keys(newValues)])];
    return columns.map((column) => ({
      column,
      oldValue: this.valueText(oldValues[column]),
      newValue: this.valueText(newValues[column]),
    }));
  });

  constructor() {
    super();
    this.destroyRef.onDestroy(() => {
      this.listRequest?.unsubscribe();
      this.detailRequest?.unsubscribe();
      clearTimeout(this.closeTimer);
    });
    this.applyInitialQueryParams();
    this.load();
  }

  /** Seeds filters from the URL (e.g. a link from another screen's audit-log action). */
  private applyInitialQueryParams(): void {
    const params = this.route.snapshot.queryParamMap;
    const entityName = params.get('entityName');
    if (entityName) this.entityName.set(entityName);
    const entityId = params.get('entityId');
    if (entityId) this.entityId.set(entityId);
    const actorId = params.get('actorId');
    if (actorId) this.actorId.set(actorId);
    const action = params.get('action');
    if (action && (AUDIT_ACTIONS as readonly string[]).includes(action)) {
      this.action.set(action as AuditAction);
    }
    const from = params.get('from');
    if (from && /^\d{4}-\d{2}-\d{2}$/.test(from)) this.fromDate.set(from);
    const to = params.get('to');
    if (to && /^\d{4}-\d{2}-\d{2}$/.test(to)) this.toDate.set(to);
  }

  applyFilters(): void {
    if (this.rangeInvalid()) return;
    this.page.set(1);
    this.load();
  }

  clearFilters(): void {
    this.keyword.set('');
    this.entityName.set('');
    this.entityId.set('');
    this.actorId.set('');
    this.action.set('');
    this.fromDate.set('');
    this.toDate.set('');
    this.applyFilters();
  }

  setSort(value: Sort): void {
    this.sort.set(value);
    this.applyFilters();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.pageCount() || page === this.page()) return;
    this.page.set(page);
    this.load();
  }

  load(): void {
    this.listRequest?.unsubscribe();
    this.loading.set(true);
    this.failed.set(false);
    this.listRequest = this.api
      .list(this.buildQuery())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: () => {
          this.items.set([]);
          this.totalCount.set(0);
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  toggleRow(item: AuditLogListItem): void {
    if (item.id === this.selectedId()) {
      this.close();
      return;
    }
    this.open(item);
  }

  open(item: AuditLogListItem): void {
    clearTimeout(this.closeTimer);
    this.closing.set(false);
    this.selectedId.set(item.id);
    this.detail.set(null);
    this.detailError.set(null);
    this.detailLoading.set(true);
    this.detailRequest?.unsubscribe();
    this.detailRequest = this.api
      .get(item.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.detail.set(detail);
          this.detailLoading.set(false);
        },
        error: (error: { status?: number }) => {
          this.detailError.set(error?.status === 404 ? 'notFound' : 'loadFailed');
          this.detailLoading.set(false);
        },
      });
  }

  @HostListener('document:keydown.escape')
  close(): void {
    if (!this.selectedId()) return;
    this.detailRequest?.unsubscribe();
    // Keep the drawer mounted (with its content) until the exit animation has finished.
    this.selectedId.set(null);
    this.closing.set(true);
    clearTimeout(this.closeTimer);
    this.closeTimer = setTimeout(() => {
      this.closing.set(false);
      this.detail.set(null);
      this.detailError.set(null);
      this.detailLoading.set(false);
    }, DRAWER_EXIT_MS);
  }

  actionLabel(action: string): string {
    return this.localization.auditLog.actions[action as AuditAction] ?? action;
  }

  actionTone(action: string): StatusBadgeTone {
    switch (action) {
      case 'Create':
        return 'success';
      case 'Update':
        return 'info';
      case 'Delete':
      case 'SoftDelete':
        return 'danger';
      default:
        return 'neutral';
    }
  }

  actorLabel(name: string | null): string {
    return name ?? this.localization.auditLog.system;
  }

  time(value: string | null | undefined): string {
    return this.i18n.formatDate(value, DATE_TIME);
  }

  columnsText(columns: string[] | null): string {
    return columns && columns.length > 0 ? columns.join(', ') : this.localization.auditLog.noValue;
  }

  private buildQuery(): AuditLogQuery {
    return {
      page: this.page(),
      pageSize: PAGE_SIZE,
      keyword: this.keyword().trim(),
      entityName: this.entityName().trim(),
      entityId: this.entityId().trim() || undefined,
      actorId: this.actorId().trim() || undefined,
      action: this.action(),
      from: this.dayBoundary(this.fromDate(), false),
      to: this.dayBoundary(this.toDate(), true),
      sort: this.sort(),
    };
  }

  /** Local calendar day boundary as an ISO 8601 instant; empty when no date is picked. */
  private dayBoundary(value: string, endOfDay: boolean): string | undefined {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
    if (!match) return undefined;
    const [, year, month, day] = match;
    const date = endOfDay
      ? new Date(Number(year), Number(month) - 1, Number(day), 23, 59, 59, 999)
      : new Date(Number(year), Number(month) - 1, Number(day));
    return date.toISOString();
  }

  private valueText(value: unknown): string {
    if (value === undefined || value === null) return this.localization.auditLog.noValue;
    return typeof value === 'string' ? value : JSON.stringify(value);
  }
}
