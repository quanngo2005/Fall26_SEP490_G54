import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { EmployeeDetail } from './employee.models';
import { EmployeeService } from './employee.service';

describe('EmployeeService', () => {
  let service: EmployeeService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/employees`;
  const employee: EmployeeDetail = {
    id: '5eed0000-0000-0000-0007-000000000004',
    employeeCode: 'NV004',
    fullName: 'Lê Minh Châu',
    email: 'chau@example.test',
    phone: null,
    departmentId: 'd1',
    departmentCode: 'CN-HN-KD1',
    departmentName: 'Nhóm Kinh doanh 1',
    jobPositionId: 'p1',
    jobPositionCode: 'CV-KD',
    jobPositionTitle: 'Chuyên viên Kinh doanh',
    managerId: null,
    managerCode: null,
    managerName: null,
    status: 'ON_LEAVE',
    directReportsCount: 0,
    lastSyncAt: '2026-10-01T00:00:00+00:00',
    updatedAt: '2026-10-01T00:00:00+00:00',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(EmployeeService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends filters as query parameters, skipping empty values, and reads the status as-is', () => {
    service
      .list({ keyword: 'chau', departmentId: 'd1', page: 2, pageSize: 20 })
      .subscribe((page) => {
        expect(page.items[0].status).toBe('ON_LEAVE');
        expect(page.totalCount).toBe(1);
      });
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('keyword')).toBe('chau');
    expect(request.request.params.get('departmentId')).toBe('d1');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.has('managerId')).toBeFalse();
    request.flush({
      success: true,
      status: 200,
      data: { items: [employee], totalCount: 1, page: 2, pageSize: 20 },
    });
  });

  it('loads one employee by an encoded id', () => {
    service.detail('a/b').subscribe((item) => expect(item.employeeCode).toBe('NV004'));
    const request = http.expectOne(`${url}/a%2Fb`);
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, status: 200, data: employee });
  });

  it('preserves a genuine empty list', () => {
    service.list().subscribe((page) => expect(page.items).toEqual([]));
    http
      .expectOne(url)
      .flush({ success: true, data: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
  });

  it('treats a failed or empty envelope as an error instead of sample data', () => {
    let failures = 0;
    service.detail('x').subscribe({ error: () => failures++ });
    http.expectOne(`${url}/x`).flush({ success: false, status: 404, errorCode: 'NOT_FOUND' });
    service.detail('y').subscribe({ error: () => failures++ });
    http.expectOne(`${url}/y`).flush({ success: true, status: 200, data: null });
    expect(failures).toBe(2);
  });

  it('propagates the HTTP status of a missing employee', () => {
    let status: number | undefined;
    service.detail('missing').subscribe({
      next: () => fail('A 404 must not succeed'),
      error: (error: HttpErrorResponse) => (status = error.status),
    });
    http
      .expectOne(`${url}/missing`)
      .flush(
        { success: false, status: 404, errorCode: 'NOT_FOUND' },
        { status: 404, statusText: 'Not Found' },
      );
    expect(status).toBe(404);
  });
});
