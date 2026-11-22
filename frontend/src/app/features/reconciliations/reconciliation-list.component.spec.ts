import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { fakeAsync, TestBed, tick } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { ReconciliationItem } from '../../core/reconciliation.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { SAVED_FILTERS_KEY } from './reconciliation-display';
import { ReconciliationListComponent } from './reconciliation-list.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const api = environment.apiUrl;
const url = `${api}/reconciliations`;
const envelope = <T>(data: T) => ({ success: true, status: 200, data });
const item = (over: Partial<ReconciliationItem> = {}): ReconciliationItem => ({
  id: 'r1',
  transactionCode: 'GD-20261021-0174',
  workId: 'w1',
  workCode: 'WK-1042',
  workTitle: 'Mở thẻ ghi nợ',
  employeeId: 'e1',
  employeeCode: 'NV0231',
  employeeName: 'N.M.Anh',
  jobTitle: 'Teller',
  departmentId: 'd1',
  departmentName: 'Phòng Dịch vụ KH',
  customerCif: 'CIF0012345',
  referEmployeeCode: null,
  productCode: 'TD_CLASSIC',
  amount: 50_000_000,
  transactionDate: '2026-10-21',
  status: 'NOT_MATCHED',
  reason: 'EMPLOYEE_MISMATCH',
  submittedAt: '2026-10-21T09:02:00Z',
  updatedAt: '2026-10-21T09:05:00Z',
  ...over,
});
const list = (items: ReconciliationItem[], totalCount = items.length) =>
  envelope({
    items,
    totalCount,
    page: 1,
    pageSize: 20,
    counts: { all: 1284, notMatched: 7, inReview: 12, matched: 1265 },
    options: {
      departments: [
        { id: 'd1', name: 'Phòng Dịch vụ KH' },
        { id: 'd2', name: 'Phòng Bán lẻ' },
      ],
      jobPositions: [{ id: 'j1', title: 'Teller' }],
      products: [{ code: 'TD_CLASSIC', name: 'Tiền gửi' }],
      reasons: ['EMPLOYEE_MISMATCH', 'NO_IMPORTED_TRANSACTION'],
    },
  });
const period = {
  id: 'p1',
  code: '2026-10',
  name: '10/2026',
  startDate: '2000-01-01',
  endDate: '2999-12-31',
  periodType: 'MONTH',
};

