import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { Desk, QueueService } from './queue.service';

describe('QueueService', () => {
  let service: QueueService;
  let http: HttpTestingController;
  const root = environment.apiUrl + '/queue-counters';
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(QueueService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('keeps the visit and retry identifiers unchanged when calling the counter', () => {
    const command = {
      action: 'start',
      requestId: 'request-a',
      visitId: 'visit-b',
      workType: 'GIAO_DICH',
    };
    service.command(command).subscribe((state) => expect(state.counter).toBe(2));
    const request = http.expectOne(root + '/commands');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(command);
    request.flush({ success: true, data: { counter: 2, options: [], visit: null } });
  });

  it('sends the observed desk revision and reports a concurrent change', () => {
    const desk: Desk = {
      counter: 4,
      position: 'SALES',
      revision: 2,
      services: ['account'],
      locked: false,
      employee: null,
      state: 'idle',
      number: null,
      service: null,
      calledAt: null,
    };
    let failed = false;
    service.save(desk).subscribe({ error: () => (failed = true) });
    const request = http.expectOne(root + '/lobby/desks/4');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      employee: null,
      services: ['account'],
      locked: false,
      revision: 2,
    });
    request.flush({ errorCode: 'STALE_DESK' }, { status: 409, statusText: 'Conflict' });
    expect(failed).toBeTrue();
  });
});
