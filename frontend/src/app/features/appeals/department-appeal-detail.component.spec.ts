import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
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
  type: 'RESULT',
  performanceEvaluationId: null,
  periodCode: null,
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
  scoreDisputeReason: null,
  ...over,
});
const ok = (data: unknown) => ({ success: true, status: 200, data });

describe('DepartmentAppealDetailComponent', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  const open = (data: AppealDetail = detail()) => {
    const fixture = TestBed.createComponent(DepartmentAppealDetailComponent);
    fixture.detectChanges();
    http.expectOne(url).flush(ok(data));
    fixture.detectChanges();
    return fixture;
  };
  const decide = (fixture: ReturnType<typeof open>, value: string, note: string) => {
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('.btn-primary')?.click();
    fixture.detectChanges();
    const radio = root.querySelector<HTMLInputElement>(`input[type="radio"][value="${value}"]`);
    radio?.click();
    const textarea = root.querySelector<HTMLTextAreaElement>('#decisionNote');
    if (textarea) {
      textarea.value = note;
      textarea.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('.modal-footer .btn-primary')?.click();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    params = new BehaviorSubject(convertToParamMap({ id }));
    await TestBed.configureTestingModule({
      imports: [DepartmentAppealDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    })
      .overrideComponent(DepartmentAppealDetailComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

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

  it('does not request anything when the route has no id', () => {
    params.next(convertToParamMap({}));
    const fixture = TestBed.createComponent(DepartmentAppealDetailComponent);
    fixture.detectChanges();
    http.expectNone(() => true);
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

  it('sends a result appeal decision as chosen and shows the decided appeal', () => {
    const fixture = open();
    decide(fixture, 'ACCEPT', 'Chứng từ đối soát hợp lệ');
    const request = http.expectOne(`${url}/decision`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ decision: 'ACCEPT', note: 'Chứng từ đối soát hợp lệ' });
    request.flush(
      ok({
        appeal: detail({ status: 'ACCEPTED', note: 'Chứng từ đối soát hợp lệ' }),
        decision: 'ACCEPT',
        submissionStatus: 'APPROVED',
        workStatus: 'VERIFIED',
        verificationId: 'v1',
        scoreEntryId: null,
      }),
    );
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.modal-window')).toBeNull();
    expect(root.querySelector('.alert-banner')?.textContent).toContain('9A8B7C6D');
    expect(root.querySelector<HTMLButtonElement>('.btn-primary')?.disabled).toBeTrue();
  });

  it('requires a decision and a note before calling the API', () => {
    const fixture = open();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('.btn-primary')?.click();
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('.modal-footer .btn-primary')?.click();
    fixture.detectChanges();
    http.expectNone(`${url}/decision`);
    expect(root.textContent).toContain('Ý kiến xử lý là bắt buộc');
  });

  it('sends rejecting a score appeal as keeping the score', () => {
    const fixture = open(
      detail({ type: 'SCORE', resultSubmissionId: null, workCode: null, periodCode: '2026-Q2' }),
    );
    decide(fixture, 'REJECT', 'Điểm đã đúng quy định');
    const request = http.expectOne(`${url}/decision`);
    expect(request.request.body).toEqual({ decision: 'UPHOLD', note: 'Điểm đã đúng quy định' });
    request.flush(
      ok({
        appeal: detail({ type: 'SCORE', status: 'REJECTED' }),
        decision: 'UPHOLD',
        submissionStatus: null,
        workStatus: null,
        verificationId: null,
        scoreEntryId: null,
      }),
    );
  });

  it('says a score decision is not supported yet instead of calling the API', () => {
    const fixture = open(detail({ type: 'SCORE', resultSubmissionId: null, workCode: null }));
    decide(fixture, 'ACCEPT', 'Gỡ sự kiện rủi ro');
    http.expectNone(`${url}/decision`);
    expect(fixture.nativeElement.querySelector('.modal-window').textContent).toContain(
      'chưa được hỗ trợ',
    );
  });

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
