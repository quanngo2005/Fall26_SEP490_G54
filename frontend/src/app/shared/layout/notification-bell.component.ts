import { Component, ElementRef, HostListener, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { notificationLink, NotificationItem } from '../../core/notification.models';
import { NotificationService } from '../../core/notification.service';
import { LayoutIconComponent } from './layout-icon.component';

/** Header bell: unread badge and a dropdown with the latest notifications (SCR-NTF-01). */
@Component({
  selector: 'app-notification-bell',
  imports: [RouterLink, LayoutIconComponent],
  templateUrl: './notification-bell.component.html',
  styleUrl: './notification-bell.component.scss',
})
export class NotificationBellComponent extends LocalizedComponent {
  protected readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly open = signal(false);

  get badgeText(): string {
    const count = this.notifications.unreadCount();
    return count > 99 ? '99+' : String(count);
  }

  get bellLabel(): string {
    return this.i18n.translate('notificationCenter.bellLabel', {
      count: this.notifications.unreadCount(),
    });
  }

  toggle(): void {
    const next = !this.open();
    this.open.set(next);
    if (next) this.notifications.refreshSummary();
  }

  @HostListener('document:click', ['$event'])
  closeOnOutsideClick(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node))
      this.open.set(false);
  }

  @HostListener('document:keydown.escape')
  close(): void {
    this.open.set(false);
  }

  typeLabel(item: NotificationItem): string {
    return this.localization.notificationCenter.types[item.type] ?? item.type;
  }

  time(item: NotificationItem): string {
    return this.i18n.formatDate(item.createdAt, 'DD/MM HH:mm');
  }

  markAll(): void {
    this.notifications
      .markAllRead()
      .subscribe({ error: () => this.notifications.refreshSummary() });
  }

  openItem(item: NotificationItem): void {
    this.open.set(false);
    if (!item.isRead) this.notifications.markRead(item).subscribe({ error: () => undefined });
    const link = notificationLink(item);
    if (link) void this.router.navigate(link.commands, { queryParams: link.queryParams });
    else void this.router.navigate(['/notifications']);
  }
}
