import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import {
  ImportBatchSummary,
  ImportedRating,
  ImportedTransaction,
  ImportRowError,
} from '../../core/import-history.models';
import { ImportBatchDrawerComponent } from './import-batch-drawer.component';

const url = `${environment.apiUrl}/transaction-imports`;
const ratingUrl = `${environment.apiUrl}/service-rating-imports`;
const envelope = <T>(data: T) => ({ success: true, status: 200, data });
const paged = <T>(items: T[], totalCount: number, page = 1, pageSize = 20) =>
  envelope({ items, totalCount, page, pageSize });
const batch = (over: Partial<ImportBatchSummary> = {}): ImportBatchSummary => ({
  id: 'b1',
  fileName: '06_Giao_dich.xlsx',
  status: 'COMPLETED',
  totalRows: 23,
  importedRows: 17,
  duplicateRows: 1,
  errorRows: 5,
  errors: null,
  importedAt: '2026-11-19T01:00:00Z',
  importedBy: 'u1',
  ...over,
});
const rowError = (row: number, over: Partial<ImportRowError> = {}): ImportRowError => ({
  row,
  field: 'Mã NV*',
  code: 'EMPLOYEE_NOT_FOUND',
  message: "Mã nhân viên 'NV999' không tồn tại.",
  ...over,
});
const transaction: ImportedTransaction = {
  id: 't1',
  transactionCode: 'GD0001',
  employeeCode: 'NV001',
  productCode: 'TD_CLASSIC',
  transactionDate: '2026-10-05',
  amount: 15000000,
  denominator: null,
  referEmployeeCode: 'NV002',
  customerCode: 'KH10001',
};

const rating: ImportedRating = {
  id: 'r1',
  transactionCode: 'GD0002',
  employeeCode: 'NV005',
  ratingDate: '2026-11-20',
  score: 1,
  customerComment: 'Chờ quá lâu',
};

