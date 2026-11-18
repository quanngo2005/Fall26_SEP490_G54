import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { NotificationItem } from '../../core/notification.models';
import { SidebarConfigurationService } from '../../core/sidebar-configuration.service';
import { of } from 'rxjs';
import { NotificationListComponent } from './notification-list.component';

const item = (id: string, over: Partial<NotificationItem> = {}): NotificationItem => ({
  id,
  type: 'TRANSACTION_MISMATCH',
  severity: 'IMPORTANT',
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

describe('NotificationListComponent', () => {
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/notifications`;
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });
  const page = (items: NotificationItem[], total = items.length) =>
    envelope({ items, totalCount: total, page: 1, pageSize: 20 });
  const user = signal({
    id: 'u1',
    email: 'u@test',
    fullName: 'User',
    roles: ['EMPLOYEE'],
    permissions: ['NOTIFICATION_READ'],
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentUser: user.asReadonly() } },
        { provide: SidebarConfigurationService, useValue: { current: () => of([]) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function render(perms: string[] = ['NOTIFICATION_READ']) {
    user.update((current) => ({ ...current, permissions: perms }));
    const fixture = TestBed.createComponent(NotificationListComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('loads the inbox and shows unread rows first-class', () => {
    const fixture = render();
    http.expectOne((r) => r.url === url).flush(page([item('a'), item('b', { isRead: true })]));
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelectorAll('tbody tr').length).toBe(2);
    expect(root.querySelectorAll('tbody tr.is-unread').length).toBe(1);
    expect(root.textContent).toContain('Giao dịch không khớp');
  });

  it('shows an empty state and an error state with retry', () => {
    const fixture = render();
    http.expectOne((r) => r.url === url).flush(page([]));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Không có thông báo nào.');

    fixture.componentInstance.load();
    http.expectOne((r) => r.url === url).flush({}, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.state-error')).not.toBeNull();

    (fixture.nativeElement.querySelector('.state-error button') as HTMLButtonElement).click();
    http.expectOne((r) => r.url === url).flush(page([item('a')]));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.state-error')).toBeNull();
  });

  it('applies the filters to the request and resets to the first page', () => {
    const fixture = render();
    http.expectOne((r) => r.url === url).flush(page([item('a')], 45));
    const component = fixture.componentInstance;

    component.goToPage(2);
    http
      .expectOne((r) => r.url === url && r.params.get('page') === '2')
      .flush(page([item('b')], 45));

    component.keyword.set(' mã ');
    component.readFilter.set('unread');
    component.typeFilter.set('TRANSACTION_MISMATCH');
    component.severityFilter.set('IMPORTANT');
    component.applyFilters();
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('keyword')).toBe('mã');
    expect(request.request.params.get('unread')).toBe('true');
    expect(request.request.params.get('type')).toBe('TRANSACTION_MISMATCH');
    expect(request.request.params.get('severity')).toBe('IMPORTANT');
    request.flush(page([]));

    component.readFilter.set('read');
    component.applyFilters();
    http.expectOne((r) => r.url === url && r.params.get('unread') === 'false').flush(page([]));

    component.clearFilters();
    const cleared = http.expectOne((r) => r.url === url);
    expect(cleared.request.params.has('keyword')).toBeFalse();
    expect(cleared.request.params.has('unread')).toBeFalse();
    cleared.flush(page([]));
  });

  it('opens a notification: marks it read and navigates to the related screen', () => {
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    const fixture = render();
    http.expectOne((r) => r.url === url).flush(page([item('a')]));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('tbody .ui-action-btn') as HTMLButtonElement).click();
    http.expectOne(`${url}/a/read`).flush(envelope(item('a', { isRead: true })));
    fixture.detectChanges();

    expect(navigate).toHaveBeenCalledWith(['/my-work'], { queryParams: { workId: 'w1' } });
    expect(fixture.nativeElement.querySelectorAll('tbody tr.is-unread').length).toBe(0);
  });

  it('marks all read and reloads', () => {
    const fixture = render();
    http.expectOne((r) => r.url === url).flush(page([item('a')]));
    fixture.componentInstance.markAllRead();
    http.expectOne(`${url}/read-all`).flush(envelope({ count: 1 }));
    http.expectOne((r) => r.url === url).flush(page([item('a', { isRead: true })]));
    expect(fixture.componentInstance.markingAll()).toBeFalse();
  });

  it('hides the send button without the send permission', () => {
    const fixture = render(['NOTIFICATION_READ']);
    http.expectOne((r) => r.url === url).flush(page([]));
    fixture.detectChanges();
    expect(fixture.componentInstance.canSend()).toBeFalse();
    expect(fixture.nativeElement.querySelector('.ui-page-actions .button-primary')).toBeNull();
  });

  it('sends an announcement to everyone in scope, or to the picked people', () => {
    const fixture = render(['NOTIFICATION_READ', 'notification:send']);
    http.expectOne((r) => r.url === url).flush(page([]));
    fixture.detectChanges();
    const component = fixture.componentInstance;
    expect(component.canSend()).toBeTrue();

    component.openSend();
    http.expectOne(`${url}/recipients`).flush(
      envelope([
        {
          id: 'e1',
          employeeCode: 'NV1',
          fullName: 'Lê Châu',
          departmentId: 'd',
          departmentName: 'Phòng KD',
          jobPositionTitle: 'Sales',
        },
        {
          id: 'e2',
          employeeCode: 'NV2',
          fullName: 'Phạm Dũng',
          departmentId: 'd',
          departmentName: 'Phòng KD',
          jobPositionTitle: 'Sales',
        },
      ]),
    );
    fixture.detectChanges();
    expect(component.sendBlocked()).toBeTrue();

    component.sendTitle.set('  Họp khẩn  ');
    component.sendBody.set('Nội dung');
    component.sendSeverity.set('IMPORTANT');
    expect(component.sendBlocked()).toBeFalse();
    component.send();
    let request = http.expectOne((r) => r.method === 'POST' && r.url === url);
    expect(request.request.body).toEqual({
      title: 'Họp khẩn',
      body: 'Nội dung',
      severity: 'IMPORTANT',
      recipientEmployeeIds: undefined,
    });
    request.flush(envelope({ recipientCount: 5 }));
    fixture.detectChanges();
    expect(component.sentCount()).toBe(5);
    expect(fixture.nativeElement.querySelector('.sent-note')?.textContent).toContain('5');

    component.openSend();
    component.sendTitle.set('Nhắc việc');
    component.sendTarget.set('picked');
    expect(component.sendBlocked()).toBeTrue();
    component.sendSearch.set('dũng');
    expect(component.visibleRecipients().map((r) => r.id)).toEqual(['e2']);
    component.pickAllVisible();
    expect(component.sendBlocked()).toBeFalse();
    component.send();
    request = http.expectOne((r) => r.method === 'POST' && r.url === url);
    expect(request.request.body.recipientEmployeeIds).toEqual(['e2']);
    request.flush(
      { errors: ['Có người nhận ngoài phạm vi'] },
      { status: 403, statusText: 'Forbidden' },
    );
    expect(component.sendError()).toBe('Bạn không có quyền gửi thông cáo.');
  });

  it('shows the server validation message when sending fails', () => {
    const fixture = render(['NOTIFICATION_SEND']);
    http.expectOne((r) => r.url === url).flush(page([]));
    const component = fixture.componentInstance;
    component.openSend();
    http.expectOne(`${url}/recipients`).flush(envelope([]));
    component.sendTitle.set('Tiêu đề');
    component.send();
    http
      .expectOne((r) => r.method === 'POST' && r.url === url)
      .flush(
        { errors: ['Không có người nhận hợp lệ.'] },
        { status: 400, statusText: 'Bad Request' },
      );
    expect(component.sendError()).toBe('Không có người nhận hợp lệ.');
    expect(component.sending()).toBeFalse();
  });

  it('translates the page live', () => {
    const fixture = render();
    http.expectOne((r) => r.url === url).flush(page([item('a')]));
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1')?.textContent).toContain('Notifications');
    expect(fixture.nativeElement.textContent).toContain('Transaction mismatch');
    TestBed.inject(I18nService).setLanguage('vi');
  });
});
