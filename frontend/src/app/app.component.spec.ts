import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { AppComponent } from './app.component';
import { AuthService } from './core/auth.service';
import { AuthenticatedUser } from './core/auth.models';

describe('AppComponent', () => {
  function create(user: Partial<AuthenticatedUser> | null) {
    const currentUser = signal(
      user && ({ id: 'u1', email: 'u@test.local', fullName: 'U', roles: [], permissions: [], ...user } as AuthenticatedUser),
    );
    const fixture = TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: {
            currentUser,
            mustChangePassword: () => currentUser()?.mustChangePassword === true,
          },
        },
      ],
    }).createComponent(AppComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the routed page outlet', () => {
    const fixture = create(null);

    expect(fixture.nativeElement.querySelector('router-outlet')).not.toBeNull();
  });

  it('shows no forced password popup for a normal session', () => {
    const fixture = create({ mustChangePassword: false });

    expect(fixture.nativeElement.querySelector('app-change-password-dialog')).toBeNull();
  });

  it('blocks the app behind a popup that cannot be dismissed while a one-time password is in use', () => {
    const fixture = create({ mustChangePassword: true });
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('app-change-password-dialog')).not.toBeNull();
    expect(el.querySelector('.modal-close')).toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    (el.querySelector('.modal-backdrop') as HTMLElement).click();
    fixture.detectChanges();
    expect(el.querySelector('app-change-password-dialog')).not.toBeNull();
  });
});
