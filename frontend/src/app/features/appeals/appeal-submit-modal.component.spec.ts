import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { AppealSubmitModalComponent } from './appeal-submit-modal.component';

const api = environment.apiUrl;
const ok = (data: unknown) => ({ success: true, status: 200, data });
const rejectedWork = { id: 'w1', code: 'WRK-1', title: 'Mở thẻ tín dụng' };
const submission = (over: Record<string, unknown> = {}) => ({
  id: 's2',
  workId: 'w1',
  submitterEmployeeId: 'me',
  submissionNo: 2,
  status: 'REJECTED',
  ...over,
});

describe('AppealSubmitModalComponent', () => {
  let http: HttpTestingController;

  const open = () => {
    const fixture = TestBed.createComponent(AppealSubmitModalComponent);
    fixture.detectChanges();
    return fixture;
  };
  const flushOptions = (works: unknown[], submissions: Record<string, unknown[]> = {}) => {
    http.expectOne(`${api}/employees/me`).flush(ok({ id: 'me' }));
    const list = http.expectOne((r) => r.url === `${api}/works`);
    expect(list.request.params.get('status')).toBe('Rejected');
    expect(list.request.params.get('assigneeEmployeeId')).toBe('me');
    list.flush(ok({ items: works, totalCount: works.length, page: 1, pageSize: 100 }));
    for (const work of works as { id: string }[]) {
      http.expectOne(`${api}/works/${work.id}/submissions`).flush(ok(submissions[work.id] ?? []));
    }
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppealSubmitModalComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('offers only the latest rejected submission of the caller for each rejected work', () => {
    const fixture = open();
    flushOptions([rejectedWork], {
      w1: [
        submission({ id: 's1', submissionNo: 1 }),
        submission(),
        submission({ id: 'other', submitterEmployeeId: 'someone', submissionNo: 3 }),
      ],
    });
    fixture.detectChanges();
    const options = (fixture.nativeElement as HTMLElement).querySelectorAll('option');
    expect(options.length).toBe(1);
    expect(options[0].textContent).toContain('WRK-1 · v2');
  });

  it('does not offer a work whose latest submission is no longer rejected', () => {
    const fixture = open();
    flushOptions([rejectedWork], { w1: [submission({ status: 'UNDER_REVIEW' })] });
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelectorAll('option').length).toBe(0);
    expect(root.textContent).toContain('Bạn không có bài nộp nào bị từ chối');
    expect(root.querySelector<HTMLButtonElement>('.button-primary')?.disabled).toBeTrue();
  });

  it('shows an error instead of options when the submissions cannot be loaded', () => {
    const fixture = open();
    http.expectOne(`${api}/employees/me`).flush(null, { status: 500, statusText: 'x' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Không thể tải',
    );
  });

  it('requires a submission and a reason before sending anything', () => {
    const fixture = open();
    flushOptions([rejectedWork], { w1: [submission()] });
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('.button-primary')?.click();
    fixture.detectChanges();
    http.expectNone((r) => r.url.endsWith('/appeals'));
    expect(root.querySelectorAll('.field-error').length).toBeGreaterThan(0);
  });

  it('files the appeal as multipart with the reason and the files, then reports success', () => {
    const fixture = open();
    flushOptions([rejectedWork], { w1: [submission()] });
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const select = root.querySelector<HTMLSelectElement>('select');
    select!.value = select!.options[0].value;
    select!.dispatchEvent(new Event('change'));
    const textarea = root.querySelector<HTMLTextAreaElement>('textarea');
    textarea!.value = '  Giao dịch do tôi xử lý  ';
    textarea!.dispatchEvent(new Event('input'));
    const input = root.querySelector<HTMLInputElement>('input[type="file"]');
    const transfer = new DataTransfer();
    transfer.items.add(new File(['a'], 'phieu.pdf', { type: 'application/pdf' }));
    input!.files = transfer.files;
    input!.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    let submitted = 0;
    fixture.componentInstance.submitted.subscribe(() => submitted++);
    root.querySelector<HTMLButtonElement>('.button-primary')?.click();
    const request = http.expectOne(`${api}/submissions/s2/appeals`);
    expect(request.request.method).toBe('POST');
    const form = request.request.body as FormData;
    expect(form.get('reason')).toBe('Giao dịch do tôi xử lý');
    expect((form.get('files') as File).name).toBe('phieu.pdf');
    request.flush(ok({ id: 'a1' }));
    expect(submitted).toBe(1);
  });

  it('shows the message of the error code and stays open when the API refuses', () => {
    const fixture = open();
    flushOptions([rejectedWork], { w1: [submission()] });
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const select = root.querySelector<HTMLSelectElement>('select');
    select!.value = select!.options[0].value;
    select!.dispatchEvent(new Event('change'));
    const textarea = root.querySelector<HTMLTextAreaElement>('textarea');
    textarea!.value = 'Lý do';
    textarea!.dispatchEvent(new Event('input'));
    let submitted = 0;
    fixture.componentInstance.submitted.subscribe(() => submitted++);

    root.querySelector<HTMLButtonElement>('.button-primary')?.click();
    http
      .expectOne(`${api}/submissions/s2/appeals`)
      .flush({ errorCode: 'APPEAL_WINDOW_EXPIRED' }, { status: 422, statusText: 'Unprocessable' });
    fixture.detectChanges();

    expect(root.querySelector('.modal-body .field-error')?.textContent).toContain('quá thời hạn');
    expect(submitted).toBe(0);
    expect(root.querySelector('.modal-dialog')).not.toBeNull();
  });

  it('can be dismissed', () => {
    const fixture = open();
    flushOptions([]);
    fixture.detectChanges();
    let dismissed = 0;
    fixture.componentInstance.dismissed.subscribe(() => dismissed++);
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.modal-close')
      ?.click();
    expect(dismissed).toBe(1);
  });
});
