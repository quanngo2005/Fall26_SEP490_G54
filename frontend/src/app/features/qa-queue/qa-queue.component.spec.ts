import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { fakeAsync, TestBed, tick } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { QaQueue, QaQueueItem } from '../../core/qa-queue.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { QaQueueContext } from './qa-queue-display';
import { QaQueueComponent } from './qa-queue.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const url = `${environment.apiUrl}/qa-queue`;
const inDays = (days: number) => new Date(Date.now() + days * 24 * 60 * 60 * 1000).toISOString();
const item = (over: Partial<QaQueueItem> = {}): QaQueueItem => ({
  kind: 'FLAG',
  type: 'FLAG',
  id: 'f1',
  code: 'QA-0331',
  source: 'MANUAL_FLAG',
  resultSubmissionId: 's1',
  submissionNo: 1,
  workId: 'w1',
  workCode: 'WK-1051',
  workTitle: 'Mở thẻ',
  employeeId: 'e1',
  employeeCode: 'NV0310',
  employeeName: 'T.Q.Bảo',
  departmentName: 'Phòng Bán lẻ',
  tab: 'PENDING',
  status: 'OPEN',
  severity: 'HIGH',
  createdAt: inDays(-1),
  dueAt: inDays(5),
  ...over,
});
const queue = (items: QaQueueItem[], over: Partial<QaQueue> = {}) => ({
  success: true,
  status: 200,
  data: {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 20,
    counts: { pending: items.length, confirmed: 2, rejected: 1 },
    departments: [{ id: 'd1', name: 'Phòng Bán lẻ' }],
    ...over,
  },
});

