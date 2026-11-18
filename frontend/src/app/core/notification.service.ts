import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import type { HubConnection } from '@microsoft/signalr';
import { firstValueFrom, map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { AuthService } from './auth.service';
import {
  NotificationItem,
  NotificationQuery,
  NotificationRecipientOption,
  SendNotificationRequest,
} from './notification.models';
import { PagedResult } from './paged-result.model';

const RECENT_LIMIT = 8;
const TOAST_LIMIT = 3;
const TOAST_MS = 8000;
const RETRY_MS = 15000;

/**
 * Notification inbox (REST) plus the live push channel (SignalR `/hubs/notifications`). The channel is opened while
 * someone is signed in and closed at sign-out; a notification is always stored first, so a missed push is never lost.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly url = `${environment.apiUrl}/notifications`;
  private connection: HubConnection | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | undefined;
  private session = 0;

  readonly unreadCount = signal(0);
  /** Newest notifications for the bell dropdown. */
  readonly recent = signal<NotificationItem[]>([]);
  readonly recentLoading = signal(false);
  readonly recentFailed = signal(false);
  /** Important notifications that just arrived, shown as pop-ups. */
  readonly toasts = signal<NotificationItem[]>([]);
  /** Bumps on every live notification so open lists know to refresh. */
  readonly arrivals = signal(0);

  list(query: NotificationQuery): Observable<PagedResult<NotificationItem>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return this.http
      .get<ApiResponse<PagedResult<NotificationItem>>>(this.url, { params })
      .pipe(map((response) => this.unwrap(response)));
  }

  fetchUnreadCount(): Observable<number> {
    return this.http
      .get<ApiResponse<{ count: number }>>(`${this.url}/unread-count`)
      .pipe(map((response) => this.unwrap(response).count));
  }

  /** Marks one notification read and keeps the bell in step. */
  markRead(item: NotificationItem): Observable<NotificationItem> {
    return this.http
      .post<ApiResponse<NotificationItem>>(`${this.url}/${encodeURIComponent(item.id)}/read`, {})
      .pipe(
        map((response) => this.unwrap(response)),
        map((updated) => {
          this.applyRead(updated, !item.isRead);
          return updated;
        }),
      );
  }

  markAllRead(): Observable<number> {
    return this.http.post<ApiResponse<{ count: number }>>(`${this.url}/read-all`, {}).pipe(
      map((response) => this.unwrap(response).count),
      map((changed) => {
        this.unreadCount.set(0);
        this.recent.update((items) => items.map((i) => ({ ...i, isRead: true })));
        return changed;
      }),
    );
  }

  recipients(): Observable<NotificationRecipientOption[]> {
    return this.http
      .get<ApiResponse<NotificationRecipientOption[]>>(`${this.url}/recipients`)
      .pipe(map((response) => this.unwrap(response)));
  }

  send(request: SendNotificationRequest): Observable<number> {
    return this.http
      .post<ApiResponse<{ recipientCount: number }>>(this.url, request)
      .pipe(map((response) => this.unwrap(response).recipientCount));
  }

  /** Reloads the unread total and the dropdown list. */
  refreshSummary(): void {
    const session = this.session;
    this.recentLoading.set(true);
    this.recentFailed.set(false);
    this.fetchUnreadCount().subscribe({
      next: (count) => session === this.session && this.unreadCount.set(count),
      error: () => session === this.session && this.recentFailed.set(true),
    });
    this.list({ page: 1, pageSize: RECENT_LIMIT }).subscribe({
      next: (page) => {
        if (session !== this.session) return;
        this.recent.set(page.items);
        this.recentLoading.set(false);
      },
      error: () => {
        if (session !== this.session) return;
        this.recentFailed.set(true);
        this.recentLoading.set(false);
      },
    });
  }

  dismissToast(id: string): void {
    this.toasts.update((items) => items.filter((item) => item.id !== id));
  }

  /** Applies a notification pushed over SignalR. */
  receive(item: NotificationItem): void {
    if (this.recent().some((existing) => existing.id === item.id)) return;
    this.recent.update((items) => [item, ...items].slice(0, RECENT_LIMIT));
    if (!item.isRead) this.unreadCount.update((count) => count + 1);
    this.arrivals.update((value) => value + 1);
    if (item.severity === 'IMPORTANT') {
      this.toasts.update((items) => [item, ...items].slice(0, TOAST_LIMIT));
      setTimeout(() => this.dismissToast(item.id), TOAST_MS);
    }
  }

  private applyRead(updated: NotificationItem, changed: boolean): void {
    this.recent.update((items) => items.map((i) => (i.id === updated.id ? updated : i)));
    if (changed) this.unreadCount.update((count) => Math.max(0, count - 1));
  }

  /** Opens the live channel and loads the bell for the signed-in user (called once by the app shell). */
  activate(): void {
    this.deactivate();
    const session = ++this.session;
    this.refreshSummary();
    void this.connect(session);
  }

  /** Closes the live channel and forgets the previous user's notifications. */
  deactivate(): void {
    this.session++;
    clearTimeout(this.retryTimer);
    const connection = this.connection;
    this.connection = null;
    if (connection) void connection.stop();
    this.unreadCount.set(0);
    this.recent.set([]);
    this.toasts.set([]);
    this.recentFailed.set(false);
    this.recentLoading.set(false);
  }

  private async connect(session: number): Promise<void> {
    // Loaded on demand so the SignalR client stays out of the initial bundle.
    const { HubConnectionBuilder, HubConnectionState, LogLevel } =
      await import('@microsoft/signalr');
    if (session !== this.session) return;
    const connection = new HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/hubs/notifications`, {
        // Refreshes an expired access token first: the factory runs again on every automatic reconnect.
        accessTokenFactory: async () => {
          await firstValueFrom(this.auth.ensureAuthenticated());
          return this.auth.accessToken() ?? '';
        },
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();
    connection.on('notification', (item: NotificationItem) => {
      if (session === this.session) this.receive(item);
    });
    connection.onreconnected(() => {
      if (session === this.session) this.refreshSummary();
    });
    this.connection = connection;

    try {
      await connection.start();
    } catch {
      // The inbox still works over REST; try the live channel again later while this session lasts.
      if (session !== this.session) return;
      this.connection = null;
      this.retryTimer = setTimeout(() => {
        if (session === this.session) void this.connect(session);
      }, RETRY_MS);
      return;
    }

    if (session !== this.session && connection.state !== HubConnectionState.Disconnected) {
      void connection.stop();
    }
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error(response.message ?? 'Invalid notification response');
    }
    return response.data;
  }
}
