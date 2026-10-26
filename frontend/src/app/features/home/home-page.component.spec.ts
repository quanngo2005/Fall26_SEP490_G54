import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth.service';
import { AuthenticatedUser } from '../../core/auth.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { HomePageComponent } from './home-page.component';

@Component({ selector: 'app-header', template: '' })
class StubHeaderComponent {}

@Component({ selector: 'app-sidebar', template: '' })
class StubSidebarComponent {}

describe('HomePageComponent', () => {
  const user = signal<AuthenticatedUser | null>(null);
  let fixture: ComponentFixture<HomePageComponent>;

  beforeEach(() => {
    user.set(null);
    TestBed.configureTestingModule({
      imports: [HomePageComponent],
      providers: [{ provide: AuthService, useValue: { currentUser: user.asReadonly() } }],
    });
    TestBed.overrideComponent(HomePageComponent, {
      remove: { imports: [HeaderComponent, SidebarComponent] },
      add: { imports: [StubHeaderComponent, StubSidebarComponent] },
    });
    fixture = TestBed.createComponent(HomePageComponent);
  });

  function heading(): string {
    return (fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent ?? '';
  }

  it('greets the signed-in user by full name', () => {
    user.set({
      id: 'id',
      email: 'user@example.test',
      fullName: 'Nguyen Van A',
      roles: [],
      permissions: [],
    });
    fixture.detectChanges();
    expect(heading()).toContain('Nguyen Van A');
  });

  it('falls back to the localized placeholder name when no user is signed in', () => {
    fixture.detectChanges();
    const fallback = TestBed.inject(I18nService).localization.home.fallbackName;
    expect(heading()).toContain(fallback);
  });
});
