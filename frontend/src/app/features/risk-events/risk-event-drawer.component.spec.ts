import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { RiskEventDetails, RiskEventListItem } from '../../core/risk-event.models';
import { RiskEventDrawerComponent } from './risk-event-drawer.component';

const api = environment.apiUrl;
const envelope = <T>(data: T) => ({ success: true, status: 200, data });
const item = (over: Partial<RiskEventListItem> = {}): RiskEventListItem => ({
  id: 'r1',
  code: 'RSK-0045',
  occurredAt: '2026-10-22T00:00:00+07:00',
  employeeId: 'e1',
  employeeCode: 'NV0310',
  employeeName: 'Trần Quốc Bảo',
  departmentId: 'd1',
  departmentName: 'KHCN',
  eventType: 'WRONG_CUSTOMER_INFO',
  severity: 'MEDIUM',
  penaltyPoints: 3,
  workId: 'w1',
  workCode: 'WK-1051',
  status: 'CONFIRMED',
  state: 'RECORDED',
  createdAt: '2026-10-23T02:30:12Z',
  ...over,
});
const details = (over: Partial<RiskEventDetails> = {}): RiskEventDetails => ({
  item: item(),
  jobPositionTitle: 'Sales Officer',
  description: 'Khai Refer ID không có trong giao dịch Core Banking.',
  periodCode: '2026-Q4',
  periodName: 'Q4/2026',
  recordedByName: 'Lê Văn Kiểm',
  recordedAt: '2026-10-23T02:30:12Z',
  reversedAt: null,
  reversedByName: null,
  reversalReason: null,
  attachments: [
    { id: 'ev1', fileName: 'bien_ban_0045.pdf', fileSize: 184320, mimeType: 'application/pdf' },
  ],
  canReverse: true,
  ...over,
});

