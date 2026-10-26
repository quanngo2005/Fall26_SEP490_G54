import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { LoginPageComponent } from '../features/auth/login-page.component';

describe('KPI route authentication', () => {
  it('redirects a logged-out visitor to login before requesting KPI data', async () => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/kpi-definitions', LoginPageComponent);
    expect(TestBed.inject(Router).url).toBe('/login');
    const http = TestBed.inject(HttpTestingController);
    http.expectNone((request) => request.url.includes('/kpi-definitions'));
    http.verify();
  });
});
