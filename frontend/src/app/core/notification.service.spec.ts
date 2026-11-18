import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { fakeAsync, TestBed, tick } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';
import { NotificationItem } from './notification.models';
import { NotificationService } from './notification.service';

describe('NotificationService', () => {
  let http: HttpTestingController;
  let service: NotificationService;
  const url = `${environment.apiUrl}/notifications`;
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });
  const item = (id: string, over: Partial<NotificationItem> = {}): NotificationItem => ({
    id,
    type: 'ANNOUNCEMENT',
    severity: 'NORMAL',
    title: `Title ${id}`,
    body: null,
    relatedType: null,
    relatedId: null,
    senderEmployeeId: null,
    senderName: null,
    createdAt: '2026-11-17T09:00:00Z',
    isRead: false,
    readAt: null,
    ...over,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentUser: signal(null) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(NotificationService);
  });
  afterEach(() => http.verify());

  it('sends only the filters that have a value', () => {
    service
      .list({
        page: 2,
        pageSize: 20,
        unread: false,
        type: 'SYSTEM_ALERT',
        keyword: '',
        severity: '',
      })
      .subscribe((page) => expect(page.items).toEqual([]));
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('unread')).toBe('false');
    expect(request.request.params.get('type')).toBe('SYSTEM_ALERT');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.has('keyword')).toBeFalse();
    expect(request.request.params.has('severity')).toBeFalse();
    request.flush(envelope({ items: [], totalCount: 0, page: 2, pageSize: 20 }));
  });

  it('marks one notification read and lowers the unread count once', () => {
    service.unreadCount.set(3);
    service.recent.set([item('a'), item('b')]);

    service.markRead(item('a')).subscribe();
    http.expectOne(`${url}/a/read`).flush(envelope(item('a', { isRead: true })));

    expect(service.unreadCount()).toBe(2);
    expect(service.recent()[0].isRead).toBeTrue();

    // Reading an already read notification does not lower the count again.
    service.markRead(item('a', { isRead: true })).subscribe();
    http.expectOne(`${url}/a/read`).flush(envelope(item('a', { isRead: true })));
    expect(service.unreadCount()).toBe(2);
  });

  it('marks everything read', () => {
    service.unreadCount.set(4);
    service.recent.set([item('a'), item('b')]);

    service.markAllRead().subscribe((changed) => expect(changed).toBe(4));
    http.expectOne(`${url}/read-all`).flush(envelope({ count: 4 }));

    expect(service.unreadCount()).toBe(0);
    expect(service.recent().every((i) => i.isRead)).toBeTrue();
  });

  it('loads the recipients and sends an announcement', () => {
    service.recipients().subscribe((list) => expect(list.length).toBe(1));
    http.expectOne(`${url}/recipients`).flush(
      envelope([
        {
          id: 'e1',
          employeeCode: 'NV1',
          fullName: 'A',
          departmentId: 'd',
          departmentName: 'D',
          jobPositionTitle: 'P',
        },
      ]),
    );

    service
      .send({ title: 'Họp', severity: 'IMPORTANT', recipientEmployeeIds: ['e1'] })
      .subscribe((count) => expect(count).toBe(1));
    const request = http.expectOne(url);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      title: 'Họp',
      severity: 'IMPORTANT',
      recipientEmployeeIds: ['e1'],
    });
    request.flush(envelope({ recipientCount: 1 }));
  });

  it('refreshes the bell from the unread count and the latest page', () => {
    service.refreshSummary();
    http.expectOne(`${url}/unread-count`).flush(envelope({ count: 5 }));
    const list = http.expectOne((r) => r.url === url);
    expect(list.request.params.get('pageSize')).toBe('8');
    list.flush(envelope({ items: [item('a')], totalCount: 1, page: 1, pageSize: 8 }));

    expect(service.unreadCount()).toBe(5);
    expect(service.recent().length).toBe(1);
    expect(service.recentLoading()).toBeFalse();
  });

  it('keeps a failed refresh visible instead of faking an empty inbox', () => {
    service.refreshSummary();
    http.expectOne(`${url}/unread-count`).flush({}, { status: 500, statusText: 'Error' });
    http.expectOne((r) => r.url === url).flush({}, { status: 500, statusText: 'Error' });

    expect(service.recentFailed()).toBeTrue();
    expect(service.recentLoading()).toBeFalse();
  });

  it('rejects an unsuccessful envelope', () => {
    let failed = false;
    service.fetchUnreadCount().subscribe({ error: () => (failed = true) });
    http.expectOne(`${url}/unread-count`).flush({ success: false, status: 200, data: null });
    expect(failed).toBeTrue();
  });

  it('adds a live notification once and pops up only the important ones', fakeAsync(() => {
    service.receive(item('n1'));
    service.receive(item('n1'));
    service.receive(item('n2', { severity: 'IMPORTANT' }));

    expect(service.recent().map((i) => i.id)).toEqual(['n2', 'n1']);
    expect(service.unreadCount()).toBe(2);
    expect(service.arrivals()).toBe(2);
    expect(service.toasts().map((i) => i.id)).toEqual(['n2']);

    tick(8000);
    expect(service.toasts()).toEqual([]);
  }));

  it('keeps only the newest items in the dropdown', () => {
    for (let i = 0; i < 12; i++) service.receive(item(`n${i}`));
    expect(service.recent().length).toBe(8);
    expect(service.recent()[0].id).toBe('n11');
    expect(service.unreadCount()).toBe(12);
  });

  it('forgets the previous user at sign-out', () => {
    service.receive(item('x', { severity: 'IMPORTANT' }));
    service.deactivate();
    expect(service.unreadCount()).toBe(0);
    expect(service.recent()).toEqual([]);
    expect(service.toasts()).toEqual([]);
  });
});
