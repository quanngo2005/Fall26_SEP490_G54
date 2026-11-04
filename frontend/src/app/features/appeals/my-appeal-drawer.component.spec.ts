import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AppealDetail, AppealListItem } from '../../core/appeal.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { MyAppealDrawerComponent } from './my-appeal-drawer.component';

const listItem = (over: Partial<AppealListItem> = {}): AppealListItem => ({
  id: '3f2a9c10-1111-2222-3333-444455556666',
  resultSubmissionId: 's1',
  workId: 'w1',
  workCode: 'WRK-1',
  workTitle: 'Mở thẻ',
  submissionNo: 1,
  employeeId: 'e1',
  employeeCode: 'NV001',
  employeeName: 'Nhân viên',
  status: 'OPEN',
  filedAt: '2026-10-24T10:00:00+00:00',
  decidedAt: null,
  updatedAt: '2026-10-24T10:00:00+00:00',
  type: 'RESULT',
  performanceEvaluationId: null,
  periodCode: null,
  jobPositionTitle: null,
  resolvedByEmployeeName: null,
  note: null,
  ...over,
});
const detail = (over: Partial<AppealDetail> = {}): AppealDetail => ({
  ...listItem(),
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

@Component({
  imports: [MyAppealDrawerComponent],
  template: `<app-my-appeal-drawer
  [appeal]="appeal"
  [detail]="detailValue"
  [loading]="loading"
  [failed]="failed"
  (closeDrawer)="closed = closed + 1"
  (withdraw)="withdrawn = $event"
  (selectNext)="next = next + 1"
  (selectPrev)="prev = prev + 1"
/>`,
})
class HostComponent {
  appeal = listItem();
  detailValue: AppealDetail | null = detail();
  loading = false;
  failed = false;
  closed = 0;
  next = 0;
  prev = 0;
  withdrawn: AppealListItem | null = null;
}

describe('MyAppealDrawerComponent', () => {
  let http: HttpTestingController;

  const open = (setup: (host: HostComponent) => void = () => undefined) => {
    const fixture = TestBed.createComponent(HostComponent);
    setup(fixture.componentInstance);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('shows the code, the status, the reason and the evidence of the appeal', () => {
    const root: HTMLElement = open().nativeElement;
    expect(root.querySelector('.appeal-title')?.textContent).toContain('3F2A9C10');
    expect(root.querySelector('.status-badge')?.textContent).toContain('Đang mở');
    expect(root.textContent).toContain('Giao dịch do tôi xử lý');
    expect(root.textContent).toContain('phieu.pdf');
    expect(root.textContent).toContain('WRK-1 · v1');
  });

  it('shows N/A instead of inventing a reason while the detail is missing', () => {
    const root: HTMLElement = open((host) => (host.detailValue = null)).nativeElement;
    expect(root.querySelector('.reason-card')?.textContent).toContain('N/A');
  });

  it('shows loading and error states instead of the sections', () => {
    let root: HTMLElement = open((host) => (host.loading = true)).nativeElement;
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Đang tải');
    expect(root.querySelector('.reason-card')).toBeNull();
    root = open((host) => (host.failed = true)).nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Không thể tải');
  });

  it('names the period of a score appeal and omits the work', () => {
    const root: HTMLElement = open((host) => {
      host.appeal = listItem({ type: 'SCORE', periodCode: '2026-Q2', workCode: null });
    }).nativeElement;
    expect(root.textContent).toContain('2026-Q2');
    expect(root.textContent).toContain('Kỳ đánh giá');
    expect(root.textContent).not.toContain('Mở thẻ');
  });

  it('closes on Escape and moves between rows with the arrow keys', () => {
    const fixture = open();
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(fixture.componentInstance.next).toBe(1);
    expect(fixture.componentInstance.prev).toBe(1);
    expect(fixture.componentInstance.closed).toBe(1);
  });

  it('withdraws only after the user confirms, and only while the appeal is open', () => {
    const fixture = open();
    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.withdraw-button',
    );
    const confirm = spyOn(window, 'confirm').and.returnValues(false, true);
    button?.click();
    expect(fixture.componentInstance.withdrawn).toBeNull();
    button?.click();
    expect(fixture.componentInstance.withdrawn?.id).toBe(listItem().id);

    const decided: HTMLElement = open((host) => {
      host.appeal = listItem({ status: 'ACCEPTED' });
    }).nativeElement;
    expect(decided.querySelector<HTMLButtonElement>('.withdraw-button')?.disabled).toBeTrue();
    expect(confirm).toHaveBeenCalledTimes(2);
  });

  it('shows when a decided appeal was decided', () => {
    const root: HTMLElement = open((host) => {
      host.appeal = listItem({
        status: 'REJECTED',
        decidedAt: '2026-10-25T09:00:00+00:00',
        resolvedByEmployeeName: 'Trưởng phòng',
      });
    }).nativeElement;
    expect(root.querySelector('.drawer-subtitle')?.textContent).toContain('Trưởng phòng');
    expect(root.textContent).toContain('Đã xử lý');
  });

  it('opens an evidence file fetched as a blob and reports a failure', () => {
    const fixture = open();
    const openWindow = spyOn(window, 'open');
    spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('.preview-btn')?.click();
    const ok = http.expectOne(`${environment.apiUrl}/evidence/ev1/preview`);
    expect(ok.request.responseType).toBe('blob');
    ok.flush(new Blob(['a']));
    expect(openWindow).toHaveBeenCalledWith('blob:x', '_blank', 'noopener,noreferrer');

    root.querySelector<HTMLButtonElement>('.preview-btn')?.click();
    http
      .expectOne(`${environment.apiUrl}/evidence/ev1/preview`)
      .flush(null, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(root.textContent).toContain('Không thể mở tệp bằng chứng');
  });

  it('switches language live', () => {
    const fixture = open();
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.status-badge')?.textContent).toContain('Open');
    expect(root.querySelector('.withdraw-button')?.textContent).toContain('Withdraw');
  });
});
