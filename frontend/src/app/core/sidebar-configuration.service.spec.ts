import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { SidebarConfiguration, SidebarConfigurationService } from './sidebar-configuration.service';

describe('SidebarConfigurationService', () => {
  let http: HttpTestingController;
  let service: SidebarConfigurationService;
  const url = `${environment.apiUrl}/sidebar-configurations`;
  const config: SidebarConfiguration = {
    role: 'employee',
    itemIds: ['overview', 'myProfile'],
    version: 2,
    routes: { myProfile: '/employee-detail' },
  };
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(SidebarConfigurationService);
  });
  afterEach(() => http.verify());
  it('reads current configuration and preserves an empty result', () => {
    service.current().subscribe((value) => expect(value).toEqual([]));
    const request = http.expectOne(`${url}/current`);
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, status: 200, data: [] });
  });
  it('reads all configurations for the admin editor', () => {
    service.all().subscribe((value) => expect(value).toEqual([config]));
    const request = http.expectOne(url);
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, status: 200, data: [config] });
  });
  it('sends ordered identifiers and concurrency version when saving', () => {
    service.save(config).subscribe((value) => expect(value.version).toBe(3));
    const request = http.expectOne(`${url}/employee`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      itemIds: config.itemIds,
      version: 2,
      routes: config.routes,
    });
    request.flush({ success: true, status: 200, data: { ...config, version: 3 } });
  });
  it('preserves failed saves and invalid envelopes as errors', () => {
    service.save(config).subscribe({
      next: () => fail('Unexpected save success'),
      error: (error) => expect(error.status).toBe(409),
    });
    http.expectOne(`${url}/employee`).flush({}, { status: 409, statusText: 'Conflict' });
    service.current().subscribe({
      next: () => fail('Unexpected fallback'),
      error: (error) => expect(error).toBeTruthy(),
    });
    http.expectOne(`${url}/current`).flush({ success: true, status: 200, data: null });
  });
});
