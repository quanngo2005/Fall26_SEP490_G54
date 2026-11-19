import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { ImportHistoryService } from './import-history.service';

describe('ImportHistoryService', () => {
  let http: HttpTestingController;
  let service: ImportHistoryService;
  const url = `${environment.apiUrl}/transaction-imports`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ImportHistoryService);
  });

  afterEach(() => http.verify());

  it('sends page and pageSize and unwraps the paged result', () => {
    let result: unknown;
    service.list(2, 20).subscribe((data) => (result = data));

    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('20');

    const data = {
      items: [
        {
          id: 'b1',
          fileName: 'tx.xlsx',
          status: 'COMPLETED',
          totalRows: 10,
          importedRows: 9,
          duplicateRows: 1,
          errorRows: 0,
          errors: null,
          importedAt: '2026-11-04T10:00:00Z',
          importedBy: 'u1',
        },
      ],
      totalCount: 1,
      page: 2,
      pageSize: 20,
    };
    request.flush({ success: true, status: 200, data });

    expect(result).toEqual(data);
  });

  it('throws when the API responds without success', () => {
    let error: unknown;
    service.list(1, 20).subscribe({ error: (e) => (error = e) });

    http.expectOne((r) => r.url === url).flush({ success: false, status: 500, data: null });

    expect(error).toBeTruthy();
  });
  it('uploads the file as multipart field "file" and reports progress then the batch with reconcile', () => {
    const events: unknown[] = [];
    const file = new File(['x'], 'tx.xlsx');
    service.upload(file).subscribe((event) => events.push(event));

    const request = http.expectOne(url);
    expect(request.request.method).toBe('POST');
    expect(request.request.reportProgress).toBeTrue();
    expect((request.request.body as FormData).get('file')).toEqual(jasmine.any(File));

    request.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 100 });
    const data = {
      id: 'b1',
      fileName: 'tx.xlsx',
      status: 'COMPLETED',
      totalRows: 3,
      importedRows: 2,
      duplicateRows: 0,
      errorRows: 1,
      errors: [{ row: 3, field: 'Mã NV*', code: 'EMPLOYEE_NOT_FOUND', message: 'x' }],
      importedAt: '2026-11-19T01:00:00Z',
      importedBy: 'u1',
      reconcile: { matched: 1, notMatched: 0, inReview: 1 },
    };
    request.flush({ success: true, status: 201, data });

    expect(events).toEqual([
      { kind: 'progress', percent: 50 },
      { kind: 'done', result: data },
    ]);
  });

  it('downloads the template as a blob', () => {
    service.downloadTemplate().subscribe();

    const request = http.expectOne(`${url}/template`);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['x']));
  });
});
