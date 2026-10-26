import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { VerificationDossier } from '../../core/verification.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { ReviewVerifyComponent } from './review-verify.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const id = 'f104d588-41e2-45e0-8fa8-617066e4a2d8';
const base = `${environment.apiUrl}/verifications/${id}`;
const dossier: VerificationDossier = {
  submissionId: id,
  submissionCode: '#SUB-WRK-1-V1',
  submissionNo: 1,
  workId: 'w1',
  workCode: 'WRK-1',
  workTitle: 'Mở thẻ tín dụng',
  submitter: {
    employeeId: 'e1',
    employeeCode: 'NV004',
    fullName: 'Lê Minh Châu',
    jobPosition: 'Chuyên viên',
    department: 'Nhóm KD 1',
  },
  submittedAt: '2026-10-05T16:30:15+00:00',
  kpiItem: {
    kpiDefinitionId: 'k1',
    kpiCode: 'HDV',
    kpiName: 'Huy động vốn',
    declaredValue: 15,
    unit: 'thẻ',
  },
  narrative: 'Đã hoàn tất.',
  evidenceFiles: [],
  controlStandards: [{ id: 'STD-01', name: 'Khớp đối soát', passed: true }],
  status: 'SUBMITTED',
};

type Internals = {
  selectDecision(decision: string): void;
  setVerifiedValue(value: string): void;
  feedback: { set(value: string): void };
  deadline: { set(value: string): void };
  openConfirm(): void;
  confirm(): void;
  confirmOpen(): boolean;
};

describe('ReviewVerifyComponent', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  const open = (withId = true) => {
    if (withId) params.next(convertToParamMap({ submissionId: id }));
    const fixture = TestBed.createComponent(ReviewVerifyComponent);
    fixture.detectChanges();
    return fixture;
  };
  const load = (fixture: ReturnType<typeof open>, data: VerificationDossier = dossier) => {
    http.expectOne(`${base}/dossier`).flush({ success: true, status: 200, data });
    http.expectOne(`${base}/history`).flush({ success: true, status: 200, data: [] });
    fixture.detectChanges();
  };
  const internals = (fixture: ReturnType<typeof open>) =>
    fixture.componentInstance as unknown as Internals;

  beforeEach(async () => {
    params = new BehaviorSubject(convertToParamMap({}));
    await TestBed.configureTestingModule({
      imports: [ReviewVerifyComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    })
      .overrideComponent(ReviewVerifyComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('does not request or invent a dossier when no submission id is given', () => {
    const fixture = open(false);
    http.expectNone(() => true);
    expect(fixture.nativeElement.textContent).toContain('Chưa chọn hồ sơ');
  });

  it('loads the dossier and history from the real endpoints', () => {
    const fixture = open();
    load(fixture);
    const root: HTMLElement = fixture.nativeElement;
    expect(root.textContent).toContain('Mở thẻ tín dụng');
    expect(root.textContent).toContain('Lê Minh Châu');
    expect(root.textContent).not.toContain('Nguyễn Tiến Thành');
  });

  it('shows an error with retry instead of a fallback dossier', () => {
    const fixture = open();
    http.expectOne(`${base}/dossier`).flush(null, { status: 500, statusText: 'Server Error' });
    // forkJoin cancels the sibling history request when the dossier fails.
    expect(http.match(`${base}/history`).length).toBeLessThanOrEqual(1);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Không thể tải');
    expect(root.textContent).not.toContain('Mở thẻ tín dụng');
    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    load(fixture);
    expect(root.textContent).toContain('Mở thẻ tín dụng');
  });

  it('shows a not-found state for 404', () => {
    const fixture = open();
    http
      .expectOne(`${base}/dossier`)
      .flush(
        { success: false, status: 404, errorCode: 'NOT_FOUND' },
        { status: 404, statusText: 'Not Found' },
      );
    http.match(`${base}/history`);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Không tìm thấy hồ sơ');
  });

  it('validates feedback and deadline before posting, then posts the documented payload', () => {
    const fixture = open();
    load(fixture);
    const cmp = internals(fixture);
    cmp.selectDecision('REVISION_REQUIRED');
    cmp.feedback.set('ngắn');
    cmp.openConfirm();
    expect(cmp.confirmOpen()).toBeFalse();
    cmp.feedback.set('Thiếu chữ ký trang 3 của hồ sơ.');
    cmp.deadline.set('2000-01-01T10:00');
    cmp.openConfirm();
    expect(cmp.confirmOpen()).toBeFalse();
    cmp.deadline.set('2999-01-01T10:00');
    cmp.openConfirm();
    expect(cmp.confirmOpen()).toBeTrue();

    cmp.confirm();
    const post = http.expectOne(`${base}/decision`);
    expect(post.request.method).toBe('POST');
    expect(post.request.body.decision).toBe('REVISION_REQUIRED');
    expect(post.request.body.verifiedValue).toBeNull();
    expect(post.request.body.feedback).toBe('Thiếu chữ ký trang 3 của hồ sơ.');
    expect(post.request.body.resubmitDeadline).toMatch(/^2999-01-01T.*Z$/);
    expect(post.request.body.checklistResults).toEqual([{ standardId: 'STD-01', isPassed: true }]);
    post.flush({
      success: true,
      status: 200,
      data: {
        verificationId: 'v1',
        decision: 'REVISION_REQUIRED',
        submissionStatus: 'UNDER_REVIEW',
        workStatus: 'IN_PROGRESS',
        verifiedAt: '2026-10-06T09:15:00+00:00',
        message: 'Đã ghi nhận.',
      },
    });
    load(fixture, { ...dossier, status: 'UNDER_REVIEW' });
    expect(fixture.nativeElement.querySelector('.notice')?.textContent).toContain('Đã ghi nhận.');
  });

  it('maps the maker-checker error code to a localized message', () => {
    const fixture = open();
    load(fixture);
    const cmp = internals(fixture);
    cmp.selectDecision('APPROVED');
    cmp.setVerifiedValue('15');
    cmp.openConfirm();
    cmp.confirm();
    http
      .expectOne(`${base}/decision`)
      .flush(
        { success: false, status: 422, errorCode: 'MAKER_CANNOT_BE_CHECKER', message: 'x' },
        { status: 422, statusText: 'Unprocessable' },
      );
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.notice.is-error')?.textContent).toContain(
      'tự thẩm duyệt',
    );
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.notice.is-error')?.textContent).toContain(
      'cannot verify their own',
    );
  });

  it('hides the decision form for a submission that can no longer be decided', () => {
    const fixture = open();
    load(fixture, { ...dossier, status: 'APPROVED' });
    expect(fixture.nativeElement.querySelector('form.decision-form')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('không ở trạng thái chờ thẩm định');
  });
});
