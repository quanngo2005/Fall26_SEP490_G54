import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { PHONE_PATTERN, PhoneUpdate } from './profile.models';
import { ProfileService } from './profile.service';

describe('ProfileService', () => {
  let service: ProfileService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/me/profile`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ProfileService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ProfileService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only the phone number when updating and unwraps the envelope', () => {
    let result: PhoneUpdate | undefined;
    service.updatePhone('0901000999').subscribe((value) => (result = value));

    const request = http.expectOne(url);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ phone: '0901000999' });
    request.flush({ success: true, status: 200, data: { phone: '0901000999' } });

    expect(result?.phone).toBe('0901000999');
  });

  it('sends null to clear the phone number', () => {
    service.updatePhone(null).subscribe();

    const request = http.expectOne(url);
    expect(request.request.body).toEqual({ phone: null });
    request.flush({ success: true, status: 200, data: { phone: null } });
  });

  it('does not turn an unsuccessful envelope into success', () => {
    let failed = false;
    service.updatePhone('0901000999').subscribe({ error: () => (failed = true) });

    http.expectOne(url).flush({ success: false, status: 200, data: null });

    expect(failed).toBeTrue();
  });

  it('surfaces HTTP errors to the caller', () => {
    let status = 0;
    service.updatePhone('12345').subscribe({ error: (error) => (status = error.status) });

    http.expectOne(url).flush({ success: false }, { status: 400, statusText: 'Bad Request' });

    expect(status).toBe(400);
  });
});

describe('PHONE_PATTERN', () => {
  it('accepts exactly 10 digits starting with 0', () => {
    for (const value of ['0901000004', '0123456789', '0000000000']) {
      expect(PHONE_PATTERN.test(value)).withContext(value).toBeTrue();
    }
  });

  it('rejects everything else', () => {
    const rejected = [
      '+84901000004',
      '+84 901 000 004',
      '(028) 3822-1234',
      '0901 000 004',
      '090-100-0004',
      '1901000004',
      '090100000',
      '09010000045',
      '090100000a',
      '0٠٠٠٠٠٠٠٠٠',
      '',
    ];
    for (const value of rejected) {
      expect(PHONE_PATTERN.test(value)).withContext(value).toBeFalse();
    }
  });
});
