import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { ReconciliationService } from './reconciliation.service';

describe('ReconciliationService', () => {
  let service: ReconciliationService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/reconciliations`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ReconciliationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists with only the filters that hold a value', () => {
    let totalCount = -1;
    service
      .list({
        status: 'NOT_MATCHED',
        search: '',
        page: 2,
        from: '2026-10-01',
        productCode: undefined,
      })
      .subscribe((list) => (totalCount = list.totalCount));

    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys().sort()).toEqual(['from', 'page', 'status']);
    expect(request.request.params.get('status')).toBe('NOT_MATCHED');
    request.flush({ success: true, data: { items: [], totalCount: 7, page: 2, pageSize: 20 } });

    expect(totalCount).toBe(7);
  });

  it('reads one transaction code', () => {
    let code = '';
    service.detail('a/b').subscribe((detail) => (code = detail.item.transactionCode));

    const request = http.expectOne(`${url}/a%2Fb`);
    request.flush({ success: true, data: { item: { transactionCode: 'GD-1' } } });

    expect(code).toBe('GD-1');
  });

  it('exports the filtered rows as a blob without paging', () => {
    let size = 0;
    service
      .export({ status: 'MATCHED', page: 3, pageSize: 20 })
      .subscribe((blob) => (size = blob.size));

    const request = http.expectOne((r) => r.url === `${url}/export`);
    expect(request.request.responseType).toBe('blob');
    expect(request.request.params.keys()).toEqual(['status']);
    request.flush(new Blob(['xlsx']));

    expect(size).toBe(4);
  });

  it('flags the quality of an approved submission', () => {
    service
      .flagQuality('s-1', { reason: 'Chứng từ không khớp báo cáo', severity: 'HIGH' })
      .subscribe();

    const request = http.expectOne(`${environment.apiUrl}/submissions/s-1/quality-flags`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      reason: 'Chứng từ không khớp báo cáo',
      severity: 'HIGH',
    });
    request.flush({ success: true, data: {} });
  });
});
