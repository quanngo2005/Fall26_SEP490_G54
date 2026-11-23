import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { notificationLink, NotificationItem } from '../../core/notification.models';
import { NotificationService } from '../../core/notification.service';

/** Pop-ups for important notifications that arrive while the app is open. */
@Component({
  selector: 'app-notification-toasts',
  template: `
    @let t = localization.notificationCenter;
<div class="toast-stack" role="status" aria-live="polite">
  @for (item of notifications.toasts(); track item.id) {
    <article class="toast">
      <div class="toast-main">
        <strong>{{item.title}}</strong>
        @if (item.body) {
          <p>{{item.body}}</p>
        }
      </div>
      <div class="toast-actions">
        <button type="button" class="toast-open" (click)="open(item)">{{t.open}}</button>
        <button
          type="button"
          class="toast-close"
          [attr.aria-label]="t.dismiss"
          (click)="notifications.dismissToast(item.id)"
        >
          ×
        </button>
      </div>
    </article>
  }
</div>
  `,
  styles: [
    `
      .toast-stack {
        position: fixed;
        right: 16px;
        bottom: 16px;
        z-index: 1100;
        display: flex;
        flex-direction: column;
        gap: 10px;
        width: min(360px, calc(100vw - 32px));
        font-family: 'Nunito Sans', sans-serif;
      }

      .toast {
        display: flex;
        align-items: flex-start;
        justify-content: space-between;
        gap: 12px;
        padding: 12px 14px;
        border: 1px solid #e2e8f0;
        border-left: 4px solid #e30613;
        border-radius: 8px;
        background: #ffffff;
        box-shadow:
          0 20px 25px -5px rgb(15 23 42 / 12%),
          0 8px 10px -6px rgb(15 23 42 / 8%);
      }

      .toast-main {
        min-width: 0;
      }

      .toast-main strong {
        color: #0f172a;
        font-size: 13.5px;
      }

      .toast-main p {
        display: -webkit-box;
        margin: 4px 0 0;
        overflow: hidden;
        color: #475569;
        font-size: 12.5px;
        line-height: 17px;
        -webkit-line-clamp: 3;
        line-clamp: 3;
        -webkit-box-orient: vertical;
      }

      .toast-actions {
        display: flex;
        flex: none;
        align-items: center;
        gap: 6px;
      }

      button {
        border: 0;
        background: transparent;
        cursor: pointer;
        font: inherit;
      }

      .toast-open {
        color: #1d4ed8;
        font-size: 13px;
        font-weight: 600;
      }

      .toast-close {
        width: 24px;
        height: 24px;
        border-radius: 4px;
        color: #64748b;
        font-size: 18px;
        line-height: 1;
      }

      .toast-close:hover {
        background: #f1f5f9;
      }

      button:focus-visible {
        outline: 2px solid #2563eb;
        outline-offset: 2px;
      }
    `,
  ],
})
export class NotificationToastsComponent extends LocalizedComponent {
  protected readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);

  open(item: NotificationItem): void {
    this.notifications.dismissToast(item.id);
    if (!item.isRead) this.notifications.markRead(item).subscribe({ error: () => undefined });
    const link = notificationLink(item);
    void this.router.navigate(link ? link.commands : ['/notifications'], {
      state: link?.state,
    });
  }
}
