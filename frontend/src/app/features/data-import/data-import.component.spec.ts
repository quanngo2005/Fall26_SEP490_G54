import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthenticatedUser } from '../../core/auth.models';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ImportBatchResult } from '../../core/import-history.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { DataImportComponent } from './data-import.component';
import { By } from '@angular/platform-browser';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const api = environment.apiUrl;
const importsUrl = `${api}/transaction-imports`;
const ratingsUrl = `${api}/service-rating-imports`;
const reconUrl = `${api}/reconciliations`;
const envelope = <T>(data: T) => ({ success: true, status: 200, data });
const period = {
  id: 'p11',
  code: '2026-11',
  name: '11/2026',
  startDate: '2000-01-01',
  endDate: '2999-12-31',
  periodType: 'MONTH',
};
const reconList = (totalCount: number) =>
  envelope({
    items: [],
    totalCount,
    page: 1,
    pageSize: 1,
    counts: { all: 0, notMatched: 0, inReview: 0, matched: 0 },
    options: { departments: [], jobPositions: [], products: [], reasons: [] },
  });
const batch = (over: Partial<ImportBatchResult> = {}): ImportBatchResult => ({
  id: 'b1',
  fileName: '06_Giao_dich.xlsx',
  status: 'COMPLETED',
  totalRows: 23,
  importedRows: 18,
  duplicateRows: 0,
  errorRows: 5,
  errors: [
    {
      row: 22,
      field: 'Mã NV*',
      code: 'EMPLOYEE_NOT_FOUND',
      message: "Mã nhân viên 'NV999' không tồn tại.",
    },
  ],
  importedAt: '2026-11-19T01:00:00Z',
  importedBy: 'u1',
  reconcile: { matched: 9, notMatched: 2, inReview: 1 },
  ...over,
});
const xlsx = (name = '06_Giao_dich.xlsx', content = 'x') => new File([content], name);

