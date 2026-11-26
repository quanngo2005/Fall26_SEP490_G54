import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { MyTargetsService } from './my-targets.service';

describe('MyTargetsService', () => {
  let service: MyTargetsService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/my-targets`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(MyTargetsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('gets the year and unwraps the envelope', () => {
    let result: unknown;
    service.year(2026).subscribe((value) => (result = value));
    const call = http.expectOne(`${url}/2026`);
    expect(call.request.method).toBe('GET');
    call.flush({ success: true, data: { year: 2026 } });
    expect(result).toEqual({ year: 2026 });
  });

  it('confirms the year with an empty POST', () => {
    service.confirm(2026).subscribe();
    const call = http.expectOne(`${url}/2026/confirm`);
    expect(call.request.method).toBe('POST');
    call.flush({ success: true, data: {} });
  });

  it('replies to a feedback thread', () => {
    service.reply('f1', 'ok').subscribe();
    const call = http.expectOne(`${url}/feedback/f1/reply`);
    expect(call.request.body).toEqual({ reply: 'ok' });
    call.flush({ success: true, data: {} });
  });

  it('sends the progress period as a query parameter', () => {
    service.progress(2026, 'Q1' as never).subscribe();
    const call = http.expectOne((r) => r.url === `${url}/2026/progress`);
    expect(call.request.params.get('period')).toBe('Q1');
    call.flush({ success: true, data: {} });
  });
});
