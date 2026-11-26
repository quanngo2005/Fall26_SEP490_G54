import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { TargetOverviewService } from './target-overview.service';

describe('TargetOverviewService', () => {
  let service: TargetOverviewService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TargetOverviewService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reminds a department manager and encodes both ids in the route', () => {
    let result: unknown;
    service.remindDepartmentManager('b 1', 'd/1').subscribe((value) => (result = value));
    const call = http.expectOne(
      `${environment.apiUrl}/target-assignments/b%201/departments/d%2F1/remind`,
    );
    expect(call.request.method).toBe('POST');
    call.flush({ success: true, data: { departmentId: 'd/1', sentAt: '2026-11-22T00:00:00Z' } });
    expect(result).toEqual({ departmentId: 'd/1', sentAt: '2026-11-22T00:00:00Z' });
  });
});
