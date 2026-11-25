import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { ServiceRatingImportService } from './service-rating-import.service';

describe('ServiceRatingImportService', () => {
  let http: HttpTestingController;
  let service: ServiceRatingImportService;
  const url = `${environment.apiUrl}/service-rating-imports`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ServiceRatingImportService);
  });

  afterEach(() => http.verify());

  it('uploads the file to /service-rating-imports and reports progress then the batch', () => {
    const events: unknown[] = [];
    service.upload(new File(['x'], '07.xlsx')).subscribe((event) => events.push(event));

    const request = http.expectOne(url);
    expect(request.request.method).toBe('POST');
    expect((request.request.body as FormData).get('file')).toEqual(jasmine.any(File));
    request.event({ type: HttpEventType.UploadProgress, loaded: 25, total: 100 });
    const data = {
      id: 'b1',
      fileName: '07.xlsx',
      status: 'COMPLETED',
      totalRows: 2,
      importedRows: 2,
      duplicateRows: 0,
      errorRows: 0,
      errors: [],
      importedAt: '2026-11-23T01:00:00Z',
      importedBy: 'u1',
      reconcile: null,
    };
    request.flush({ success: true, status: 201, data });

    expect(events).toEqual([
      { kind: 'progress', percent: 25 },
      { kind: 'done', result: data },
    ]);
  });

  it('downloads the rating template as a blob', () => {
    service.downloadTemplate().subscribe();

    const request = http.expectOne(`${url}/template`);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['x']));
  });

  it('reads a rating batch, its errors and its saved ratings page by page', () => {
    const batch = { id: 'b 1', fileName: '07.xlsx' };
    let detail: unknown;
    let ratings: unknown;
    service.detail('b 1').subscribe((value) => (detail = value));
    service.errors('b 1', 2, 20).subscribe();
    service.ratings('b 1', 1, 50).subscribe((value) => (ratings = value));

    http.expectOne(`${url}/b%201`).flush({ success: true, status: 200, data: batch });
    const errors = http.expectOne((r) => r.url === `${url}/b%201/errors`);
    expect(errors.request.params.get('page')).toBe('2');
    errors.flush({
      success: true,
      status: 200,
      data: { items: [], totalCount: 0, page: 2, pageSize: 20 },
    });
    const rows = http.expectOne((r) => r.url === `${url}/b%201/ratings`);
    expect(rows.request.params.get('pageSize')).toBe('50');
    const page = {
      items: [{ id: 'r1', transactionCode: 'GD1', score: 5 }],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    };
    rows.flush({ success: true, status: 200, data: page });

    expect(detail).toEqual(batch);
    expect(ratings).toEqual(page);
  });
});
