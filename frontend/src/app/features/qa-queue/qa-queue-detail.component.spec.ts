import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { QaQueueDetail, QaQueueItem } from '../../core/qa-queue.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { QaQueueContext } from './qa-queue-display';
import { QaQueueDetailComponent } from './qa-queue-detail.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const api = environment.apiUrl;
const envelope = <T>(data: T) => ({ success: true, status: 200, data });
const item = (over: Partial<QaQueueItem> = {}): QaQueueItem => ({
  kind: 'FLAG',
  type: 'FLAG',
  id: 'f1',
  code: 'QA-0331',
  source: 'MANUAL_FLAG',
  resultSubmissionId: 's1',
  submissionNo: 2,
  workId: 'w1',
  workCode: 'WK-1051',
  workTitle: 'Mở thẻ ghi nợ',
  employeeId: 'e1',
  employeeCode: 'NV0244',
  employeeName: 'Lê Thu Hoa',
  departmentName: 'Phòng Bán lẻ',
  tab: 'PENDING',
  status: 'OPEN',
  severity: 'HIGH',
  createdAt: '2026-10-23T03:00:00Z',
  dueAt: '2026-10-28T03:00:00Z',
  ...over,
});
const detail = (
  over: Partial<QaQueueDetail> = {},
  itemOver: Partial<QaQueueItem> = {},
): QaQueueDetail => ({
  item: item(itemOver),
  reason: 'Số liệu không khớp chứng từ gốc.',
  reworkComment: null,
  reviewedAt: null,
  reviewedByName: null,
  submissionComment: 'Đã phát hành đủ thẻ.',
  submittedAt: '2026-10-20T07:00:00Z',
  jobPositionTitle: 'Teller',
  evidences: [
    { id: 'ev1', fileName: 'phieu_mo_the.pdf', fileSize: 2048, mimeType: 'application/pdf' },
  ],
  transactions: [
    {
      transactionCode: 'TX-1',
      status: 'MATCHED',
      reason: null,
      importBatchFileName: 'IMP-1024.xlsx',
      productCode: 'THE',
      amount: 5,
      transactionDate: '2026-10-20',
    },
  ],
  ...over,
});

