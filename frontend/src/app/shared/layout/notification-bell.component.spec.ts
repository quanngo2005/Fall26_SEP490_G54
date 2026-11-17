import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { NotificationItem } from '../../core/notification.models';
import { NotificationService } from '../../core/notification.service';
import { NotificationBellComponent } from './notification-bell.component';
import { NotificationToastsComponent } from './notification-toasts.component';

const item = (id: string, over: Partial<NotificationItem> = {}): NotificationItem => ({
  id,
  type: 'WORK_ASSIGNED',
  severity: 'NORMAL',
  title: `Title ${id}`,
  body: `Body ${id}`,
  relatedType: 'WORK',
  relatedId: 'w1',
  senderEmployeeId: null,
  senderName: null,
  createdAt: '2026-11-17T09:00:00Z',
  isRead: false,
  readAt: null,
  ...over,
});

describe('Notification bell and toasts', () => {
  let http: HttpTestingController;
  let service: NotificationService;
  const url = `${environment.apiUrl}/notifications`;
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentUser: signal(null) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(NotificationService);
  });
  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(NotificationBellComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the unread count on the bell and hides the badge at zero', () => {
    const fixture = render();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.notification-badge')).toBeNull();

    service.unreadCount.set(3);
    fixture.detectChanges();
    expect(root.querySelector('.notification-badge')?.textContent?.trim()).toBe('3');
    expect(root.querySelector('button')?.getAttribute('aria-label')).toBe('Thông báo, 3 chưa đọc');

    service.unreadCount.set(120);
    fixture.detectChanges();
    expect(root.querySelector('.notification-badge')?.textContent?.trim()).toBe('99+');
  });

  it('opens the dropdown, loads the latest notifications and lists them', () => {
    const fixture = render();
    const root: HTMLElement = fixture.nativeElement;
    (root.querySelector('.notification-btn') as HTMLButtonElement).click();
    http.expectOne(`${url}/unread-count`).flush(envelope({ count: 2 }));
    http
      .expectOne((r) => r.url === url)
      .flush(
        envelope({
          items: [item('a'), item('b', { isRead: true })],
          totalCount: 2,
          page: 1,
          pageSize: 8,
        }),
      );
    fixture.detectChanges();

    expect(root.querySelectorAll('.item').length).toBe(2);
    expect(root.querySelector('.item.is-unread .item-title')?.textContent).toContain('Title a');
    expect(root.querySelector('.panel')).not.toBeNull();
  });

  it('shows the empty and error states', () => {
    const fixture = render();
    const root: HTMLElement = fixture.nativeElement;
    (root.querySelector('.notification-btn') as HTMLButtonElement).click();
    http.expectOne(`${url}/unread-count`).flush(envelope({ count: 0 }));
    http
      .expectOne((r) => r.url === url)
      .flush(envelope({ items: [], totalCount: 0, page: 1, pageSize: 8 }));
    fixture.detectChanges();
    expect(root.querySelector('.state')?.textContent).toContain('Không có thông báo nào.');

    (root.querySelector('.notification-btn') as HTMLButtonElement).click(); // close
    (root.querySelector('.notification-btn') as HTMLButtonElement).click(); // open again
    http.expectOne(`${url}/unread-count`).flush({}, { status: 500, statusText: 'Error' });
    http.expectOne((r) => r.url === url).flush({}, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(root.querySelector('.state-error')?.textContent).toContain('Không tải được');
  });

  it('opens an item: marks it read and goes to its screen', () => {
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    service.recent.set([item('a')]);
    service.unreadCount.set(1);
    const fixture = render();
    const root: HTMLElement = fixture.nativeElement;
    (root.querySelector('.notification-btn') as HTMLButtonElement).click();
    http.expectOne(`${url}/unread-count`).flush(envelope({ count: 1 }));
    http
      .expectOne((r) => r.url === url)
      .flush(envelope({ items: [item('a')], totalCount: 1, page: 1, pageSize: 8 }));
    fixture.detectChanges();

    (root.querySelector('.item') as HTMLButtonElement).click();
    http.expectOne(`${url}/a/read`).flush(envelope(item('a', { isRead: true })));

    expect(navigate).toHaveBeenCalledWith(['/my-work'], { queryParams: { workId: 'w1' } });
    expect(service.unreadCount()).toBe(0);
  });

  it('marks all as read from the dropdown and closes on Escape', () => {
    service.unreadCount.set(2);
    service.recent.set([item('a'), item('b')]);
    const fixture = render();
    const root: HTMLElement = fixture.nativeElement;
    (root.querySelector('.notification-btn') as HTMLButtonElement).click();
    http.expectOne(`${url}/unread-count`).flush(envelope({ count: 2 }));
    http
      .expectOne((r) => r.url === url)
      .flush(envelope({ items: [item('a'), item('b')], totalCount: 2, page: 1, pageSize: 8 }));
    fixture.detectChanges();

    (root.querySelector('.link-button') as HTMLButtonElement).click();
    http.expectOne(`${url}/read-all`).flush(envelope({ count: 2 }));
    expect(service.unreadCount()).toBe(0);

    fixture.componentInstance.close();
    fixture.detectChanges();
    expect(root.querySelector('.panel')).toBeNull();
  });

  it('switches the dropdown language live', () => {
    const fixture = render();
    const root: HTMLElement = fixture.nativeElement;
    (root.querySelector('.notification-btn') as HTMLButtonElement).click();
    http.expectOne(`${url}/unread-count`).flush(envelope({ count: 0 }));
    http
      .expectOne((r) => r.url === url)
      .flush(envelope({ items: [], totalCount: 0, page: 1, pageSize: 8 }));
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(root.querySelector('.state')?.textContent).toContain('No notifications.');
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('shows important pop-ups, opens them and lets the user dismiss them', () => {
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(NotificationToastsComponent);
    service.toasts.set([
      item('t1', { severity: 'IMPORTANT' }),
      item('t2', { severity: 'IMPORTANT' }),
    ]);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelectorAll('.toast').length).toBe(2);

    (root.querySelectorAll('.toast-close')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root.querySelectorAll('.toast').length).toBe(1);

    (root.querySelector('.toast-open') as HTMLButtonElement).click();
    http.expectOne(`${url}/t1/read`).flush(envelope(item('t1', { isRead: true })));
    fixture.detectChanges();
    expect(navigate).toHaveBeenCalled();
    expect(root.querySelectorAll('.toast').length).toBe(0);
  });
});
