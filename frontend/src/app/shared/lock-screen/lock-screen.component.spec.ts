import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AuthenticatedUser } from '../../core/auth.models';
import { IdleTimeoutService } from '../../core/idle-timeout.service';
import { LockScreenComponent } from './lock-screen.component';

describe('LockScreenComponent', () => {
  let component: LockScreenComponent;
  let fixture: ComponentFixture<LockScreenComponent>;

  const mockUser: AuthenticatedUser = {
    id: 'user-001',
    email: 'officer@bank.vn',
    fullName: 'Nguyen Van Officer',
    roles: ['officer'],
    permissions: ['view'],
  };

  const isLockedSignal = signal(false);
  const isWarningSignal = signal(false);
  const isSensitiveSignal = signal(false);
  const remainingSecondsSignal = signal(30);
  const currentUserSignal = signal<AuthenticatedUser | null>(null);

  const mockIdleService = {
    isLocked: isLockedSignal.asReadonly(),
    isWarning: isWarningSignal.asReadonly(),
    isSensitive: isSensitiveSignal.asReadonly(),
    remainingSeconds: remainingSecondsSignal.asReadonly(),
    currentUser: currentUserSignal.asReadonly(),
    stayLoggedIn: jasmine.createSpy('stayLoggedIn'),
    lock: jasmine.createSpy('lock'),
    unlock: jasmine.createSpy('unlock').and.returnValue(of(true)),
    logout: jasmine.createSpy('logout'),
  };

  beforeEach(async () => {
    isLockedSignal.set(false);
    isWarningSignal.set(false);
    isSensitiveSignal.set(false);
    remainingSecondsSignal.set(30);
    currentUserSignal.set(null);
    mockIdleService.stayLoggedIn.calls.reset();
    mockIdleService.lock.calls.reset();
    mockIdleService.unlock.calls.reset();
    mockIdleService.unlock.and.returnValue(of(true));
    mockIdleService.logout.calls.reset();

    await TestBed.configureTestingModule({
      imports: [LockScreenComponent],
      providers: [{ provide: IdleTimeoutService, useValue: mockIdleService }],
    }).compileComponents();

    fixture = TestBed.createComponent(LockScreenComponent);
    component = fixture.componentInstance;
  });

  it('renders nothing when not warned and not locked', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.idle-warning-overlay')).toBeNull();
    expect(fixture.nativeElement.querySelector('.lock-screen-overlay')).toBeNull();
  });

  it('renders warning overlay when isWarning is true', () => {
    isWarningSignal.set(true);
    fixture.detectChanges();

    const warningOverlay = fixture.nativeElement.querySelector('.idle-warning-overlay');
    expect(warningOverlay).not.toBeNull();
    expect(warningOverlay.textContent).toContain(component.localization.sessionLock.warningTitle);
  });

  it('calls stayLoggedIn when clicking stay logged in button', () => {
    isWarningSignal.set(true);
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('.btn-primary') as HTMLButtonElement;
    btn.click();

    expect(mockIdleService.stayLoggedIn).toHaveBeenCalled();
  });

  it('calls lockNow when clicking lock now button', () => {
    isWarningSignal.set(true);
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('.btn-secondary') as HTMLButtonElement;
    btn.click();

    expect(mockIdleService.lock).toHaveBeenCalled();
  });

  it('renders lock screen overlay when isLocked is true', () => {
    isLockedSignal.set(true);
    currentUserSignal.set(mockUser);
    fixture.detectChanges();

    const lockOverlay = fixture.nativeElement.querySelector('.lock-screen-overlay');
    expect(lockOverlay).not.toBeNull();
    expect(lockOverlay.textContent).toContain(component.localization.sessionLock.lockedTitle);
    expect(lockOverlay.textContent).toContain('Nguyen Van Officer');
    expect(lockOverlay.textContent).toContain('officer@bank.vn');
  });

  it('shows validation error when attempting to unlock with empty password', () => {
    isLockedSignal.set(true);
    fixture.detectChanges();

    component.password.set('   ');
    component.unlock();
    fixture.detectChanges();

    expect(component.errorMessage()).toBe(component.localization.sessionLock.passwordError);
    const errorEl = fixture.nativeElement.querySelector('.error-text');
    expect(errorEl).not.toBeNull();
    expect(errorEl.textContent).toContain(component.localization.sessionLock.passwordError);
  });

  it('calls idleService.unlock and resets state on success', () => {
    isLockedSignal.set(true);
    fixture.detectChanges();

    component.password.set('ValidPassword123');
    component.unlock();
    fixture.detectChanges();

    expect(mockIdleService.unlock).toHaveBeenCalledWith('ValidPassword123');
    expect(component.password()).toBe('');
    expect(component.errorMessage()).toBeNull();
    expect(component.isSubmitting()).toBeFalse();
  });

  it('displays error message when unlock fails', () => {
    isLockedSignal.set(true);
    mockIdleService.unlock.and.returnValue(throwError(() => new Error('Invalid credentials')));
    fixture.detectChanges();

    component.password.set('BadPassword');
    component.unlock();
    fixture.detectChanges();

    expect(component.errorMessage()).toBe(component.localization.sessionLock.invalidPassword);
    expect(component.isSubmitting()).toBeFalse();
  });

  it('calls idleService.logout when clicking logout button', () => {
    isLockedSignal.set(true);
    fixture.detectChanges();

    const logoutBtn = fixture.nativeElement.querySelector('.btn-logout') as HTMLButtonElement;
    logoutBtn.click();

    expect(mockIdleService.logout).toHaveBeenCalled();
  });
});
