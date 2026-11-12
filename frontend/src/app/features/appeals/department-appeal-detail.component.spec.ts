import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AppealDetail } from '../../core/appeal.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { DepartmentAppealDetailComponent } from './department-appeal-detail.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const id = '9a8b7c6d-1111-2222-3333-444455556666';
const url = `${environment.apiUrl}/appeals/${id}`;
const detail = (over: Partial<AppealDetail> = {}): AppealDetail => ({
  id,
  resultSubmissionId: 's1',
  workId: 'w1',
  workCode: 'WRK-1',
  workTitle: 'Mở thẻ',
  submissionNo: 2,
  employeeId: 'e2',
  employeeCode: 'NV002',
  employeeName: 'Lê Thị Mai',
  status: 'OPEN',
  filedAt: '2026-10-24T10:00:00+00:00',
  decidedAt: null,
  updatedAt: '2026-10-24T10:00:00+00:00',
  reason: 'Giao dịch do tôi xử lý',
  note: null,
  resolvedByEmployeeId: null,
  resolvedByEmployeeName: null,
  evidences: [
    {
      id: 'ev1',
      fileName: 'phieu.pdf',
      fileSize: 2048,
      mimeType: 'application/pdf',
      previewUrl: '/evidence/ev1/preview',
      downloadUrl: '/evidence/ev1/download',
    },
  ],
  submittedAt: '2026-10-24T09:00:00+00:00',
  workTypeName: 'Thẻ tín dụng',
  referrerEmployeeCode: 'NV009',
  referrerEmployeeName: 'Phạm Văn Giới',
  ...over,
});
const ok = (data: unknown) => ({ success: true, status: 200, data });
const decided = (appeal: AppealDetail, decisionValue: string) =>
  ok({
    appeal,
    decision: decisionValue,
    submissionStatus: 'APPROVED',
    workStatus: 'APPROVED',
    verificationId: null,
  });

