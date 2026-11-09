import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { HandoverService } from './handover.service';

describe('HandoverService', () => {
  let service: HandoverService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HandoverService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the colleagues a work can be handed over to', () => {
    service.recipients().subscribe((list) => expect(list[0].fullName).toBe('Trần Thị B'));
    const request = http.expectOne(`${environment.apiUrl}/handovers/recipients`);
    expect(request.request.method).toBe('GET');
    request.flush({
      success: true,
      status: 200,
      data: [{ id: 'e2', employeeCode: 'NV002', fullName: 'Trần Thị B', jobPositionTitle: 'GDV' }],
    });
  });

  it('posts the request to the encoded work id', () => {
    const body = { toEmployeeId: 'e2', reason: 'Nghỉ phép', amount: 0 };
    service.create('a b', body).subscribe((handover) => expect(handover.status).toBe('PENDING'));
    const request = http.expectOne(`${environment.apiUrl}/works/a%20b/handovers`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(body);
    request.flush({ success: true, status: 200, data: { id: 'h1', status: 'PENDING' } });
  });

  it('treats a failed or empty envelope as an error', () => {
    let failures = 0;
    service.recipients().subscribe({ error: () => failures++ });
    http
      .expectOne(`${environment.apiUrl}/handovers/recipients`)
      .flush({ success: false, status: 403, errorCode: 'FORBIDDEN' });
    service.create('x', { toEmployeeId: 'e', reason: 'r', amount: 0 }).subscribe({
      error: () => failures++,
    });
    http
      .expectOne(`${environment.apiUrl}/works/x/handovers`)
      .flush({ success: true, status: 200, data: null });
    expect(failures).toBe(2);
  });
});
