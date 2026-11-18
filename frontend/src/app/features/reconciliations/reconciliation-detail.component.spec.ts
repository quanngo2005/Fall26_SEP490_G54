import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, input, output, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthenticatedUser } from '../../core/auth.models';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ReconciliationDetail } from '../../core/reconciliation.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { RiskEventCreateModalComponent } from '../risk-events/risk-event-create-modal.component';
import { ReconciliationDetailComponent } from './reconciliation-detail.component';
import { ReconciliationFlagModalComponent } from './reconciliation-flag-modal.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}
@Component({ selector: 'app-reconciliation-flag-modal', template: '<p class="flag-stub"></p>' })
class FlagModalStub {
  readonly submissionId = input<string>();
  readonly flagged = output<void>();
  readonly dismissed = output<void>();
}
@Component({ selector: 'app-risk-event-create-modal', template: '<p class="risk-stub"></p>' })
class RiskModalStub {
  readonly preset = input<unknown>();
  readonly created = output<{ code: string; uploadFailed: boolean }>();
  readonly dismissed = output<void>();
}

const api = environment.apiUrl;
const envelope = <T>(data: T) => ({ success: true, status: 200, data });

const detail = (over: Partial<ReconciliationDetail> = {}): ReconciliationDetail => ({
  item: {
    id: 'r1',
    transactionCode: 'TXN-77001',
    workId: 'w1',
    workCode: 'WK-1018',
    workTitle: 'Gửi tiết kiệm',
    employeeId: 'e1',
    employeeCode: 'NV-0412',
    employeeName: 'Nguyễn Văn A',
    jobTitle: 'Giao dịch viên',
    departmentId: 'd1',
    departmentName: 'PGD 1',
    customerCif: '0045678',
    referEmployeeCode: null,
    productCode: 'THE_TIN_DUNG',
    amount: 95_000_000,
    transactionDate: '2026-10-20',
    status: 'NOT_MATCHED',
    reason: 'PRODUCT_MISMATCH',
    submittedAt: '2026-10-20T02:12:40Z',
    updatedAt: '2026-10-20T11:02:00Z',
  },
  work: {
    id: 'w1',
    code: 'WK-1018',
    title: 'Gửi tiết kiệm',
    status: 'SUBMITTED',
    workTypeName: 'Tiền gửi',
    productCode: 'TIEN_GUI_CO_KY_HAN',
    customerName: null,
    customerCif: '0045678',
  },
  submission: {
    id: 's1',
    submissionNo: 2,
    status: 'SUBMITTED',
    submittedAt: '2026-10-20T02:12:40Z',
    submitterName: 'Nguyễn Văn A',
  },
  import: {
    batchId: 'b1',
    fileName: 'IMP-2026-10-20-01.xlsx',
    importedAt: '2026-10-20T11:00:00Z',
    importedByName: 'Quản trị hệ thống',
    transactionDate: '2026-10-20',
    amount: 95_000_000,
  },
  fields: [
    { field: 'TRANSACTION_CODE', declared: 'TXN-77001', imported: 'TXN-77001', matched: true },
    { field: 'CUSTOMER', declared: '0045678', imported: '0045678', matched: true },
    { field: 'EMPLOYEE', declared: 'NV-0412', imported: 'NV-0412', matched: true },
    {
      field: 'PRODUCT',
      declared: 'TIEN_GUI_CO_KY_HAN',
      imported: 'THE_TIN_DUNG',
      matched: false,
    },
    { field: 'REFERRAL', declared: null, imported: null, matched: null },
    { field: 'AMOUNT', declared: null, imported: '95000000', matched: null },
  ],
  comparedFields: 4,
  matchedFields: 3,
  siblings: [
    { id: 'r1', transactionCode: 'TXN-77001', status: 'NOT_MATCHED', reason: 'PRODUCT_MISMATCH' },
    { id: 'r2', transactionCode: 'TXN-77002', status: 'MATCHED', reason: null },
  ],
  history: [
    {
      kind: 'RECONCILED',
      at: '2026-10-20T11:02:00Z',
      status: 'NOT_MATCHED',
      reason: 'PRODUCT_MISMATCH',
      actor: null,
      reference: null,
    },
    {
      kind: 'IMPORTED',
      at: '2026-10-20T11:00:00Z',
      status: null,
      reason: null,
      actor: 'Quản trị hệ thống',
      reference: 'IMP-2026-10-20-01.xlsx',
    },
    {
      kind: 'CODE_ADDED',
      at: '2026-10-20T02:12:40Z',
      status: null,
      reason: null,
      actor: 'Nguyễn Văn A',
      reference: 'TXN-77001',
    },
  ],
  appeal: {
    state: 'NOT_APPLICABLE',
    deadline: null,
    appealId: null,
    status: null,
    submissionId: 's1',
  },
  actions: {
    canFlag: true,
    flagSubmissionId: 's1',
    flagBlockedReason: null,
    canRecordRisk: true,
  },
  ...over,
});

