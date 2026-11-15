import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { ImportBatchSummary } from '../../core/import-history.models';
import { ImportHistoryListComponent } from './import-history-list.component';

const batch = (id: string, fileName: string, errorRows: number): ImportBatchSummary => ({
  id,
  fileName,
  status: 'COMPLETED',
  totalRows: 100,
  importedRows: 100 - errorRows,
  duplicateRows: 0,
  errorRows,
  errors: null,
  importedAt: '2026-11-04T10:00:00Z',
  importedBy: 'u1',
});

describe('ImportHistoryListComponent', () => {
  let http: HttpTestingController;
  const base = environment.apiUrl;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ImportHistoryListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  function flush(data: unknown): void {
    http
      .expectOne((r) => r.url === `${base}/transaction-imports`)
      .flush({
        success: true,
        status: 200,
        data,
      });
  }

  it('loads page 1 on init and renders rows', () => {
    const fixture = TestBed.createComponent(ImportHistoryListComponent);
    fixture.detectChanges();
    flush({
      items: [batch('11111111-aaaa-bbbb-cccc-000000000001', 'tx-oct.xlsx', 0)],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const rows = el.querySelectorAll('tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('tx-oct.xlsx');
    expect(rows[0].textContent).toContain('100');
  });

  it('shows the error count in red when errorRows > 0 and not when it is 0', () => {
    const fixture = TestBed.createComponent(ImportHistoryListComponent);
    fixture.detectChanges();
    flush({
      items: [
        batch('11111111-aaaa-bbbb-cccc-000000000001', 'with-errors.xlsx', 3),
        batch('22222222-aaaa-bbbb-cccc-000000000002', 'clean.xlsx', 0),
      ],
      totalCount: 2,
      page: 1,
      pageSize: 20,
    });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const rows = el.querySelectorAll('tbody tr');
    expect(rows[0].querySelector('.error-count')).toBeTruthy();
    expect(rows[1].querySelector('.error-count')).toBeFalsy();
  });

  it('clicking page 2 requests page 2 and updates the range text', () => {
    const fixture = TestBed.createComponent(ImportHistoryListComponent);
    fixture.detectChanges();
    flush({
      items: Array.from({ length: 20 }, (_, i) => batch(`id-${i}`, `file-${i}.xlsx`, 0)),
      totalCount: 48,
      page: 1,
      pageSize: 20,
    });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const pageButtons = Array.from(el.querySelectorAll<HTMLButtonElement>('.page-button'));
    const pageTwo = pageButtons.find((b) => b.textContent?.trim() === '2')!;
    pageTwo.click();

    const req = http.expectOne((r) => r.url === `${base}/transaction-imports`);
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush({
      success: true,
      status: 200,
      data: {
        items: Array.from({ length: 20 }, (_, i) => batch(`id2-${i}`, `file2-${i}.xlsx`, 0)),
        totalCount: 48,
        page: 2,
        pageSize: 20,
      },
    });
    fixture.detectChanges();

    expect(el.querySelector('.table-footer span')?.textContent).toContain('21');
    expect(el.querySelector('.table-footer span')?.textContent).toContain('40');
  });

  it('shows the empty state when the result has no items', () => {
    const fixture = TestBed.createComponent(ImportHistoryListComponent);
    fixture.detectChanges();
    flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.empty')).toBeTruthy();
  });

  it('shows the failed state on API error and retries on click', () => {
    const fixture = TestBed.createComponent(ImportHistoryListComponent);
    fixture.detectChanges();
    http
      .expectOne((r) => r.url === `${base}/transaction-imports`)
      .flush({ success: false, status: 500, data: null }, { status: 500, statusText: 'Error' });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.state-error')).toBeTruthy();

    (el.querySelector('.state-error button') as HTMLButtonElement).click();
    flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    fixture.detectChanges();

    expect(el.querySelector('.state-error')).toBeFalsy();
    expect(el.querySelector('.empty')).toBeTruthy();
  });
});
