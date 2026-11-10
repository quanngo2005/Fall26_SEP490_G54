import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { RiskEventCreateModalComponent } from './risk-event-create-modal.component';

const api = environment.apiUrl;
const envelope = <T>(data: T) => ({ success: true, status: 200, data });
const employee = (id: string, code: string, name: string) => ({
  id,
  employeeCode: code,
  fullName: name,
});

describe('RiskEventCreateModalComponent', () => {
  let http: HttpTestingController;

  const open = (employees = [employee('e1', 'NV0310', 'Trần Quốc Bảo')]) => {
    const fixture = TestBed.createComponent(RiskEventCreateModalComponent);
    fixture.detectChanges();
    const request = http.expectOne((r) => r.url === `${api}/employees`);
    expect(request.request.params.get('status')).toBe('ACTIVE');
    expect(request.request.params.get('pageSize')).toBe('100');
    request.flush(
      envelope({ items: employees, totalCount: employees.length, page: 1, pageSize: 100 }),
    );
    fixture.detectChanges();
    return fixture;
  };
  const text = (fixture: { nativeElement: HTMLElement }) =>
    (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
  const el = <T extends HTMLElement>(fixture: { nativeElement: HTMLElement }, selector: string) =>
    fixture.nativeElement.querySelector(selector) as T;
  const set = (
    fixture: { nativeElement: HTMLElement },
    selector: string,
    value: string,
    event = 'input',
  ) => {
    const target = el<HTMLInputElement>(fixture, selector);
    target.value = value;
    target.dispatchEvent(new Event(event));
  };
  const fill = (fixture: { nativeElement: HTMLElement }) => {
    set(fixture, '#risk-employee', 'e1', 'change');
    set(fixture, '#risk-type', 'KYC_VIOLATION', 'change');
    set(fixture, '#risk-penalty', '3');
    set(fixture, '#risk-description', '  Khai Refer ID không có trong giao dịch.  ');
  };
  const submitButton = (fixture: { nativeElement: HTMLElement }) =>
    el<HTMLButtonElement>(fixture, '.risk-footer .btn-primary');
  const pick = (fixture: { nativeElement: HTMLElement }, files: File[]) => {
    const input = el<HTMLInputElement>(fixture, 'input[type="file"]');
    const transfer = new DataTransfer();
    files.forEach((f) => transfer.items.add(f));
    input.files = transfer.files;
    input.dispatchEvent(new Event('change'));
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RiskEventCreateModalComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('offers the active employees, the violation types and the notice', () => {
    const fixture = open();
    const body = text(fixture);

    expect(body).toContain('Ghi sự kiện rủi ro');
    expect(body).toContain('NV0310 · Trần Quốc Bảo');
    expect(body).toContain('Khai sai thông tin khách hàng');
    expect(body).toContain('Thiếu chứng từ');
    expect(body).toContain('Vi phạm quy trình KYC');
    expect(body).toContain('Sau khi ghi không sửa được');
    expect(body).toContain('Esc để hủy · Ctrl+S để lưu');
    expect(body).toContain('Nghiêm trọng');
  });

  it('still opens when the employees cannot be read', () => {
    const fixture = TestBed.createComponent(RiskEventCreateModalComponent);
    fixture.detectChanges();
    http
      .expectOne((r) => r.url === `${api}/employees`)
      .flush('', { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(text(fixture)).toContain('Chọn nhân viên');
    expect(el<HTMLSelectElement>(fixture, '#risk-employee').disabled).toBeFalse();
  });

  it('shows what is missing for each field and sends nothing', () => {
    const fixture = open();
    submitButton(fixture).click();
    fixture.detectChanges();

    http.expectNone(`${api}/risk-events`);
    const body = text(fixture);
    expect(body).toContain('Vui lòng chọn nhân viên.');
    expect(body).toContain('Vui lòng chọn loại vi phạm.');
    expect(body).toContain('Điểm phạt phải lớn hơn 0 và không quá 100.');
    expect(body).toContain('Vui lòng nhập mô tả');
  });

  it('refuses a penalty that is zero or too large, and a violation date in the future', () => {
    const fixture = open();
    fill(fixture);
    set(fixture, '#risk-penalty', '0');
    submitButton(fixture).click();
    fixture.detectChanges();
    expect(text(fixture)).toContain('Điểm phạt phải lớn hơn 0');
    set(fixture, '#risk-penalty', '101');
    submitButton(fixture).click();
    fixture.detectChanges();
    expect(text(fixture)).toContain('không quá 100');

    set(fixture, '#risk-penalty', '3');
    set(fixture, '#risk-date', '2999-01-01');
    submitButton(fixture).click();
    fixture.detectChanges();
    expect(text(fixture)).toContain('không được ở tương lai');
    http.expectNone(`${api}/risk-events`);
  });

  it('records the event in one step with the chosen severity, then reports the code', () => {
    const fixture = open();
    const results: { code: string; uploadFailed: boolean }[] = [];
    fixture.componentInstance.created.subscribe((r) => results.push(r));
    fill(fixture);
    set(fixture, '#risk-date', '2026-10-22');
    set(fixture, '#risk-work', '  WK-1051 ');
    el<HTMLButtonElement>(fixture, '.severity-btn:nth-child(3)').click();
    fixture.detectChanges();

    submitButton(fixture).click();
    const request = http.expectOne(`${api}/risk-events`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      employeeId: 'e1',
      eventType: 'KYC_VIOLATION',
      description: 'Khai Refer ID không có trong giao dịch.',
      severity: 'HIGH',
      occurredAt: '2026-10-22T00:00:00+07:00',
      penaltyPoints: 3,
      workCode: 'WK-1051',
      record: true,
    });
    request.flush(envelope({ id: 'r1', code: 'RSK-0046' }));

    expect(results).toEqual([{ code: 'RSK-0046', uploadFailed: false }]);
  });

  it('leaves the work code out when it is blank', () => {
    const fixture = open();
    fill(fixture);
    submitButton(fixture).click();
    const request = http.expectOne(`${api}/risk-events`);

    expect('workCode' in request.request.body).toBeFalse();
    expect(request.request.body.severity).toBe('MEDIUM');
    request.flush(envelope({ id: 'r1', code: 'RSK-0046' }));
  });

  it('uploads the attachments after the event was recorded', () => {
    const fixture = open();
    const results: { code: string; uploadFailed: boolean }[] = [];
    fixture.componentInstance.created.subscribe((r) => results.push(r));
    fill(fixture);
    pick(fixture, [new File(['a'], 'bien_ban.pdf', { type: 'application/pdf' })]);
    fixture.detectChanges();
    expect(text(fixture)).toContain('bien_ban.pdf');

    submitButton(fixture).click();
    http.expectOne(`${api}/risk-events`).flush(envelope({ id: 'r1', code: 'RSK-0046' }));
    const upload = http.expectOne(`${api}/risk-events/r1/evidence`);
    expect((upload.request.body as FormData).getAll('files').length).toBe(1);
    upload.flush(envelope([{}]));

    expect(results).toEqual([{ code: 'RSK-0046', uploadFailed: false }]);
  });

  it('reports a failed upload without undoing the recorded event', () => {
    const fixture = open();
    const results: { code: string; uploadFailed: boolean }[] = [];
    fixture.componentInstance.created.subscribe((r) => results.push(r));
    fill(fixture);
    pick(fixture, [new File(['a'], 'bien_ban.pdf')]);
    submitButton(fixture).click();
    http.expectOne(`${api}/risk-events`).flush(envelope({ id: 'r1', code: 'RSK-0046' }));
    http
      .expectOne(`${api}/risk-events/r1/evidence`)
      .flush({}, { status: 500, statusText: 'Server Error' });

    expect(results).toEqual([{ code: 'RSK-0046', uploadFailed: true }]);
  });

  it('rejects files of the wrong type or over 25MB, keeps the good ones, and can remove one', () => {
    const fixture = open();
    const big = new File(['x'], 'lon.pdf');
    Object.defineProperty(big, 'size', { value: 26 * 1024 * 1024 });
    pick(fixture, [new File(['a'], 'ok.png'), new File(['b'], 'virus.exe'), big]);
    fixture.detectChanges();

    const body = text(fixture);
    expect(body).toContain('ok.png');
    expect(body).toContain('Tệp virus.exe không đúng định dạng cho phép.');
    expect(body).toContain('Tệp lon.pdf vượt quá 25MB.');

    el<HTMLButtonElement>(fixture, '.file-chip button').click();
    fixture.detectChanges();
    expect(el(fixture, '.file-chip')).toBeNull();
  });

  it('says why the API refused the event', () => {
    const cases: [object, number, string][] = [
      [{ errorCode: 'RISK_EVENT_PERIOD_UNAVAILABLE' }, 409, 'Không có kỳ đánh giá đang mở'],
      [
        { errorCode: 'VALIDATION_ERROR', errors: ['Không tìm thấy công việc có mã workCode.'] },
        400,
        'Không tìm thấy công việc có mã này.',
      ],
      [
        { errorCode: 'VALIDATION_ERROR', errors: ['Mô tả là bắt buộc.'] },
        400,
        'Dữ liệu chưa hợp lệ',
      ],
      [
        {
          errorCode: 'FORBIDDEN',
          message: 'Bạn không thể ghi nhận sự kiện rủi ro cho chính mình.',
        },
        403,
        'ghi hoặc gỡ điểm phạt sự kiện rủi ro của chính mình',
      ],
      [{}, 500, 'Không thực hiện được'],
    ];
    for (const [body, status, expected] of cases) {
      const fixture = open();
      fill(fixture);
      submitButton(fixture).click();
      http.expectOne(`${api}/risk-events`).flush(body, { status, statusText: 'Error' });
      fixture.detectChanges();
      expect(text(fixture)).toContain(expected);
      expect(submitButton(fixture).disabled).toBeFalse();
    }
  });

  it('cancels with Escape and the buttons, and saves with Ctrl+S', () => {
    const fixture = open();
    let dismissed = 0;
    fixture.componentInstance.dismissed.subscribe(() => dismissed++);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    el<HTMLButtonElement>(fixture, '.modal-close').click();
    el<HTMLButtonElement>(fixture, '.risk-footer .btn-secondary').click();
    expect(dismissed).toBe(3);

    fill(fixture);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true }));
    const request = http.expectOne(`${api}/risk-events`);
    request.flush(envelope({ id: 'r1', code: 'RSK-0046' }));
  });
});
