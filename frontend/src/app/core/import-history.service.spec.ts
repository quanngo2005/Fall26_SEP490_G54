import { provideHttpClient } from '@angular/common/http';
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
});