describe('RiskEventDrawerComponent', () => {
  let http: HttpTestingController;

  const open = (body: RiskEventDetails | null = details(), id = 'r1') => {
    const fixture = TestBed.createComponent(RiskEventDrawerComponent);
    fixture.componentRef.setInput('eventId', id);
    fixture.detectChanges();
    if (body) {
      http.expectOne(`${api}/risk-events/${id}/details`).flush(envelope(body));
      fixture.detectChanges();
    }
    return fixture;
  };
  const text = (fixture: { nativeElement: HTMLElement }) =>
    (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
  const click = (fixture: { nativeElement: HTMLElement }, selector: string) =>
    (fixture.nativeElement.querySelector(selector) as HTMLElement).click();
  const type = (fixture: { nativeElement: HTMLElement }, value: string) => {
    const box = fixture.nativeElement.querySelector('#risk-reverse-reason') as HTMLTextAreaElement;
    box.value = value;
    box.dispatchEvent(new Event('input'));
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RiskEventDrawerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('shows the information, description, attachment and history of the event', () => {
    const fixture = open();
    const body = text(fixture);

    expect(body).toContain('RSK-0045 · Khai sai thông tin khách hàng');
    expect(body).toContain('Ghi bởi Lê Văn Kiểm');
    expect(body).toContain('NV0310 · Trần Quốc Bảo');
    expect(body).toContain('Sales Officer · KHCN');
    expect(body).toContain('22/10/2026');
    expect(body).toContain('Trung bình');
    expect(body).toContain('−3 điểm');
    expect(body).toContain('Q4/2026');
    expect(body).toContain('WK-1051');
    expect(body).toContain('Khai Refer ID không có trong giao dịch Core Banking.');
    expect(body).toContain('bien_ban_0045.pdf');
    expect(body).toContain('180 KB');
    expect(body).toContain('Lê Văn Kiểm đã ghi sự kiện');
    expect(body).toContain('Không sửa được');
    expect(body).toContain('Esc để đóng · ↑↓ chuyển dòng');
    expect(body).toContain('Đã ghi');
  });

  it('says so when there is no attachment, link, period or position', () => {
    const fixture = open(
      details({
        attachments: [],
        periodCode: null,
        periodName: null,
        jobPositionTitle: null,
        item: item({ workCode: null }),
      }),
    );

    expect(text(fixture)).toContain('Không có tệp đính kèm.');
    expect(text(fixture)).toContain('— · KHCN');
  });

  it('opens an attachment through the authenticated preview, and reports a failure', () => {
    const fixture = open();
    const win = spyOn(window, 'open');

    click(fixture, '.link-btn');
    http.expectOne(`${api}/evidence/ev1/preview`).flush(new Blob(['x']));
    expect(win).toHaveBeenCalled();

    click(fixture, '.link-btn');
    http
      .expectOne(`${api}/evidence/ev1/preview`)
      .error(new ProgressEvent('error'), { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(text(fixture)).toContain('Không thể mở tệp đính kèm.');
  });

  it('offers the reversal only when the event can be reversed', () => {
    const allowed = open();
    expect(allowed.nativeElement.querySelector('.reverse-btn')).not.toBeNull();

    const denied = open(details({ canReverse: false }), 'r2');
    expect(denied.nativeElement.querySelector('.reverse-btn')).toBeNull();
  });

  it('refuses a reversal reason shorter than ten characters', () => {
    const fixture = open();
    click(fixture, '.reverse-btn');
    fixture.detectChanges();
    type(fixture, 'ngắn');
    click(fixture, '.btn-danger');
    fixture.detectChanges();

    http.expectNone(`${api}/risk-events/r1/reverse`);
    expect(text(fixture)).toContain('tối thiểu 10 ký tự');
  });

  it('reverses the event with the reason and tells the list', () => {
    const fixture = open();
    const reversed: string[] = [];
    fixture.componentInstance.reversed.subscribe((code) => reversed.push(code));
    click(fixture, '.reverse-btn');
    fixture.detectChanges();
    expect(text(fixture)).toContain('Gỡ điểm phạt sự kiện RSK-0045');
    type(fixture, 'Ghi nhầm nhân viên, cần đảo.');
    click(fixture, '.btn-danger');

    const request = http.expectOne(`${api}/risk-events/r1/reverse`);
    expect(request.request.body).toEqual({ reason: 'Ghi nhầm nhân viên, cần đảo.' });
    request.flush(
      envelope(
        details({
          item: item({ state: 'REVERSED', status: 'DISMISSED' }),
          canReverse: false,
          reversedAt: '2026-10-24T03:00:00Z',
          reversedByName: 'Nguyễn Admin',
          reversalReason: 'Ghi nhầm nhân viên, cần đảo.',
        }),
      ),
    );
    fixture.detectChanges();

    expect(reversed).toEqual(['RSK-0045']);
    expect(fixture.nativeElement.querySelector('.reverse-dialog')).toBeNull();
    expect(text(fixture)).toContain('Nguyễn Admin đã gỡ điểm phạt');
    expect(text(fixture)).toContain('Lý do: Ghi nhầm nhân viên, cần đảo.');
    expect(text(fixture)).toContain('Đã gỡ');
    expect(fixture.nativeElement.querySelector('.reverse-btn')).toBeNull();
    expect(fixture.nativeElement.querySelector('.penalty').classList).toContain('muted');
  });

  it('keeps the reversal open and says why when the API refuses it', () => {
    const fixture = open();
    click(fixture, '.reverse-btn');
    fixture.detectChanges();
    type(fixture, 'Ghi nhầm nhân viên, cần đảo.');
    click(fixture, '.btn-danger');
    http
      .expectOne(`${api}/risk-events/r1/reverse`)
      .flush(
        { errorCode: 'RISK_EVENT_PERIOD_UNAVAILABLE' },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();

    expect(text(fixture)).toContain('Không có kỳ đánh giá đang mở');
    expect(fixture.nativeElement.querySelector('.reverse-dialog')).not.toBeNull();
  });

  it('closes on Escape, and Escape closes only the reversal dialog first', () => {
    const fixture = open();
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);

    click(fixture, '.reverse-btn');
    fixture.detectChanges();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.reverse-dialog')).toBeNull();
    expect(closed).toBe(0);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(closed).toBe(1);
  });

  it('closes from the close button and the backdrop', () => {
    const fixture = open();
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);

    click(fixture, '.drawer-close');
    click(fixture, '.drawer-backdrop');

    expect(closed).toBe(2);
  });

  it('reloads when another event is selected', () => {
    const fixture = open();
    fixture.componentRef.setInput('eventId', 'r2');
    fixture.detectChanges();

    http
      .expectOne(`${api}/risk-events/r2/details`)
      .flush(envelope(details({ item: item({ id: 'r2', code: 'RSK-0044' }) })));
    fixture.detectChanges();
    expect(text(fixture)).toContain('RSK-0044');
  });

  it('says the event was not found for a 404, and a generic error otherwise', () => {
    const missing = open(null, 'gone');
    http
      .expectOne(`${api}/risk-events/gone/details`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    missing.detectChanges();
    expect(text(missing)).toContain('Không tìm thấy sự kiện này');

    const broken = open(null, 'r9');
    http.expectOne(`${api}/risk-events/r9/details`).flush({}, { status: 500, statusText: 'Error' });
    broken.detectChanges();
    expect(text(broken)).toContain('Không tải được chi tiết sự kiện.');
  });
});
