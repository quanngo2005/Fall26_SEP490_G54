import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { MySubmissionDetail, MySubmissionListItem } from '../../core/my-submission.models';
import { MySubmissionDrawerComponent } from './my-submission-drawer.component';

const listItem: MySubmissionListItem = {
  submissionId: 's1',
  submissionCode: '#SUB-WK-1-V2',
  submissionNo: 2,
  workId: 'w1',
  workCode: 'WK-1',
  workTitle: 'Mở tiền gửi',
  workTypeName: 'Tiền gửi',
  submittedAt: '2026-10-20T09:12:40+00:00',
  status: 'REJECTED',
  matchStatus: 'NONE',
  transactionCount: 0,
  matchedTransactionCount: 0,
  firstTransactionCode: null,
  appealDeadline: null,
};

const detail = (over: Partial<MySubmissionDetail> = {}): MySubmissionDetail => ({
  submissionId: 's1',
  submissionCode: '#SUB-WK-1-V2',
  submissionNo: 2,
  workId: 'w1',
  workCode: 'WK-1',
  workTitle: 'Mở tiền gửi',
  workTypeName: null,
  submittedAt: '2026-10-20T09:12:40+00:00',
  status: 'REJECTED',
  matchStatus: 'NONE',
  evidences: [
    {
      id: 'e1',
      fileName: 'hop-dong.pdf',
      filePath: 'x',
      mimeType: 'application/pdf',
      fileSize: 2048,
      uploadedAt: '2026-10-20T09:12:40+00:00',
    },
  ],
  transactions: [],
  versions: [
    {
      submissionId: 's1',
      submissionNo: 2,
      submittedAt: '2026-10-20T09:12:40+00:00',
      status: 'REJECTED',
      matchStatus: 'NONE',
    },
  ],
  appeal: { state: 'AVAILABLE', deadline: '2099-01-01T00:00:00Z', appealId: null },
  ...over,
});

describe('MySubmissionDrawerComponent', () => {
  let fixture: ComponentFixture<MySubmissionDrawerComponent>;
  let http: HttpTestingController;

  const open = (
    body: MySubmissionDetail | null,
    state: { loading?: boolean; failed?: boolean } = {},
  ) => {
    fixture = TestBed.createComponent(MySubmissionDrawerComponent);
    fixture.componentRef.setInput('item', listItem);
    fixture.componentRef.setInput('detail', body);
    fixture.componentRef.setInput('loading', state.loading ?? false);
    fixture.componentRef.setInput('failed', state.failed ?? false);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MySubmissionDrawerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('shows the list row data while the detail loads, and the loading text', () => {
    const root = open(null, { loading: true });
    expect(root.querySelector('h2')?.textContent).toContain('WK-1 · Phiên bản 2');
    expect(root.textContent).toContain('Đang tải chi tiết…');
    expect(root.querySelector('.drawer-footer button')).toBeNull();
  });

  it('shows an error text when the detail failed', () => {
    const root = open(null, { failed: true });
    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'Chưa tải được chi tiết bài nộp. Bạn thử lại.',
    );
  });

  it('emits the submission id when the appeal button is pressed', () => {
    const root = open(detail());
    const emitted: string[] = [];
    fixture.componentInstance.appeal.subscribe((id) => emitted.push(id));
    root.querySelector<HTMLButtonElement>('.drawer-footer button')?.click();
    expect(emitted).toEqual(['s1']);
  });

  it('emits the id of the clicked version', () => {
    const root = open(detail());
    const emitted: string[] = [];
    fixture.componentInstance.selectVersion.subscribe((id) => emitted.push(id));
    root.querySelector<HTMLButtonElement>('.version-row')?.click();
    expect(emitted).toEqual(['s1']);
  });

  it('maps the arrow keys and Escape to outputs', () => {
    open(detail());
    const seen: string[] = [];
    fixture.componentInstance.selectNext.subscribe(() => seen.push('next'));
    fixture.componentInstance.selectPrev.subscribe(() => seen.push('prev'));
    fixture.componentInstance.closeDrawer.subscribe(() => seen.push('close'));
    for (const key of ['ArrowDown', 'ArrowUp', 'Escape']) {
      window.dispatchEvent(new KeyboardEvent('keydown', { key }));
    }
    expect(seen).toEqual(['next', 'prev', 'close']);
  });

  it('shows the work type fallback and the file size of an evidence', () => {
    const root = open(detail());
    expect(root.textContent).toContain('Ngoài loại việc');
    expect(root.textContent).toContain('hop-dong.pdf');
    expect(root.textContent).toContain('2');
  });

  it('opens an evidence through the API and reports a failed preview', () => {
    const root = open(detail());
    root.querySelector<HTMLButtonElement>('.preview-btn')?.click();
    http.expectOne(`${environment.apiUrl}/evidence/e1/preview`).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(root.textContent).toContain('Chưa mở được tệp minh chứng.');
  });

  it('shows no appeal action for a submission that cannot be appealed', () => {
    const root = open(
      detail({
        status: 'APPROVED',
        appeal: { state: 'NOT_APPLICABLE', deadline: null, appealId: null },
      }),
    );
    expect(root.querySelector('.drawer-footer button')).toBeNull();
    expect(root.querySelector('.drawer-footer a')).toBeNull();
  });
});