describe('ReconciliationListComponent', () => {
  let http: HttpTestingController;
  let navigate: jasmine.Spy;

  const open = (items: ReconciliationItem[] = [item()]) => {
    const fixture = TestBed.createComponent(ReconciliationListComponent);
    fixture.detectChanges();
    http.expectOne(`${api}/evaluations/periods`).flush(envelope([period]));
    http.expectOne((r) => r.url === url).flush(list(items));
    fixture.detectChanges();
    return fixture;
  };
  const text = (fixture: { nativeElement: HTMLElement }) =>
    (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
  const cards = (fixture: { nativeElement: HTMLElement }) =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.status-card'),
    );

  beforeEach(async () => {
    localStorage.removeItem(SAVED_FILTERS_KEY);
    await TestBed.configureTestingModule({
      imports: [ReconciliationListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    })
      .overrideComponent(ReconciliationListComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
  });

  afterEach(() => {
    http.verify();
    localStorage.removeItem(SAVED_FILTERS_KEY);
  });

  it('opens on the current period and shows the rows in plain Vietnamese', () => {
    const fixture = TestBed.createComponent(ReconciliationListComponent);
    fixture.detectChanges();
    http.expectOne(`${api}/evaluations/periods`).flush(envelope([period]));
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.params.get('periodId')).toBe('p1');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(list([item()]));
    fixture.detectChanges();
    const body = text(fixture);

    expect(body).toContain('Kết quả đối soát');
    expect(body).toContain('Đối soát tự động');
    expect(body).toContain('file giao dịch Core Banking đã được nhập');
    expect(body).toContain('WK-1042');
    expect(body).toContain('NV0231 · N.M.Anh');
    expect(body).toContain('GD-20261021-0174');
    expect(body).toContain('CIF0012345');
    expect(body).toContain('50.000.000');
    expect(body).toContain('Không khớp');
    expect(body).toContain('Sai nhân viên');
    expect(body).not.toContain('Refer ID');
    expect(body).not.toContain('Not Matched');
  });

  const signIn = (permissions: string[]) =>
    localStorage.setItem(
      'g54.auth.session',
      JSON.stringify({
        accessToken: 'a',
        refreshToken: 'r',
        expiresAt: new Date(Date.now() + 60_000).toISOString(),
        refreshTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
        user: { id: 'u1', email: 'qa@test.local', fullName: 'QA', roles: ['QA'], permissions },
      }),
    );
  const importLink = (fixture: { nativeElement: HTMLElement }) =>
    fixture.nativeElement.querySelector<HTMLAnchorElement>('a[href="/data-import"]');

  it('links to the data import screen for whoever may import the Core Banking file', () => {
    signIn(['RECONCILIATION_READ', 'TRANSACTION_IMPORT_MANAGE']);
    const fixture = open();

    expect(importLink(fixture)?.textContent?.trim()).toBe('Nhập file giao dịch');
    localStorage.removeItem('g54.auth.session');
  });

  it('hides the data import link from a reader without the import permission', () => {
    signIn(['RECONCILIATION_READ']);
    const fixture = open();

    expect(importLink(fixture)).toBeNull();
    localStorage.removeItem('g54.auth.session');
  });

  it('shows the count of every status on its card', () => {
    const fixture = open();
    const part = (selector: string) =>
      cards(fixture).map((c) => c.querySelector(selector)?.textContent?.trim());

    expect(part('.status-card-label')).toEqual([
      'Tất cả',
      'Không khớp',
      'Chờ đối chiếu',
      'Đã khớp',
    ]);
    expect(part('.status-card-count')).toEqual(['1.284', '7', '12', '1.265']);
  });

  it('asks for one status when its card is pressed and goes back to the first page', () => {
    const fixture = open();

    cards(fixture)[1].click();
    const request = http.expectOne((r) => r.url === url);

    expect(request.request.params.get('status')).toBe('NOT_MATCHED');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(list([item()]));
    fixture.detectChanges();
    expect(cards(fixture)[1].getAttribute('aria-pressed')).toBe('true');
  });

  it('searches after a short pause, not on every key', fakeAsync(() => {
    const fixture = TestBed.createComponent(ReconciliationListComponent);
    fixture.detectChanges();
    http.expectOne(`${api}/evaluations/periods`).flush(envelope([period]));
    http.expectOne((r) => r.url === url).flush(list([item()]));
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('.search-input') as HTMLInputElement;

    input.value = 'GD-0174';
    input.dispatchEvent(new Event('input'));
    http.expectNone((r) => r.url === url);
    tick(300);

    const request = http.expectOne((r) => r.url === url);
    expect(request.request.params.get('search')).toBe('GD-0174');
    request.flush(list([item()]));
  }));

  it('keeps the filters in the address', () => {
    const fixture = open();
    navigate.calls.reset();

    cards(fixture)[2].click();
    http.expectOne((r) => r.url === url).flush(list([]));

    const extras = navigate.calls.mostRecent().args[1] as { queryParams: Record<string, string> };
    expect(extras.queryParams['status']).toBe('IN_REVIEW');
    expect(extras.queryParams['period']).toBe('p1');
  });

  it('says there is nothing to show instead of an error', () => {
    const fixture = open([]);

    expect(text(fixture)).toContain('Không tìm thấy kết quả phù hợp.');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('offers a retry when the list cannot be loaded', () => {
    const fixture = TestBed.createComponent(ReconciliationListComponent);
    fixture.detectChanges();
    http.expectOne(`${api}/evaluations/periods`).flush(envelope([period]));
    http.expectOne((r) => r.url === url).flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(text(fixture)).toContain('Không tải được danh sách đối soát.');
    (fixture.nativeElement.querySelector('.state-error button') as HTMLButtonElement).click();
    http.expectOne((r) => r.url === url).flush(list([item()]));
    fixture.detectChanges();
    expect(text(fixture)).toContain('WK-1042');
  });

  it('opens the comparison of a row', () => {
    const fixture = open();

    (fixture.nativeElement.querySelector('tbody tr') as HTMLElement).click();

    expect(navigate).toHaveBeenCalledWith(['/reconciliations', 'r1']);
  });

  it('offers the department filter when more than one department is in view', () => {
    const fixture = open();

    expect(fixture.nativeElement.querySelectorAll('app-ui-select').length).toBe(5);
  });

  it('saves the filters for the next visit and forgets them on clear', () => {
    const fixture = open();
    cards(fixture)[1].click();
    http.expectOne((r) => r.url === url).flush(list([item()]));
    fixture.detectChanges();

    const link = (name: string) =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.link-action'),
      ).find((b) => b.textContent?.includes(name)) as HTMLButtonElement;
    link('Lưu bộ lọc').click();
    fixture.detectChanges();

    expect(JSON.parse(localStorage.getItem(SAVED_FILTERS_KEY) ?? '{}').status).toBe('NOT_MATCHED');
    expect(text(fixture)).toContain('Đã lưu bộ lọc');

    link('Xóa bộ lọc').click();
    http.expectOne((r) => r.url === url).flush(list([item()]));
    expect(localStorage.getItem(SAVED_FILTERS_KEY)).toBeNull();
  });

  it('exports with the current filters and reports a failure', () => {
    const fixture = open();
    const button = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
        '.ui-page-actions button',
      ),
    )[0];

    button.click();
    const request = http.expectOne((r) => r.url === `${url}/export`);
    expect(request.request.params.get('periodId')).toBe('p1');
    expect(request.request.params.has('page')).toBeFalse();
    request.flush(new Blob(), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(text(fixture)).toContain('Không xuất được file Excel');
  });
});
