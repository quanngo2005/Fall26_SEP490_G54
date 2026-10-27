import { registerLocaleData } from '@angular/common';
import localeVi from '@angular/common/locales/vi';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { ApplicationConfig, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth.interceptor';

registerLocaleData(localeVi, 'vi');

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(withXhr(), withInterceptors([authInterceptor])),
    { provide: LOCALE_ID, useValue: 'vi-VN' },
  ],
};
