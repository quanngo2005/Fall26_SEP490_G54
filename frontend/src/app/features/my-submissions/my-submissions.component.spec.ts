import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { MySubmissionDetail, MySubmissionListItem } from '../../core/my-submission.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { MySubmissionsComponent } from './my-submissions.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const listUrl = `${environment.apiUrl}/submissions/mine`;
const envelope = <T>(data: T) => ({ success: true, status: 200, data });

const item = (over: Partial<MySubmissionListItem> = {}): MySubmissionListItem => ({
  submissionId: 's1',
  submissionCode: '#SUB-WK-1018-V2',
  submissionNo: 2,
  workId: 'w1',
  workCode: 'WK-1018',
  workTitle: 'Mở tiền gửi',
  workTypeName: 'Tiền gửi',
  submittedAt: '2026-10-20T09:12:40+00:00',
  status: 'SUBMITTED',
  matchStatus: 'NOT_MATCHED',
  transactionCount: 2,
  matchedTransactionCount: 1,
  firstTransactionCode: 'TXN-77001',
  appealDeadline: null,
  ...over,
});

const detail = (over: Partial<MySubmissionDetail> = {}): MySubmissionDetail => ({
  submissionId: 's1',
  submissionCode: '#SUB-WK-1018-V2',
  submissionNo: 2,
  workId: 'w1',
  workCode: 'WK-1018',
  workTitle: 'Mở tiền gửi',
  workTypeName: 'Tiền gửi',
  submittedAt: '2026-10-20T09:12:40+00:00',
  status: 'SUBMITTED',
  matchStatus: 'NOT_MATCHED',
  evidences: [],
  transactions: [
    {
      transactionCode: 'TXN-77001',
      status: 'NOT_MATCHED',
      reason: 'PRODUCT_MISMATCH',
      fields: [
        { field: 'TRANSACTION_CODE', declared: 'TXN-77001', imported: 'TXN-77001', matched: true },
        { field: 'PRODUCT', declared: 'DEPOSIT', imported: 'CREDIT_CARD', matched: false },
        { field: 'AMOUNT', declared: null, imported: '95000000', matched: null },
      ],
    },
    {
      transactionCode: 'TXN-77002',
      status: 'IN_REVIEW',
      reason: null,
      fields: [{ field: 'TRANSACTION_CODE', declared: 'TXN-77002', imported: null, matched: null }],
    },
  ],
  versions: [
    {
      submissionId: 's1',
      submissionNo: 2,
      submittedAt: '2026-10-20T09:12:40+00:00',
      status: 'SUBMITTED',
      matchStatus: 'NOT_MATCHED',
    },
    {
      submissionId: 's0',
      submissionNo: 1,
      submittedAt: '2026-10-19T15:40:00+00:00',
      status: 'REJECTED',
      matchStatus: 'NOT_MATCHED',
    },
  ],
  appeal: { state: 'NOT_APPLICABLE', deadline: null, appealId: null },
  ...over,
});