describe('DataImportComponent', () => {
  let http: HttpTestingController;
  const user = signal<AuthenticatedUser | null>(null);
  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'User', roles: [], permissions });

  const text = (fixture: ComponentFixture<DataImportComponent>) =>
    ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
  const el = <T extends Element>(
    fixture: ComponentFixture<DataImportComponent>,
    selector: string,
  ) => (fixture.nativeElement as HTMLElement).querySelector<T>(selector);

  /** Flushes the context calls: periods, waiting codes, last import. */
  const flushContext = (waiting = 12) => {
    http.expectOne(`${api}/evaluations/periods`).flush(envelope([period]));
    const waitingRequest = http.expectOne((r) => r.url === reconUrl);
    expect(waitingRequest.request.params.get('status')).toBe('IN_REVIEW');
    expect(waitingRequest.request.params.get('reason')).toBe('NO_IMPORTED_TRANSACTION');
    expect(waitingRequest.request.params.get('periodId')).toBe('p11');
    waitingRequest.flush(reconList(waiting));
    http
      .expectOne((r) => r.url === importsUrl && r.method === 'GET')
      .flush(envelope({ items: [], totalCount: 0, page: 1, pageSize: 1 }));
  };

  const open = (waiting = 12) => {
    const fixture = TestBed.createComponent(DataImportComponent);
    fixture.detectChanges();
    flushContext(waiting);
    fixture.detectChanges();
    return fixture;
  };

  const choose = (fixture: ComponentFixture<DataImportComponent>, file: File) => {
    const input = el<HTMLInputElement>(fixture, 'input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };

  const button = (fixture: ComponentFixture<DataImportComponent>, label: string) =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((b) => b.textContent?.trim() === label);
  const uploadButton = (fixture: ComponentFixture<DataImportComponent>) =>
    button(fixture, 'Tải lên và đối soát')!;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DataImportComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: user.asReadonly() } },
      ],
    })
      .overrideComponent(DataImportComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
    as(['TRANSACTION_IMPORT_MANAGE', 'SERVICE_RATING_IMPORT_MANAGE']);
  });

  afterEach(() => http.verify());

  it('shows the waiting codes of the current period with a link to them, and the upload stays disabled', () => {
    const fixture = open();
    const body = text(fixture);

    expect(body).toContain('Nhập dữ liệu');
    expect(body).toContain('Kỳ 11/2026: 12 mã giao dịch đang chờ file.');
    const link = el<HTMLAnchorElement>(fixture, '.context-card a')!;
    expect(link.getAttribute('href')).toBe(
      '/reconciliations?status=IN_REVIEW&reason=NO_IMPORTED_TRANSACTION',
    );
    expect(
      fixture.debugElement.query(By.css('.context-card a')).injector.get(RouterLink).state,
    ).toEqual({ period: 'p11' });
    expect(uploadButton(fixture).disabled).toBeTrue();
  });

  it('hides the context card when the waiting count cannot be read', () => {
    const fixture = TestBed.createComponent(DataImportComponent);
    fixture.detectChanges();
    http.expectOne(`${api}/evaluations/periods`).flush(envelope([period]));
    http.expectOne((r) => r.url === reconUrl).flush(null, { status: 403, statusText: 'Forbidden' });
    http
      .expectOne((r) => r.url === importsUrl)
      .flush(envelope({ items: [], totalCount: 0, page: 1, pageSize: 1 }));
    fixture.detectChanges();

    expect(el(fixture, '.context-card')).toBeNull();
  });

  it('refuses a file that is not .xlsx without calling the API', () => {
    const fixture = open();
    choose(fixture, xlsx('data.csv'));

    expect(text(fixture)).toContain('Chỉ nhận tệp Excel .xlsx.');
    expect(uploadButton(fixture).disabled).toBeTrue();
  });

  it('uploads the file, then shows the file result, the reconcile result and the rejected rows', () => {
    const fixture = open();
    choose(fixture, xlsx());
    uploadButton(fixture).click();
    fixture.detectChanges();

    const request = http.expectOne((r) => r.url === importsUrl && r.method === 'POST');
    expect((request.request.body as FormData).get('file')).toEqual(jasmine.any(File));
    request.event({ type: HttpEventType.UploadProgress, loaded: 40, total: 100 });
    fixture.detectChanges();
    expect(el(fixture, '[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('40');

    request.flush({ success: true, status: 201, data: batch() });
    flushContext(3);
    fixture.detectChanges();
    const body = text(fixture);

    expect(body).toContain('Kết quả tệp');
    expect(body).toContain('Đối soát tự động sau khi nhập');
    expect(body).toContain('mã không khớp, trưởng phòng cần xử lý');
    expect(body).toContain('Kỳ 11/2026: 3 mã giao dịch đang chờ file.');
    expect(body).toContain("Mã nhân viên 'NV999' không tồn tại.");
    expect(body).toContain('Chỉ hiển thị 20 dòng lỗi đầu tiên');
    const hrefs = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
        '.reconcile-list a',
      ),
    ).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual([
      '/reconciliations?status=NOT_MATCHED',
      '/reconciliations?status=IN_REVIEW',
    ]);
  });

  it('opens the error drawer of the new batch from the result', () => {
    const fixture = open();
    choose(fixture, xlsx());
    uploadButton(fixture).click();
    http
      .expectOne((r) => r.url === importsUrl && r.method === 'POST')
      .flush({ success: true, status: 201, data: batch() });
    flushContext();
    fixture.detectChanges();

    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => b.textContent?.trim() === 'Xem lỗi')!
      .click();
    fixture.detectChanges();

    http.expectOne(`${importsUrl}/b1`).flush(envelope(batch({ errors: null, reconcile: null })));
    http
      .expectOne((r) => r.url === `${importsUrl}/b1/errors`)
      .flush(envelope({ items: batch().errors, totalCount: 1, page: 1, pageSize: 20 }));
    fixture.detectChanges();

    expect(el(fixture, 'app-import-batch-drawer #import-drawer-title')?.textContent).toContain(
      '06_Giao_dich.xlsx',
    );
  });

  it('says reconciliation did not run instead of showing zeros when reconcile is null', () => {
    const fixture = open();
    choose(fixture, xlsx());
    uploadButton(fixture).click();
    http
      .expectOne((r) => r.url === importsUrl && r.method === 'POST')
      .flush({
        success: true,
        status: 201,
        data: batch({ reconcile: null, errorRows: 0, errors: [] }),
      });
    flushContext();
    fixture.detectChanges();

    expect(text(fixture)).toContain('đối soát chưa chạy xong');
    expect(el(fixture, '.reconcile-list')).toBeNull();
  });

  it('returns to step 1 with the server reason when the whole file is rejected', () => {
    const fixture = open();
    choose(fixture, xlsx());
    uploadButton(fixture).click();
    http
      .expectOne((r) => r.url === importsUrl && r.method === 'POST')
      .flush(
        {
          success: false,
          status: 400,
          message: 'Thiếu cột bắt buộc: Mã NV*',
          errorCode: 'IMPORT_INVALID_FILE',
        },
        { status: 400, statusText: 'Bad Request' },
      );
    fixture.detectChanges();
    const body = text(fixture);

    expect(body).toContain('Tệp bị từ chối.');
    expect(body).toContain('Thiếu cột bắt buộc: Mã NV*');
    expect(uploadButton(fixture).disabled).toBeFalse();
  });

  it('downloads the template', () => {
    const fixture = open();
    const click = jasmine.createSpy('click');
    spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    const realCreate = document.createElement.bind(document);
    spyOn(document, 'createElement').and.callFake((tag: string) => {
      const node = realCreate(tag);
      if (tag === 'a') (node as HTMLAnchorElement).click = click;
      return node;
    });
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => b.textContent?.trim() === 'Tải tệp mẫu')!
      .click();

    http.expectOne(`${importsUrl}/template`).flush(new Blob(['x']));

    expect(click).toHaveBeenCalled();
  });

  it('offers the service rating type only with its permission', () => {
    as(['TRANSACTION_IMPORT_MANAGE']);
    const withoutPermission = open();
    expect(button(withoutPermission, 'Đánh giá dịch vụ')).toBeUndefined();

    as(['TRANSACTION_IMPORT_MANAGE', 'SERVICE_RATING_IMPORT_MANAGE']);
    const withPermission = open();
    expect(button(withPermission, 'Đánh giá dịch vụ')?.disabled).toBeFalse();
  });

  it('uploads a service rating file and shows the file result without reconciliation', () => {
    const fixture = open();
    button(fixture, 'Đánh giá dịch vụ')!.click();
    fixture.detectChanges();
    expect(text(fixture)).toContain('Ngày đánh giá* (dd/MM/yyyy)');
    expect(el(fixture, '.context-card')).toBeNull();

    choose(fixture, xlsx('07_Danh_gia_dich_vu.xlsx'));
    button(fixture, 'Tải lên')!.click();
    fixture.detectChanges();
    const request = http.expectOne((r) => r.url === ratingsUrl && r.method === 'POST');
    request.event({ type: HttpEventType.UploadProgress, loaded: 100, total: 100 });
    fixture.detectChanges();
    expect(text(fixture)).toContain('Đang kiểm tra và lưu…');

    request.flush({
      success: true,
      status: 201,
      data: batch({
        fileName: '07_Danh_gia_dich_vu.xlsx',
        totalRows: 16,
        importedRows: 15,
        errorRows: 1,
        reconcile: null,
      }),
    });
    fixture.detectChanges();
    const body = text(fixture);

    expect(body).toContain('Kết quả tệp');
    expect(body).toContain("Mã nhân viên 'NV999' không tồn tại.");
    expect(body).not.toContain('Đối soát tự động sau khi nhập');
    expect(body).not.toContain('đối soát chưa chạy xong');
  });

  it('downloads the service rating template', () => {
    const fixture = open();
    button(fixture, 'Đánh giá dịch vụ')!.click();
    spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    const realCreate = document.createElement.bind(document);
    spyOn(document, 'createElement').and.callFake((tag: string) => {
      const node = realCreate(tag);
      if (tag === 'a') (node as HTMLAnchorElement).click = () => undefined;
      return node;
    });
    button(fixture, 'Tải tệp mẫu')!.click();

    http.expectOne(`${ratingsUrl}/template`).flush(new Blob(['x']));
  });
});
