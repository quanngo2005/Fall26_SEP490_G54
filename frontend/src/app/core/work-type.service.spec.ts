import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { WorkTypeDetail } from './work-type.models';
import { WorkTypeService } from './work-type.service';

describe('WorkTypeService', () => {
  let service: WorkTypeService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/work-types`;
  const workType: WorkTypeDetail = {
    id: 'w1',
    code: 'HUY_DONG',
    name: 'Huy động tiền gửi',
    description: null,
    workflowDefinitionId: 'wf1',
    workflowName: 'Quy trình 2 cấp',
    status: 'ARCHIVED',
    createdAt: '2026-10-01T00:00:00+00:00',
    kpis: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WorkTypeService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends filters as query parameters and reads the status as-is', () => {
    service.list({ keyword: 'huy', page: 1, pageSize: 50 }).subscribe((page) => {
      expect(page.items[0].status).toBe('ARCHIVED');
    });
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('keyword')).toBe('huy');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({
      success: true,
      status: 200,
      data: { items: [workType], totalCount: 1, page: 1, pageSize: 50 },
    });
  });

  it('loads one work type by an encoded id', () => {
    service.detail('a b').subscribe((item) => expect(item.code).toBe('HUY_DONG'));
    const request = http.expectOne(`${url}/a%20b`);
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, status: 200, data: workType });
  });

  it('treats a failed or empty envelope as an error instead of sample data', () => {
    let failures = 0;
    service.detail('x').subscribe({ error: () => failures++ });
    http.expectOne(`${url}/x`).flush({ success: false, status: 404, errorCode: 'NOT_FOUND' });
    service.list().subscribe({ error: () => failures++ });
    http.expectOne(url).flush({ success: true, status: 200, data: null });
    expect(failures).toBe(2);
  });
});