describe('MySubmissionsComponent', () => {
  let http: HttpTestingController;

  const open = (items: MySubmissionListItem[] = [item()], totalCount = items.length) => {
    const fixture = TestBed.createComponent(MySubmissionsComponent);
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/evaluations/periods`).flush({
      success: true,
      status: 200,
      data: [{ id: 'p1', code: '2026-10', name: 'Tháng 10', status: 'OPEN', isActive: true }],
    });
    http
      .expectOne((r) => r.url === listUrl)
      .flush(envelope({ items, totalCount, page: 1, pageSize: 20 }));
    fixture.detectChanges();
    return fixture;
  };

  const select = (fixture: ReturnType<typeof open>, body: MySubmissionDetail) => {
    fixture.nativeElement.querySelector('tbody tr').click();
    http.expectOne(`${environment.apiUrl}/submissions/s1/reconciliation`).flush(envelope(body));
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MySubmissionsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    })
      .overrideComponent(MySubmissionsComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('defaults to the active period and shows the summary and the row', () => {
    const fixture = open();
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Lịch sử nộp kết quả');
    expect(text).toContain('Kỳ 2026-10 · 1 bài nộp');
    expect(text).toContain('WK-1018');
    expect(text).toContain('v2');
    expect(text).toContain('TXN-77001 +1');
    expect(text).toContain('Không khớp');
    expect(text).toContain('1/2 mã khớp');
    expect(text).not.toContain('MSG17');
  });

  it('says "all periods" instead of repeating the word period when no period is chosen', () => {
    const fixture = open();
    const select: HTMLSelectElement = fixture.nativeElement.querySelector(
      'select[aria-label="Lọc theo kỳ"]',
    );
    select.value = 'ALL';
    select.dispatchEvent(new Event('change'));
    http
      .expectOne((r) => r.url === listUrl)
      .flush(envelope({ items: [item()], totalCount: 1, page: 1, pageSize: 20 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Tất cả kỳ · 1 bài nộp');
    expect(fixture.nativeElement.textContent).not.toContain('Kỳ Tất cả kỳ');
  });

  it('sends the active period to the API on the first load', () => {
    const fixture = TestBed.createComponent(MySubmissionsComponent);
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/evaluations/periods`).flush({
      success: true,
      status: 200,
      data: [{ id: 'p1', code: '2026-10', name: 'Tháng 10', status: 'OPEN', isActive: true }],
    });
    const request = http.expectOne((r) => r.url === listUrl);
    expect(request.request.params.get('periodId')).toBe('p1');
    request.flush(envelope({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
  });

  it('shows the empty state and an accurate footer when there is nothing', () => {
    const fixture = open([], 0);
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Chưa có bài nộp nào phù hợp.');
    expect(text).toContain('0–0 trong 0');
  });

  it('shows an error with a retry button instead of rows', () => {
    const fixture = TestBed.createComponent(MySubmissionsComponent);
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiUrl}/evaluations/periods`)
      .flush([], { status: 500, statusText: 'x' });
    http.expectOne((r) => r.url === listUrl).flush({}, { status: 500, statusText: 'x' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    const retry: HTMLButtonElement = fixture.nativeElement.querySelector('.state-block button');
    retry.click();
    http
      .expectOne((r) => r.url === listUrl)
      .flush(envelope({ items: [item()], totalCount: 1, page: 1, pageSize: 20 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('WK-1018');
  });

  it('filters by reconciliation and goes back to the first page', () => {
    const fixture = open();
    const select$: HTMLSelectElement = fixture.nativeElement.querySelector(
      'select[aria-label="Lọc theo đối soát"]',
    );
    select$.value = 'MATCHED';
    select$.dispatchEvent(new Event('change'));
    const request = http.expectOne((r) => r.url === listUrl);
    expect(request.request.params.get('matchStatus')).toBe('MATCHED');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(envelope({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
  });

  it('opens the drawer with a field-by-field comparison and marks the mismatch', () => {
    const fixture = open();
    select(fixture, detail());
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.appeal-drawer')?.textContent).toContain('WK-1018 · Phiên bản 2');
    expect(root.textContent).toContain('Sản phẩm không đúng với loại việc.');
    expect(root.querySelectorAll('.compare-table tr.mismatch').length).toBe(1);
    expect(root.querySelector('.mark.bad')?.getAttribute('aria-label')).toBe('Không khớp');
    expect(root.querySelector('.mark.ok')?.getAttribute('aria-label')).toBe('Khớp');
    // A code with no import yet has nothing to compare.
    expect(root.textContent).toContain('Chưa có dữ liệu giao dịch để so khớp.');
    expect(root.textContent).toContain('95.000.000');
    expect(root.textContent).not.toContain('BR-31');
  });

  it('lists the versions and loads an earlier one without changing the list', () => {
    const fixture = open();
    select(fixture, detail());
    const rows = fixture.nativeElement.querySelectorAll('.version-row');
    expect(rows.length).toBe(2);
    expect(rows[0].getAttribute('aria-current')).toBe('true');
    rows[1].click();
    http
      .expectOne(`${environment.apiUrl}/submissions/s0/reconciliation`)
      .flush(envelope(detail({ submissionId: 's0', submissionNo: 1, status: 'REJECTED' })));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.appeal-drawer')?.textContent).toContain(
      'Phiên bản 1',
    );
  });

  it('enables the appeal button only inside the appeal window', () => {
    const fixture = open([item({ status: 'REJECTED', appealDeadline: '2099-01-01T00:00:00Z' })]);
    select(
      fixture,
      detail({
        status: 'REJECTED',
        appeal: { state: 'AVAILABLE', deadline: '2099-01-01T00:00:00Z', appealId: null },
      }),
    );
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('.drawer-footer button');
    expect(button.disabled).toBe(false);
    expect(button.textContent).toContain('Khiếu nại kết quả');
  });

  it('disables the appeal button once the window has closed', () => {
    const fixture = open([item({ status: 'REJECTED' })]);
    select(
      fixture,
      detail({
        status: 'REJECTED',
        appeal: { state: 'EXPIRED', deadline: '2020-01-01T00:00:00Z', appealId: null },
      }),
    );
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('.drawer-footer button');
    expect(button.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Đã hết thời hạn khiếu nại');
  });

  it('links to the appeal when one is already being processed and offers no second appeal', () => {
    const fixture = open([item({ status: 'REJECTED' })]);
    select(
      fixture,
      detail({
        status: 'REJECTED',
        appeal: { state: 'IN_PROGRESS', deadline: null, appealId: 'a1' },
      }),
    );
    expect(fixture.nativeElement.querySelector('.drawer-footer button')).toBeNull();
    expect(fixture.nativeElement.querySelector('.drawer-footer a')?.getAttribute('href')).toBe(
      '/appeals/mine?appeal=a1',
    );
  });

  it('offers no appeal for a submission that was not rejected', () => {
    const fixture = open();
    select(fixture, detail());
    expect(fixture.nativeElement.querySelector('.drawer-footer button')).toBeNull();
  });

  it('closes the drawer with Escape', () => {
    const fixture = open();
    select(fixture, detail());
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.appeal-drawer')).toBeNull();
  });
});
