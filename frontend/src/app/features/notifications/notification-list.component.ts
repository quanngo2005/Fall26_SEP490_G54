import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  computed,
  DestroyRef,
  effect,
  HostListener,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  NOTIFICATION_TYPES,
  notificationLink,
  NotificationItem,
  NotificationQuery,
  NotificationRecipientOption,
  NotificationSeverity,
  NotificationType,
} from '../../core/notification.models';
import { NotificationService } from '../../core/notification.service';
import { HeaderComponent, LayoutIconComponent, SidebarComponent } from '../../shared/layout';
import {
  SelectOption,
  StatusBadgeComponent,
  TablePaginationComponent,
  UiSelectComponent,
} from '../../shared/ui';

const PAGE_SIZE = 20;
const DATE_TIME = 'DD/MM/YYYY HH:mm';
const TITLE_MAX = 200;
const BODY_MAX = 2000;

type ReadFilter = 'all' | 'unread' | 'read';
type SendTarget = 'scope' | 'picked';

/** SCR-NTF-01: the signed-in user's inbox, plus announcements for managers. Contract: docs/api/NOTIFICATION_CONTRACT.md. */
@Component({
  selector: 'app-notification-list',
  imports: [
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    StatusBadgeComponent,
    TablePaginationComponent,
    UiSelectComponent,
  ],
  templateUrl: './notification-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/modal.scss',
    './notification-list.component.scss',
  ],
})
export class NotificationListComponent extends LocalizedComponent {
  private readonly api = inject(NotificationService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private listRequest?: Subscription;

  readonly pageSize = PAGE_SIZE;
  readonly titleMax = TITLE_MAX;
  readonly bodyMax = BODY_MAX;

  readonly keyword = signal('');
  readonly readFilter = signal<ReadFilter>('all');
  readonly typeFilter = signal<NotificationType | ''>('');
  readonly severityFilter = signal<NotificationSeverity | ''>('');
  readonly page = signal(1);

  readonly items = signal<NotificationItem[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly failed = signal(false);
  readonly markingAll = signal(false);

  readonly canSend = computed(() =>
    (this.auth.currentUser()?.permissions ?? []).some(
      (code) => code.toUpperCase().replace(/[.:-]/g, '_') === 'NOTIFICATION_SEND',
    ),
  );
  readonly unreadCount = this.api.unreadCount;

  readonly readOptions = computed<SelectOption<ReadFilter>[]>(() => {
    const t = this.localization.notificationCenter;
    return [
      { value: 'all', label: t.filterAll },
      { value: 'unread', label: t.filterUnread },
      { value: 'read', label: t.filterRead },
    ];
  });
  readonly typeOptions = computed<SelectOption<NotificationType | ''>[]>(() => {
    const t = this.localization.notificationCenter;
    return [
      { value: '', label: t.typeAll },
      ...NOTIFICATION_TYPES.map((type) => ({ value: type, label: t.types[type] })),
    ];
  });
  readonly severityOptions = computed<SelectOption<NotificationSeverity | ''>[]>(() => {
    const t = this.localization.notificationCenter;
    return [
      { value: '', label: t.severityAll },
      { value: 'IMPORTANT', label: t.severities.IMPORTANT },
      { value: 'NORMAL', label: t.severities.NORMAL },
    ];
  });
  readonly hasFilters = computed(
    () =>
      !!this.keyword() ||
      this.readFilter() !== 'all' ||
      !!this.typeFilter() ||
      !!this.severityFilter(),
  );

  // Send announcement dialog
  readonly sendOpen = signal(false);
  readonly sendTitle = signal('');
  readonly sendBody = signal('');
  readonly sendSeverity = signal<NotificationSeverity>('NORMAL');
  readonly sendTarget = signal<SendTarget>('scope');
  readonly sendSearch = signal('');
  readonly sendPicked = signal<ReadonlySet<string>>(new Set());
  readonly recipients = signal<NotificationRecipientOption[]>([]);
  readonly recipientsLoading = signal(false);
  readonly recipientsFailed = signal(false);
  readonly sending = signal(false);
  readonly sendError = signal<string | null>(null);
  readonly sentCount = signal<number | null>(null);
  readonly titleInvalid = computed(() => !this.sendTitle().trim());
  readonly sendBlocked = computed(
    () =>
      this.sending() ||
      this.titleInvalid() ||
      this.sendTitle().trim().length > TITLE_MAX ||
      this.sendBody().length > BODY_MAX ||
      (this.sendTarget() === 'picked' && this.sendPicked().size === 0),
  );
  readonly severitySendOptions = computed<SelectOption<NotificationSeverity>[]>(() => {
    const t = this.localization.notificationCenter;
    return [
      { value: 'NORMAL', label: t.severities.NORMAL },
      { value: 'IMPORTANT', label: t.severities.IMPORTANT },
    ];
  });
  readonly visibleRecipients = computed(() => {
    const text = this.sendSearch().trim().toLowerCase();
    if (!text) return this.recipients();
    return this.recipients().filter((r) =>
      `${r.fullName} ${r.employeeCode} ${r.departmentName}`.toLowerCase().includes(text),
    );
  });

  constructor() {
    super();
    this.destroyRef.onDestroy(() => this.listRequest?.unsubscribe());
    this.load();
    // A live notification refreshes the first page so the list never lags behind the bell.
    effect(() => {
      this.api.arrivals();
      untracked(() => {
        if (this.page() === 1 && this.api.arrivals() > 0) this.load();
      });
    });
  }

  applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  clearFilters(): void {
    this.keyword.set('');
    this.readFilter.set('all');
    this.typeFilter.set('');
    this.severityFilter.set('');
    this.applyFilters();
  }

  goToPage(page: number): void {
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

  open(item: NotificationItem): void {
    if (!item.isRead) {
      this.api
        .markRead(item)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (updated) =>
            this.items.update((rows) => rows.map((row) => (row.id === updated.id ? updated : row))),
          error: () => undefined,
        });
    }
    const link = notificationLink(item);
    if (link) void this.router.navigate(link.commands, { queryParams: link.queryParams });
  }

  hasLink(item: NotificationItem): boolean {
    return notificationLink(item) !== null;
  }

  markAllRead(): void {
    this.markingAll.set(true);
    this.api
      .markAllRead()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.markingAll.set(false);
          this.load();
        },
        error: () => {
          this.markingAll.set(false);
          this.failed.set(true);
        },
      });
  }

  typeLabel(type: NotificationType): string {
    return this.localization.notificationCenter.types[type] ?? type;
  }

  time(value: string): string {
    return this.i18n.formatDate(value, DATE_TIME);
  }

  // ---- send announcement ----

  openSend(): void {
    this.sendTitle.set('');
    this.sendBody.set('');
    this.sendSeverity.set('NORMAL');
    this.sendTarget.set('scope');
    this.sendSearch.set('');
    this.sendPicked.set(new Set());
    this.sendError.set(null);
    this.sentCount.set(null);
    this.sendOpen.set(true);
    if (this.recipients().length === 0) this.loadRecipients();
  }

  @HostListener('document:keydown.escape')
  closeSend(): void {
    if (!this.sending()) this.sendOpen.set(false);
  }

  loadRecipients(): void {
    this.recipientsLoading.set(true);
    this.recipientsFailed.set(false);
    this.api
      .recipients()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.recipients.set(list);
          this.recipientsLoading.set(false);
        },
        error: () => {
          this.recipientsFailed.set(true);
          this.recipientsLoading.set(false);
        },
      });
  }

  togglePicked(id: string): void {
    this.sendPicked.update((current) => {
      const next = new Set(current);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  pickAllVisible(): void {
    this.sendPicked.update((current) => {
      const next = new Set(current);
      for (const r of this.visibleRecipients()) next.add(r.id);
      return next;
    });
  }

  clearPicked(): void {
    this.sendPicked.set(new Set());
  }

  send(): void {
    if (this.sendBlocked()) return;
    this.sending.set(true);
    this.sendError.set(null);
    this.api
      .send({
        title: this.sendTitle().trim(),
        body: this.sendBody().trim() || undefined,
        severity: this.sendSeverity(),
        recipientEmployeeIds: this.sendTarget() === 'picked' ? [...this.sendPicked()] : undefined,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (count) => {
          this.sending.set(false);
          this.sentCount.set(count);
        },
        error: (error: HttpErrorResponse) => {
          this.sending.set(false);
          const messages =
            (error?.error as { errors?: string[]; message?: string } | undefined) ?? {};
          this.sendError.set(
            error?.status === 403
              ? this.localization.notificationCenter.sendForbidden
              : (messages.errors?.[0] ??
                  messages.message ??
                  this.localization.notificationCenter.sendFailed),
          );
        },
      });
  }

  private buildQuery(): NotificationQuery {
    const read = this.readFilter();
    return {
      page: this.page(),
      pageSize: PAGE_SIZE,
      keyword: this.keyword().trim(),
      type: this.typeFilter(),
      severity: this.severityFilter(),
      unread: read === 'unread' ? true : read === 'read' ? false : undefined,
    };
  }
}