describe('ReconciliationDetailComponent', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let navigate: jasmine.Spy;
  const user = signal<AuthenticatedUser | null>(null);

  const open = (body: ReconciliationDetail | null = detail()) => {
    const fixture = TestBed.createComponent(ReconciliationDetailComponent);
    fixture.detectChanges();
    const request = http.expectOne(`${api}/reconciliations/r1`);
    if (body) request.flush(envelope(body));
    else request.flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    return fixture;
  };
  const text = (fixture: { nativeElement: HTMLElement }) =>
    (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
  const buttons = (fixture: { nativeElement: HTMLElement }) =>
    Array.from(fixture.nativeElement.querySelectorAll<HTMLButtonElement>('.detail-actions button'));

  beforeEach(async () => {
    params = new BehaviorSubject(convertToParamMap({ id: 'r1' }));
    user.set({
      id: 'u1',
      email: 'qa@test.local',
      fullName: 'QA',
      roles: [],
      permissions: ['RECONCILIATION_READ'],
    });
    await TestBed.configureTestingModule({
      imports: [ReconciliationDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } },
        { provide: AuthService, useValue: { currentUser: user.asReadonly() } },
      ],
    })
      .overrideComponent(ReconciliationDetailComponent, {
        remove: {
          imports: [
            HeaderComponent,
            SidebarComponent,
            ReconciliationFlagModalComponent,
            RiskEventCreateModalComponent,
          ],
        },
        add: { imports: [HeaderStub, SidebarStub, FlagModalStub, RiskModalStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
  });

  afterEach(() => http.verify());

  it('compares every field with the imported file and marks the one that differs', () => {
    const fixture = open();
    const body = text(fixture);

    expect(body).toContain('Đối soát WK-1018 · TXN-77001');
    expect(body).toContain('Không khớp');
    expect(body).toContain('3/4 trường khớp');
    expect(body).toContain('IMP-2026-10-20-01.xlsx');
    expect(body).toContain('bởi Quản trị hệ thống');
    expect(body).toContain('TIEN_GUI_CO_KY_HAN');
    expect(body).toContain('THE_TIN_DUNG');
    expect(fixture.nativeElement.querySelectorAll('.kind-mismatch').length).toBe(1);
    expect(fixture.nativeElement.querySelectorAll('.kind-match').length).toBe(3);
    // The amount is never declared by the employee: it is shown as taken from the file, never as a "match".
    expect(body).toContain('Lấy theo file giao dịch');
    expect(body).toContain('95.000.000 VND');
    expect(body).toContain('Không có bàn giao');
    expect(body).toContain('Để trống');
    expect(body).not.toContain('Refer ID');
  });

  it('says plainly that the code is not in any imported file yet', () => {
    const fixture = open(
      detail({
        import: null,
        comparedFields: 0,
        matchedFields: 0,
        fields: [
          { field: 'TRANSACTION_CODE', declared: 'TXN-77001', imported: null, matched: null },
          { field: 'AMOUNT', declared: null, imported: null, matched: null },
        ],
      }),
    );
    const body = text(fixture);

    expect(body).toContain('Chưa có trong file giao dịch nào');
    expect(body).toContain('hệ thống sẽ tự đối soát lại');
    expect(body).toContain('Chờ file');
    expect(body).toContain('Chưa có trong file');
    expect(body).not.toContain('trường khớp');
  });

  it('lets QA flag quality and record a risk event, filling the risk form in', () => {
    const fixture = open();

    const [flag, risk] = buttons(fixture);
    expect(flag.disabled).toBeFalse();
    flag.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.flag-stub')).not.toBeNull();

    risk.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.risk-stub')).not.toBeNull();
  });

  it('explains why a flag is not possible instead of hiding the button', () => {
    const fixture = open(
      detail({
        actions: {
          canFlag: false,
          flagSubmissionId: null,
          flagBlockedReason: 'NO_APPROVED_SUBMISSION',
          canRecordRisk: true,
        },
      }),
    );

    const [flag] = buttons(fixture);
    expect(flag.disabled).toBeTrue();
    expect(flag.title).toContain('Chỉ gắn cờ được kết quả đã duyệt');
    expect(text(fixture)).toContain('Việc QA có thể làm');
  });

  it('shows a manager a view-only screen', () => {
    const fixture = open(
      detail({
        actions: {
          canFlag: false,
          flagSubmissionId: null,
          flagBlockedReason: 'NO_PERMISSION',
          canRecordRisk: false,
        },
      }),
    );

    expect(buttons(fixture).length).toBe(0);
    expect(text(fixture)).toContain('Chỉ xem, không có nút duyệt');
    expect(text(fixture)).not.toContain('Việc QA có thể làm');
  });

  it('explains the appeal of a rejected submission with the hours left', () => {
    const deadline = new Date(Date.now() + 30 * 3_600_000).toISOString();
    const fixture = open(
      detail({
        appeal: { state: 'AVAILABLE', deadline, appealId: null, status: null, submissionId: 's1' },
      }),
    );

    expect(text(fixture)).toContain('Còn hạn');
    expect(text(fixture)).toMatch(/còn 30 giờ/);
  });

  it('links an appeal in progress only for people who can decide on it', () => {
    const inProgress = detail({
      appeal: {
        state: 'IN_PROGRESS',
        deadline: null,
        appealId: 'a1',
        status: 'OPEN',
        submissionId: 's1',
      },
    });
    const without = open(inProgress);
    expect(without.nativeElement.querySelector('.side-link')).toBeNull();
    expect(text(without)).toContain('Đang xử lý');
  });

  it('reads the history in plain language', () => {
    const fixture = open();
    const body = text(fixture);

    expect(body).toContain('Nguyễn Văn A gửi mã giao dịch');
    expect(body).toContain('Quản trị hệ thống nhập file giao dịch IMP-2026-10-20-01.xlsx');
    expect(body).toContain('Hệ thống đối soát: không khớp (Sai sản phẩm)');
  });

  it('moves to another code of the same work', () => {
    const fixture = open();

    (fixture.nativeElement.querySelectorAll('.sibling-chip')[1] as HTMLElement).click();

    expect(navigate).toHaveBeenCalledWith(['/reconciliations', 'r2']);
  });

  it('says so when the code is missing or outside the callers scope', () => {
    const fixture = open(null);

    expect(text(fixture)).toContain('Không tìm thấy mã giao dịch này');
    expect(text(fixture)).toContain('Quay lại danh sách');
  });
});