describe('QaQueueComponent', () => {
  let http: HttpTestingController;

  const open = () => {
    const fixture = TestBed.createComponent(QaQueueComponent);
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/evaluations/periods`).flush({
      success: true,
      status: 200,
      data: [{ id: 'p1', code: '2026-10', name: '10/2026', status: 'OPEN' }],
    });
    return fixture;
  };
  const listRequest = () => http.expectOne((r) => r.url === url);
  const text = (fixture: { nativeElement: HTMLElement }) =>
    (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QaQueueComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    })
      .overrideComponent(QaQueueComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('loads the pending tab first and shows the rows, the due text and the counts of every tab', () => {
    const fixture = open();
    const request = listRequest();
    expect(request.request.params.get('tab')).toBe('PENDING');
    request.flush(
      queue([
        item(),
        item({
          kind: 'SAMPLE',
          type: 'SAMPLE',
          id: 's2',
          code: 'QA-0329',
          source: 'RANDOM_SAMPLE',
          status: 'PENDING',
          severity: null,
        }),
      ]),
    );
    fixture.detectChanges();

    const body = text(fixture);
    expect(body).toContain('QA-0331');
    expect(body).toContain('WK-1051');
    expect(body).toContain('NV0310 · T.Q.Bảo');
    expect(body).toContain('còn 5 ngày');
    expect(body).toContain('Cờ chất lượng');
    expect(body).toContain('Mẫu 5%');
    expect(body).toContain('Hệ thống chọn ngẫu nhiên');
    expect(body).toContain('Chờ xử lý 2');
    expect(body).toContain('Đã xác nhận 2');
    expect(body).toContain('Đã bác bỏ 1');
    expect(fixture.nativeElement.querySelectorAll('tbody tr.clickable-row').length).toBe(2);
    expect(TestBed.inject(QaQueueContext).items()).toEqual([
      { kind: 'FLAG', id: 'f1' },
      { kind: 'SAMPLE', id: 's2' },
    ]);
  });

  it('marks an item that is due within a day as urgent and says when one is overdue', () => {
    const fixture = open();
    listRequest().flush(
      queue([item({ dueAt: inDays(0.4) }), item({ id: 'f2', code: 'QA-0332', dueAt: inDays(-3) })]),
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.urgent-cell').length).toBe(2);
    expect(text(fixture)).toContain('quá hạn 3 ngày');
  });

  it('does not show a deadline once the item is handled', () => {
    const fixture = open();
    const request = listRequest();
    request.flush(queue([item({ tab: 'CONFIRMED', status: 'DISMISSED', dueAt: inDays(-9) })]));
    fixture.detectChanges();

    expect(text(fixture)).not.toContain('quá hạn');
    expect(text(fixture)).toContain('Đã xác nhận');
  });

  it('reloads the chosen tab from the first page', () => {
    const fixture = open();
    listRequest().flush(queue([item()]));
    fixture.detectChanges();

    const tabs = fixture.nativeElement.querySelectorAll('button[role="tab"]');
    (tabs[2] as HTMLButtonElement).click();
    const request = listRequest();
    expect(request.request.params.get('tab')).toBe('REJECTED');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(queue([], { counts: { pending: 1, confirmed: 2, rejected: 0 } }));
    fixture.detectChanges();

    expect(text(fixture)).toContain('Không có mục nào trong nhóm này.');
  });

  it('sends the kind, department and period filters and offers the departments of the queue', () => {
    const fixture = open();
    listRequest().flush(queue([item()]));
    fixture.detectChanges();
    const selects = fixture.nativeElement.querySelectorAll(
      'select',
    ) as NodeListOf<HTMLSelectElement>;
    expect(selects[1].textContent).toContain('Phòng Bán lẻ');
    expect(selects[2].textContent).toContain('2026-10');

    const choose = (select: HTMLSelectElement, value: string) => {
      select.value = value;
      select.dispatchEvent(new Event('change'));
    };
    choose(selects[0], 'SAMPLE');
    expect(listRequest().request.params.get('kind')).toBe('SAMPLE');
    http.match((r) => r.url === url);
    choose(selects[1], 'd1');
    const byDepartment = listRequest();
    expect(byDepartment.request.params.get('departmentId')).toBe('d1');
    expect(byDepartment.request.params.get('kind')).toBe('SAMPLE');
    byDepartment.flush(queue([]));
    choose(selects[2], 'p1');
    expect(listRequest().request.params.get('periodId')).toBe('p1');
  });

  it('waits for the typing to stop before it searches', fakeAsync(() => {
    const fixture = TestBed.createComponent(QaQueueComponent);
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiUrl}/evaluations/periods`)
      .flush({ success: true, status: 200, data: [] });
    listRequest().flush(queue([item()]));
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[type="text"]') as HTMLInputElement;
    for (const value of ['Q', 'QA', 'QA-0331']) {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      tick(100);
    }
    http.expectNone((r) => r.url === url);
    tick(300);
    const request = listRequest();
    expect(request.request.params.get('search')).toBe('QA-0331');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(queue([item()]));
  }));

  it('pages through the queue', () => {
    const fixture = open();
    listRequest().flush(queue([item()], { totalCount: 45, pageSize: 20 }));
    fixture.detectChanges();

    const pages = fixture.nativeElement.querySelectorAll(
      '.page-box',
    ) as NodeListOf<HTMLButtonElement>;
    expect(pages.length).toBe(3);
    pages[2].click();
    expect(listRequest().request.params.get('page')).toBe('3');
  });

  it('opens the detail of a row', () => {
    const fixture = open();
    listRequest().flush(queue([item({ kind: 'SAMPLE', type: 'SAMPLE', id: 's9' })]));
    fixture.detectChanges();
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    (fixture.nativeElement.querySelector('tbody tr') as HTMLElement).click();

    expect(navigate).toHaveBeenCalledWith(['/qa-queue', 'SAMPLE', 's9']);
  });

  it('keeps the filters usable when the periods cannot be read, and retries a failed list', () => {
    const fixture = TestBed.createComponent(QaQueueComponent);
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiUrl}/evaluations/periods`)
      .flush('', { status: 403, statusText: 'Forbidden' });
    listRequest().flush('', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(text(fixture)).toContain('Không tải được hàng chờ QA');
    (fixture.nativeElement.querySelector('.state-block button') as HTMLButtonElement).click();
    listRequest().flush(queue([item()]));
    fixture.detectChanges();
    expect(text(fixture)).toContain('QA-0331');
  });
});