describe('ImportBatchDrawerComponent', () => {
  let http: HttpTestingController;

  const text = (fixture: ComponentFixture<ImportBatchDrawerComponent>) =>
    ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
  const el = <T extends Element>(
    fixture: ComponentFixture<ImportBatchDrawerComponent>,
    selector: string,
  ) => (fixture.nativeElement as HTMLElement).querySelector<T>(selector)!;
  const tab = (fixture: ComponentFixture<ImportBatchDrawerComponent>, index: number) =>
    (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="tab"]')[
      index
    ];
  const pageButton = (fixture: ComponentFixture<ImportBatchDrawerComponent>, label: string) =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.ui-page-btn'),
    ).find((b) => b.textContent?.trim() === label)!;

  /** Opens the drawer and answers the batch and its first page of errors. */
  const open = (
    errors: ImportRowError[] = [rowError(22)],
    over: Partial<ImportBatchSummary> = {},
  ) => {
    const fixture = TestBed.createComponent(ImportBatchDrawerComponent);
    fixture.componentRef.setInput('batchId', 'b1');
    fixture.detectChanges();
    http.expectOne(`${url}/b1`).flush(envelope(batch(over)));
    http.expectOne((r) => r.url === `${url}/b1/errors`).flush(paged(errors, errors.length));
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ImportBatchDrawerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('shows the batch header, its counts and the rejected rows with localized error codes', () => {
    const fixture = open([rowError(22), rowError(23, { code: 'NEW_CODE', field: 'Ngày GD*' })]);
    const body = text(fixture);

    expect(el(fixture, '#import-drawer-title').textContent).toContain('06_Giao_dich.xlsx');
    expect(body).toContain('Người nhập u1');
    const counts = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.count-grid dd'),
    ).map((dd) => dd.textContent?.trim());
    expect(counts).toEqual(['23', '17', '1', '5']);
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('Không có mã nhân viên');
    expect(rows[0].textContent).toContain("Mã nhân viên 'NV999' không tồn tại.");
    expect(rows[1].textContent).toContain('NEW_CODE');
  });

  it('requests the next page of errors from the server', () => {
    const fixture = TestBed.createComponent(ImportBatchDrawerComponent);
    fixture.componentRef.setInput('batchId', 'b1');
    fixture.detectChanges();
    http.expectOne(`${url}/b1`).flush(envelope(batch({ errorRows: 25 })));
    http
      .expectOne((r) => r.url === `${url}/b1/errors`)
      .flush(
        paged(
          Array.from({ length: 20 }, (_, i) => rowError(i + 2)),
          25,
        ),
      );
    fixture.detectChanges();

    pageButton(fixture, '2').click();
    const request = http.expectOne((r) => r.url === `${url}/b1/errors`);
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('20');
    request.flush(paged([rowError(99)], 25, 2));
    fixture.detectChanges();

    expect(text(fixture)).toContain('21–25 / 25 dòng');
  });

  it('reads the saved transactions when their tab is first shown, then pages them', () => {
    const fixture = open();
    tab(fixture, 1).click();
    fixture.detectChanges();
    const first = http.expectOne((r) => r.url === `${url}/b1/transactions`);
    expect(first.request.params.get('page')).toBe('1');
    first.flush(paged([transaction], 30));
    fixture.detectChanges();

    const body = text(fixture);
    expect(tab(fixture, 1).getAttribute('aria-selected')).toBe('true');
    expect(body).toContain('GD0001');
    expect(body).toContain('05/10/2026');
    expect(body).toContain('KH10001');
    expect(body).toContain('TD_CLASSIC');
    expect(body).toContain('NV002');

    pageButton(fixture, '2').click();
    expect(
      http.expectOne((r) => r.url === `${url}/b1/transactions`).request.params.get('page'),
    ).toBe('2');

    tab(fixture, 0).click();
    tab(fixture, 1).click();
    http.expectNone((r) => r.url === `${url}/b1/transactions`);
  });

  it('says so when a tab has no rows', () => {
    const fixture = open([], { errorRows: 0 });
    expect(text(fixture)).toContain('Lô này không có dòng lỗi.');

    tab(fixture, 1).click();
    http.expectOne((r) => r.url === `${url}/b1/transactions`).flush(paged([], 0));
    fixture.detectChanges();
    expect(text(fixture)).toContain('Lô này không có giao dịch nào được lưu.');
  });

  it('downloads every page of errors as a UTF-8 CSV with BOM and escaped cells', async () => {
    const fixture = open();
    let saved: Blob | undefined;
    spyOn(URL, 'createObjectURL').and.callFake((blob: Blob | MediaSource) => {
      saved = blob as Blob;
      return 'blob:x';
    });
    const realCreate = document.createElement.bind(document);
    let fileName = '';
    spyOn(document, 'createElement').and.callFake((tag: string) => {
      const node = realCreate(tag);
      if (node instanceof HTMLAnchorElement) node.click = () => (fileName = node.download);
      return node;
    });

    el<HTMLButtonElement>(fixture, '.drawer-footer button').click();
    const first = http.expectOne((r) => r.url === `${url}/b1/errors`);
    expect(first.request.params.get('pageSize')).toBe('200');
    first.flush(paged([rowError(2, { message: 'Sai "định dạng", dòng 2' })], 201, 1, 200));
    const second = http.expectOne((r) => r.url === `${url}/b1/errors`);
    expect(second.request.params.get('page')).toBe('2');
    second.flush(paged([rowError(3, { field: '=SUM(A1)', message: 'a\nb' })], 201, 2, 200));

    expect(fileName).toBe('06_Giao_dich-errors.csv');
    const bytes = new Uint8Array(await saved!.arrayBuffer());
    expect(Array.from(bytes.slice(0, 3))).toEqual([0xef, 0xbb, 0xbf]);
    expect(await saved!.text()).toBe(
      [
        'Dòng,Cột,Mã lỗi,Nội dung',
        '2,Mã NV*,EMPLOYEE_NOT_FOUND,"Sai ""định dạng"", dòng 2"',
        `3,'=SUM(A1),EMPLOYEE_NOT_FOUND,"a\nb"`,
      ].join('\r\n'),
    );
  });

  it('reports a failed download and disables it for a batch without errors', () => {
    const fixture = open();
    el<HTMLButtonElement>(fixture, '.drawer-footer button').click();
    http
      .expectOne((r) => r.url === `${url}/b1/errors`)
      .flush({}, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(text(fixture)).toContain('Không tải được danh sách lỗi.');

    const clean = open([], { errorRows: 0 });
    expect(el<HTMLButtonElement>(clean, '.drawer-footer button').disabled).toBeTrue();
  });

  it('shows the load errors and retries a failed table', () => {
    const fixture = TestBed.createComponent(ImportBatchDrawerComponent);
    fixture.componentRef.setInput('batchId', 'b1');
    fixture.detectChanges();
    http.expectOne(`${url}/b1`).flush({}, { status: 404, statusText: 'Not Found' });
    http
      .expectOne((r) => r.url === `${url}/b1/errors`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(text(fixture)).toContain('Không tải được chi tiết lô nhập.');

    const table = open();
    tab(table, 1).click();
    http
      .expectOne((r) => r.url === `${url}/b1/transactions`)
      .flush({}, { status: 500, statusText: 'Error' });
    table.detectChanges();
    expect(text(table)).toContain('Không tải được dữ liệu.');

    el<HTMLButtonElement>(table, '.table-error button').click();
    http.expectOne((r) => r.url === `${url}/b1/transactions`).flush(paged([transaction], 1));
    table.detectChanges();
    expect(text(table)).toContain('GD0001');
  });

  it('closes on Escape, the close button and the backdrop', () => {
    const fixture = open();
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    el<HTMLButtonElement>(fixture, '.drawer-close').click();
    el<HTMLElement>(fixture, '.drawer-backdrop').click();

    expect(closed).toBe(3);
  });

  it('reads a service rating batch from the rating endpoints and lists its saved ratings', () => {
    const fixture = TestBed.createComponent(ImportBatchDrawerComponent);
    fixture.componentRef.setInput('batchId', 'b1');
    fixture.componentRef.setInput('fileType', 'SERVICE_RATING');
    fixture.detectChanges();
    http.expectOne(`${ratingUrl}/b1`).flush(envelope(batch({ fileName: '07_Danh_gia.xlsx' })));
    http.expectOne((r) => r.url === `${ratingUrl}/b1/errors`).flush(paged([rowError(4)], 1));
    fixture.detectChanges();

    expect(tab(fixture, 1).textContent).toContain('Đánh giá đã nhập');
    tab(fixture, 1).click();
    http.expectOne((r) => r.url === `${ratingUrl}/b1/ratings`).flush(paged([rating], 1));
    http.expectNone((r) => r.url === `${url}/b1/transactions`);
    fixture.detectChanges();

    const row = text(fixture);
    expect(row).toContain('GD0002');
    expect(row).toContain('20/11/2026');
    expect(row).toContain('Chờ quá lâu');
  });
});
