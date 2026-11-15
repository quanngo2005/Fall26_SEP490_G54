import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { SystemParameter } from './system-parameter.models';
import { SystemParameterService } from './system-parameter.service';

describe('SystemParameterService', () => {
  let http: HttpTestingController;
  let service: SystemParameterService;
  const url = `${environment.apiUrl}/system-parameters`;
  const parameter: SystemParameter = {
    key: 'importMaxFileMb',
    value: 8,
    min: 1,
    max: 50,
    locked: false,
    updatedAt: null,
    updatedBy: null,
  };
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(SystemParameterService);
  });
  afterEach(() => http.verify());

  it('GB654_10 loads the parameter list with GET /system-parameters', () => {
    let result: SystemParameter[] = [];
    service.list().subscribe((items) => (result = items));
    const request = http.expectOne(url);
    expect(request.request.method).toBe('GET');
    request.flush(envelope([parameter]));
    expect(result).toEqual([parameter]);
  });

  it('GB654_11 sends only the changes with PUT /system-parameters and returns the updated list', () => {
    let result: SystemParameter[] = [];
    const payload = { changes: [{ key: 'importMaxFileMb' as const, value: 10 }] };
    service.save(payload).subscribe((items) => (result = items));
    const request = http.expectOne(url);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(payload);
    request.flush(envelope([{ ...parameter, value: 10 }]));
    expect(result[0].value).toBe(10);
  });

  it('GB654_12 surfaces a 400 VALIDATION_ERROR instead of faking success', () => {
    let status = 0;
    service.save({ changes: [{ key: 'importMaxFileMb', value: 999 }] }).subscribe({
      error: (error: { status: number }) => (status = error.status),
    });
    http
      .expectOne(url)
      .flush(
        { success: false, status: 400, errorCode: 'VALIDATION_ERROR', data: null },
        { status: 400, statusText: 'Bad Request' },
      );
    expect(status).toBe(400);
  });

  it('GB654_13 surfaces a 403 FORBIDDEN list response', () => {
    let failed = false;
    service.list().subscribe({ error: () => (failed = true) });
    http.expectOne(url).flush({}, { status: 403, statusText: 'Forbidden' });
    expect(failed).toBeTrue();
  });

  it('rejects an unsuccessful or empty envelope', () => {
    let failed = false;
    service.list().subscribe({ error: () => (failed = true) });
    http.expectOne(url).flush({ success: false, status: 200, data: null, message: 'x' });
    expect(failed).toBeTrue();
  });
});