describe('DepartmentAppealDetailComponent', () => {
  let http: HttpTestingController;

  const open = (data: AppealDetail = detail()) => {
    const fixture = TestBed.createComponent(DepartmentAppealDetailComponent);
    fixture.detectChanges();
    http.expectOne(url).flush(ok(data));
    fixture.detectChanges();
    return fixture;
  };
  const openModal = (fixture: ReturnType<typeof open>) => {
    fixture.nativeElement.querySelector('.btn-primary')?.click();
    fixture.detectChanges();
  };
  const type = (fixture: ReturnType<typeof open>, selector: string, value: string) => {
    const field = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const confirm = (fixture: ReturnType<typeof open>) => {
    fixture.nativeElement.querySelector('.modal-footer .btn-primary')?.click();
    fixture.detectChanges();
  };
  const decide = (fixture: ReturnType<typeof open>, value: string, note: string) => {
    openModal(fixture);
    fixture.nativeElement.querySelector(`input[type="radio"][value="${value}"]`)?.click();
    fixture.detectChanges();
    type(fixture, '#decisionNote', note);
    confirm(fixture);
  };
  const rowValues = (fixture: ReturnType<typeof open>) => {
    const rows = new Map<string, string>();
    for (const row of Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('.reconcile-row'),
    )) {
      rows.set(
        row.querySelector('.row-label')?.textContent?.trim() ?? '',
        row.querySelector('.row-val')?.textContent?.trim() ?? '',
      );
    }
    return rows;
  };

  beforeEach(async () => {
    history.replaceState({ appealId: id }, '');
    await TestBed.configureTestingModule({
      imports: [DepartmentAppealDetailComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    })
      .overrideComponent(DepartmentAppealDetailComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => {
    history.replaceState(null, '');
    http.verify();
  });

  it('loads the appeal by the id in the route and shows the real data', () => {
    const fixture = open();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.textContent).toContain('9A8B7C6D');
    expect(root.textContent).toContain('Lê Thị Mai');
    expect(root.textContent).toContain('Giao dịch do tôi xử lý');
    expect(root.textContent).toContain('phieu.pdf');
    expect(root.textContent).not.toContain('APL-0418');
    expect(root.textContent).not.toContain('NV0261');
  });

  it('shows the submission time, product and referrer of the API', () => {
    const rows = rowValues(open());
    expect(rows.get('Sản phẩm')).toBe('Thẻ tín dụng');
    expect(rows.get('Mã giới thiệu')).toBe('NV009 · Phạm Văn Giới');
    expect(rows.get('Bài nộp')).toBe('WRK-1 · v2');
  });

  it('shows the submission time in the card header', () => {
    const subtitle = open().nativeElement.querySelector('.card-subtitle')?.textContent;
    expect(subtitle).toMatch(/24\/10\/2026 \d{2}:\d{2}/);
  });

  it('shows N/A for the data the API does not have yet instead of sample values', () => {
    const fixture = open(
      detail({
        workTypeName: null,
        referrerEmployeeCode: null,
        referrerEmployeeName: null,
      }),
    );
    const rows = rowValues(fixture);
    for (const label of ['Mã giao dịch', 'Mã CIF', 'Sản phẩm', 'Mã giới thiệu', 'Đối soát']) {
      expect(rows.get(label)).withContext(label).toBe('N/A');
    }
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.sla-tag')?.textContent).toContain('N/A');
    expect(root.textContent).not.toContain('TX-20260715');
    expect(root.textContent).not.toContain('IMP-');
  });

  it('shows only the code when the referrer has no name', () => {
    const rows = rowValues(open(detail({ referrerEmployeeName: null })));
    expect(rows.get('Mã giới thiệu')).toBe('NV009');
  });

  it('goes back to the list without a request when the navigation state has no id', () => {
    history.replaceState(null, '');
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(DepartmentAppealDetailComponent);
    fixture.detectChanges();
    http.expectNone(() => true);
    expect(navigate).toHaveBeenCalledWith(['/appeals/department']);
    expect(fixture.nativeElement.querySelector('.action-header')).toBeNull();
  });

  it('shows a not-found state for 404 without a retry button', () => {
    const fixture = TestBed.createComponent(DepartmentAppealDetailComponent);
    fixture.detectChanges();
    http.expectOne(url).flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert.textContent).toContain('Không tìm thấy khiếu nại');
    expect(alert.querySelector('button')).toBeNull();
  });

  it('shows an error with retry for a failed load', () => {
    const fixture = TestBed.createComponent(DepartmentAppealDetailComponent);
    fixture.detectChanges();
    http.expectOne(url).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[role="alert"] button').click();
    http.expectOne(url).flush(ok(detail()));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Lê Thị Mai');
  });

  it('offers the three result decisions and sends the chosen one', () => {
    const fixture = open();
    openModal(fixture);
    const values = Array.from<HTMLInputElement>(
      fixture.nativeElement.querySelectorAll('input[type="radio"]'),
    ).map((radio) => radio.value);
    expect(values).toEqual(['ACCEPT', 'ADJUST', 'REJECT']);

    fixture.nativeElement.querySelector('input[value="ACCEPT"]').click();
    type(fixture, '#decisionNote', ' Chứng từ đối soát hợp lệ ');
    confirm(fixture);
    const request = http.expectOne(`${url}/decision`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ decision: 'ACCEPT', note: 'Chứng từ đối soát hợp lệ' });
    request.flush(decided(detail({ status: 'ACCEPTED' }), 'ACCEPT'));
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.modal-window')).toBeNull();
    expect(root.querySelector('.alert-banner')?.textContent).toContain('9A8B7C6D');
    expect(root.querySelector<HTMLButtonElement>('.btn-primary')?.disabled).toBeTrue();
  });

  it('sends adjusting a result appeal with its note only', () => {
    const fixture = open();
    decide(fixture, 'ADJUST', 'Bổ sung chứng từ đối soát');
    const request = http.expectOne(`${url}/decision`);
    expect(request.request.body).toEqual({
      decision: 'ADJUST',
      note: 'Bổ sung chứng từ đối soát',
    });
    request.flush(decided(detail({ status: 'ACCEPTED' }), 'ADJUST'));
  });

  it('requires a decision and a note before calling the API', () => {
    const fixture = open();
    openModal(fixture);
    confirm(fixture);
    http.expectNone(`${url}/decision`);
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Ý kiến xử lý là bắt buộc');
    expect(text).toContain('Dữ liệu không hợp lệ');
  });

  it('confirms with Ctrl+Enter and closes with Escape only while the window is open', () => {
    const fixture = open();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true }));
    http.expectNone(`${url}/decision`);

    openModal(fixture);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.modal-window')).toBeNull();

    decideAndKeys(fixture);
  });

  const decideAndKeys = (fixture: ReturnType<typeof open>) => {
    openModal(fixture);
    fixture.nativeElement.querySelector('input[value="REJECT"]').click();
    type(fixture, '#decisionNote', 'Giữ nguyên kết quả từ chối');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true }));
    const request = http.expectOne(`${url}/decision`);
    expect(request.request.body.decision).toBe('REJECT');
    request.flush(decided(detail({ status: 'REJECTED' }), 'REJECT'));
  };

  it('shows the error code message of a failed decision and keeps the window open', () => {
    const fixture = open();
    decide(fixture, 'ADJUST', 'Bổ sung chứng từ đối soát');
    http
      .expectOne(`${url}/decision`)
      .flush(
        { errorCode: 'MAKER_CANNOT_BE_CHECKER' },
        { status: 422, statusText: 'Unprocessable' },
      );
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.modal-window').textContent).toContain(
      'không được xử lý khiếu nại của chính mình',
    );
  });

  it('disables deciding on an appeal that is already decided', () => {
    const fixture = open(detail({ status: 'REJECTED' }));
    expect(
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.btn-primary')
        ?.disabled,
    ).toBeTrue();
  });

  it('disables the escalation button because escalating is not supported', () => {
    const fixture = open();
    const buttons = fixture.nativeElement.querySelectorAll('.header-right-actions button');
    expect(buttons[0].disabled).toBeTrue();
  });

  it('switches language live', () => {
    const fixture = open();
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(rowValues(fixture).has('Submission')).toBeTrue();
    expect(fixture.nativeElement.querySelector('.sla-tag').textContent).toContain('Due');
    openModal(fixture);
    expect(fixture.nativeElement.querySelector('.modal-footer').textContent).toContain(
      'Ctrl+Enter: confirm',
    );
  });

  it('fetches an evidence file as a blob and opens it', () => {
    const fixture = open();
    const openWindow = spyOn(window, 'open');
    spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    fixture.nativeElement.querySelector('.preview-btn').click();
    const request = http.expectOne(`${environment.apiUrl}/evidence/ev1/preview`);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['a']));
    expect(openWindow).toHaveBeenCalledWith('blob:x', '_blank', 'noopener,noreferrer');
  });

  it('shows an error when an evidence file cannot be opened', () => {
    const fixture = open();
    fixture.nativeElement.querySelector('.preview-btn').click();
    http
      .expectOne(`${environment.apiUrl}/evidence/ev1/preview`)
      .flush(null, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Không thể mở tệp bằng chứng');
  });
});