describe('QaQueueDetailComponent', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let navigate: jasmine.Spy;

  const open = (kind = 'FLAG', id = 'f1', body: QaQueueDetail | null = detail()) => {
    params.next(convertToParamMap({ kind, id }));
    const fixture = TestBed.createComponent(QaQueueDetailComponent);
    fixture.detectChanges();
    if (body) {
      http.expectOne(`${api}/qa-queue/${kind.toUpperCase()}/${id}`).flush(envelope(body));
      fixture.detectChanges();
    }
    return fixture;
  };
  const text = (fixture: { nativeElement: HTMLElement }) =>
    (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
  const click = (fixture: { nativeElement: HTMLElement }, selector: string, index = 0) =>
    (fixture.nativeElement.querySelectorAll(selector)[index] as HTMLElement).click();
  const type = (fixture: { nativeElement: HTMLElement }, value: string) => {
    const box = fixture.nativeElement.querySelector('#qaComment') as HTMLTextAreaElement;
    box.value = value;
    box.dispatchEvent(new Event('input'));
  };

  beforeEach(async () => {
    params = new BehaviorSubject(convertToParamMap({}));
    await TestBed.configureTestingModule({
      imports: [QaQueueDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } },
      ],
    })
      .overrideComponent(QaQueueDetailComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
  });

  afterEach(() => http.verify());

  it('shows the submission, the evidence, the reconciliation and the reason of the item', () => {
    const fixture = open();
    const body = text(fixture);

    expect(body).toContain('QA-0331');
    expect(body).toContain('WK-1051');
    expect(body).toContain('Cờ chất lượng');
    expect(body).toContain('phieu_mo_the.pdf');
    expect(body).toContain('v2 · 20/10/2026');
    expect(body).toContain('NV0244 · Lê Thu Hoa · Teller');
    expect(body).toContain('Mở thẻ ghi nợ');
    expect(body).toContain('TX-1');
    expect(body).toContain('Matched');
    expect(body).toContain('IMP-1024.xlsx');
    expect(body).toContain('Số liệu không khớp chứng từ gốc.');
    expect(fixture.nativeElement.querySelector('#qaComment')).not.toBeNull();
  });

  it('says so when the work has no transaction codes or evidence', () => {
    const fixture = open('FLAG', 'f1', detail({ transactions: [], evidences: [] }));

    expect(text(fixture)).toContain('chưa có mã giao dịch để đối soát');
    expect(text(fixture)).toContain('Không có tệp đính kèm.');
  });

  it('opens an evidence file through the authenticated preview', () => {
    const fixture = open();
    const win = spyOn(window, 'open');
    click(fixture, '.preview-btn');

    http.expectOne(`${api}/evidence/ev1/preview`).flush(new Blob(['x']));
    expect(win).toHaveBeenCalled();

    click(fixture, '.preview-btn');
    http
      .expectOne(`${api}/evidence/ev1/preview`)
      .error(new ProgressEvent('error'), { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(text(fixture)).toContain('Không thể mở tệp bằng chứng.');
  });

  it('dismisses a flag when QA confirms the result is correct, then reloads it', () => {
    const fixture = open();

    click(fixture, '.btn-primary');
    http.expectOne(`${api}/quality-flags/f1/dismiss`).flush(envelope({}));
    http
      .expectOne(`${api}/qa-queue/FLAG/f1`)
      .flush(envelope(detail({}, { tab: 'CONFIRMED', status: 'DISMISSED' })));
    fixture.detectChanges();

    expect(text(fixture)).toContain('Đã ghi nhận quyết định cho QA-0331.');
    expect(text(fixture)).toContain('Mục này đã được xử lý: Đã xác nhận.');
    expect(fixture.nativeElement.querySelector('#qaComment')).toBeNull();
  });

  it('refuses to reject a flag without a reason of at least ten characters', () => {
    const fixture = open();
    click(fixture, '.verdict-btn', 1);
    fixture.detectChanges();
    type(fixture, 'ngắn');
    click(fixture, '.btn-primary');
    fixture.detectChanges();

    http.expectNone(`${api}/quality-flags/f1/request-rework`);
    expect(text(fixture)).toContain('tối thiểu 10 ký tự');
  });

  it('sends the work back when QA rejects a flag with a reason', () => {
    const fixture = open();
    click(fixture, '.verdict-btn', 1);
    fixture.detectChanges();
    expect(text(fixture)).toContain('điểm KPI của công việc này được trừ lại');
    type(fixture, 'Làm lại báo cáo và đính kèm chứng từ.');
    click(fixture, '.btn-primary');

    const request = http.expectOne(`${api}/quality-flags/f1/request-rework`);
    expect(request.request.body).toEqual({ comment: 'Làm lại báo cáo và đính kèm chứng từ.' });
    request.flush(envelope({}));
    http
      .expectOne(`${api}/qa-queue/FLAG/f1`)
      .flush(
        envelope(
          detail(
            { reworkComment: 'Làm lại báo cáo và đính kèm chứng từ.' },
            { tab: 'REJECTED', status: 'REWORK_REQUESTED' },
          ),
        ),
      );
    fixture.detectChanges();
    expect(text(fixture)).toContain('Yêu cầu làm lại');
  });

  it('passes a 5% sample without a reason', () => {
    const fixture = open(
      'SAMPLE',
      's1',
      detail(
        { reason: null },
        { kind: 'SAMPLE', type: 'SAMPLE', id: 's1', status: 'PENDING', source: 'RANDOM_SAMPLE' },
      ),
    );

    click(fixture, '.btn-primary');
    const request = http.expectOne(`${api}/qa-samples/s1/decision`);
    expect(request.request.body).toEqual({ result: 'PASSED', comment: null, requestRework: false });
    request.flush(envelope({}));
    http
      .expectOne(`${api}/qa-queue/SAMPLE/s1`)
      .flush(
        envelope(
          detail(
            { reason: null },
            { kind: 'SAMPLE', type: 'SAMPLE', id: 's1', tab: 'CONFIRMED', status: 'PASSED' },
          ),
        ),
      );
  });

  it('fails a 5% sample with a severity and the rework choice, then goes back to the queue', () => {
    const fixture = open(
      'SAMPLE',
      's1',
      detail(
        { reason: null },
        { kind: 'SAMPLE', type: 'SAMPLE', id: 's1', status: 'PENDING', source: 'RANDOM_SAMPLE' },
      ),
    );
    click(fixture, '.verdict-btn', 1);
    fixture.detectChanges();
    click(fixture, '.severity-btn', 2);
    type(fixture, 'Chứng từ không hợp lệ, cần làm lại.');
    click(fixture, '.switch');
    fixture.detectChanges();
    expect(text(fixture)).toContain('tạo một cờ chất lượng chờ xử lý');

    click(fixture, '.btn-primary');
    const request = http.expectOne(`${api}/qa-samples/s1/decision`);
    expect(request.request.body).toEqual({
      result: 'FAILED',
      comment: 'Chứng từ không hợp lệ, cần làm lại.',
      severity: 'HIGH',
      requestRework: false,
    });
    request.flush(envelope({}));

    expect(navigate).toHaveBeenCalledWith(['/qa-queue']);
  });

  it('shows what went wrong when the decision is refused', () => {
    const fixture = open();
    click(fixture, '.verdict-btn', 1);
    fixture.detectChanges();
    type(fixture, 'Làm lại báo cáo và đính kèm chứng từ.');
    click(fixture, '.btn-primary');
    http
      .expectOne(`${api}/quality-flags/f1/request-rework`)
      .flush({ errorCode: 'PERIOD_LOCKED' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(text(fixture)).toContain('Kỳ đánh giá đã khóa');
    expect(fixture.nativeElement.querySelector('#qaComment')).not.toBeNull();
  });

  it('shows a handled item read-only with who handled it', () => {
    const fixture = open(
      'SAMPLE',
      's1',
      detail(
        {
          reviewedAt: '2026-10-24T03:00:00Z',
          reviewedByName: 'Lê Văn Kiểm',
          reason: 'Đạt yêu cầu.',
        },
        { kind: 'SAMPLE', type: 'SAMPLE', id: 's1', tab: 'CONFIRMED', status: 'PASSED' },
      ),
    );

    expect(text(fixture)).toContain('Mục này đã được xử lý: Đã xác nhận.');
    expect(text(fixture)).toContain('Lê Văn Kiểm');
    expect(fixture.nativeElement.querySelector('.decision-footer')).toBeNull();
  });

  it('steps to the neighbours in the list the user came from', () => {
    TestBed.inject(QaQueueContext).items.set([
      { kind: 'FLAG', id: 'f0' },
      { kind: 'FLAG', id: 'f1' },
      { kind: 'SAMPLE', id: 's2' },
    ]);
    const fixture = open();
    const buttons = fixture.nativeElement.querySelectorAll(
      '.header-right-actions button',
    ) as NodeListOf<HTMLButtonElement>;

    buttons[0].click();
    expect(navigate).toHaveBeenCalledWith(['/qa-queue', 'FLAG', 'f0']);
    buttons[1].click();
    expect(navigate).toHaveBeenCalledWith(['/qa-queue', 'SAMPLE', 's2']);
  });

  it('disables the neighbour buttons when the list is unknown', () => {
    const fixture = open();
    const buttons = fixture.nativeElement.querySelectorAll(
      '.header-right-actions button',
    ) as NodeListOf<HTMLButtonElement>;

    expect(buttons[0].disabled).toBeTrue();
    expect(buttons[1].disabled).toBeTrue();
  });

  it('says the item was not found for a 404', () => {
    const fixture = open('FLAG', 'gone', null);
    http.expectOne(`${api}/qa-queue/FLAG/gone`).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text(fixture)).toContain('Không tìm thấy mục QA này.');
  });

  it('says the item was not found for an unknown kind, without asking the API', () => {
    const fixture = open('OTHER', 'x', null);

    expect(text(fixture)).toContain('Không tìm thấy mục QA này.');
  });

  it('retries a failed load', () => {
    const fixture = open('FLAG', 'f1', null);
    http
      .expectOne(`${api}/qa-queue/FLAG/f1`)
      .flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    click(fixture, '.state-block button');
    http.expectOne(`${api}/qa-queue/FLAG/f1`).flush(envelope(detail()));
    fixture.detectChanges();

    expect(text(fixture)).toContain('QA-0331');
  });
});
