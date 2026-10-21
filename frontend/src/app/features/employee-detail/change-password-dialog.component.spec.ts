import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { AuthResponse } from '../../core/auth.models';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ChangePasswordDialogComponent } from './change-password-dialog.component';

const session = { accessToken: 'a', refreshToken: 'r' } as AuthResponse;

describe('ChangePasswordDialogComponent', () => {
  const auth = { changePassword: jasmine.createSpy('changePassword') };
  let fixture: ComponentFixture<ChangePasswordDialogComponent>;
  let root: HTMLElement;
  let opener: HTMLButtonElement;
  let changed: jasmine.Spy;
  let closed: jasmine.Spy;

  beforeEach(async () => {
    auth.changePassword.calls.reset();
    await TestBed.configureTestingModule({
      imports: [ChangePasswordDialogComponent],
      providers: [{ provide: AuthService, useValue: auth }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    opener = document.createElement('button');
    document.body.appendChild(opener);
  });

  afterEach(() => opener.remove());

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function open(email = 'chau.le@progest.local'): Promise<void> {
    opener.focus();
    fixture = TestBed.createComponent(ChangePasswordDialogComponent);
    fixture.componentRef.setInput('email', email);
    changed = jasmine.createSpy('changed');
    closed = jasmine.createSpy('closed');
    fixture.componentInstance.changed.subscribe(changed);
    fixture.componentInstance.closed.subscribe(closed);
    root = fixture.nativeElement;
    await settle();
  }

  async function type(selector: string, value: string): Promise<void> {
    const input = root.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  async function fill(current: string, next: string, confirm = next): Promise<void> {
    await type('#current-password', current);
    await type('#new-password', next);
    await type('#confirm-password', confirm);
  }

  const submit = () => root.querySelector<HTMLButtonElement>('button[type="submit"]')!;
  const button = (label: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      b.textContent?.includes(label),
    )!;

  it('is an accessible modal with empty fields and focus on the current password', async () => {
    await open();

    const dialog = root.querySelector('[role="dialog"]')!;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(dialog.getAttribute('aria-labelledby')).toBe('password-title');
    expect(root.querySelector<HTMLInputElement>('#current-password')!.value).toBe('');
    expect(document.activeElement).toBe(root.querySelector('#current-password'));
    expect(submit().disabled).toBeTrue();
  });

  it('enables Change password only when every rule and the confirmation are satisfied', async () => {
    await open();

    await fill('Current-Password-1', 'short1A');
    expect(submit().disabled).toBeTrue();

    await fill('Current-Password-1', 'Brand-New-Password-2', 'Different-Password-2');
    expect(submit().disabled).toBeTrue();
    expect(root.textContent).toContain('Mật khẩu nhập lại không khớp');

    await fill('Current-Password-1', 'Brand-New-Password-2');
    expect(submit().disabled).toBeFalse();
  });

  it('rejects a new password equal to the current one, without digits, or containing the email name', async () => {
    await open();

    await fill('Same-Password-1234', 'Same-Password-1234');
    expect(submit().disabled).toBeTrue();

    await fill('Current-Password-1', 'onlyletterspassword');
    expect(submit().disabled).toBeTrue();

    await fill('Current-Password-1', 'xx-CHAU.LE-secret-99');
    expect(submit().disabled).toBeTrue();
  });

  it('does not reject a password for a very short email name', async () => {
    await open('a@x.test');

    await fill('Current-Password-1', 'Aa-correct-horse-9-battery');

    expect(submit().disabled).toBeFalse();
  });

  it('marks each rule as met or not met for assistive technology', async () => {
    await open();
    await fill('Current-Password-1', 'Brand-New-Password-2');

    const items = Array.from(root.querySelectorAll('.rules li'));
    expect(items.length).toBe(4);
    expect(items.every((item) => item.classList.contains('is-met'))).toBeTrue();
    expect(items.every((item) => item.textContent?.includes('Đã đáp ứng'))).toBeTrue();
  });

  it('toggles password visibility', async () => {
    await open();
    const input = () => root.querySelector<HTMLInputElement>('#new-password')!;
    expect(input().type).toBe('password');

    button('Hiện mật khẩu').click();
    await settle();

    expect(input().type).toBe('text');
    expect(button('Ẩn mật khẩu').getAttribute('aria-pressed')).toBe('true');
  });

  it('changes the password and tells the parent', async () => {
    auth.changePassword.and.returnValue(of(session));
    await open();
    await fill('Current-Password-1', 'Brand-New-Password-2');

    submit().click();
    await settle();

    expect(auth.changePassword).toHaveBeenCalledOnceWith({
      currentPassword: 'Current-Password-1',
      newPassword: 'Brand-New-Password-2',
    });
    expect(changed).toHaveBeenCalledTimes(1);
    expect(closed).not.toHaveBeenCalled();
  });

  it('stays open with the wrong-current-password message and keeps the typed values', async () => {
    auth.changePassword.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({ status: 400, error: { errorCode: 'INVALID_CURRENT_PASSWORD' } }),
      ),
    );
    await open();
    await fill('wrong-Current-1', 'Brand-New-Password-2');

    submit().click();
    await settle();

    expect(changed).not.toHaveBeenCalled();
    expect(root.querySelector('.form-message')!.textContent).toContain(
      'Mật khẩu hiện tại không đúng',
    );
    expect(root.querySelector('.form-message')!.getAttribute('role')).toBe('alert');
    expect(root.querySelector<HTMLInputElement>('#new-password')!.value).toBe(
      'Brand-New-Password-2',
    );
  });

  it('shows the locked message for 403, the policy message for other 400s and a generic one otherwise', async () => {
    await open();
    await fill('Current-Password-1', 'Brand-New-Password-2');

    auth.changePassword.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
    submit().click();
    await settle();
    expect(root.textContent).toContain('khóa tạm thời');

    auth.changePassword.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, error: { errorCode: 'VALIDATION_ERROR' } }),
      ),
    );
    submit().click();
    await settle();
    expect(root.textContent).toContain('chưa đáp ứng yêu cầu');

    auth.changePassword.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    submit().click();
    await settle();
    expect(root.textContent).toContain('Không thể đổi mật khẩu');
  });

  it('closes from Cancel, ×, Escape and the backdrop without changing anything, and returns focus', async () => {
    const closers: Array<() => void> = [
      () => button('Hủy').click(),
      () => root.querySelector<HTMLButtonElement>('.modal-close')!.click(),
      () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })),
      () => root.querySelector<HTMLElement>('.modal-backdrop')!.click(),
    ];

    for (const close of closers) {
      await open();
      await fill('Current-Password-1', 'Brand-New-Password-2');
      close();
      await settle();

      expect(closed).toHaveBeenCalledTimes(1);
      expect(document.activeElement).toBe(opener);
    }
    expect(auth.changePassword).not.toHaveBeenCalled();
  });

  it('discards the typed passwords when it closes', async () => {
    await open();
    await fill('Current-Password-1', 'Brand-New-Password-2');

    button('Hủy').click();
    await settle();

    const instance = fixture.componentInstance as unknown as Record<string, string>;
    expect(instance['currentPassword']).toBe('');
    expect(instance['newPassword']).toBe('');
    expect(instance['confirmPassword']).toBe('');
  });

  it('cannot be closed or submitted twice while the change is in flight', async () => {
    const pending = new Subject<AuthResponse>();
    auth.changePassword.and.returnValue(pending);
    await open();
    await fill('Current-Password-1', 'Brand-New-Password-2');

    submit().click();
    await settle();

    expect(submit().disabled).toBeTrue();
    expect(button('Hủy').disabled).toBeTrue();
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await settle();

    expect(auth.changePassword).toHaveBeenCalledTimes(1);
    expect(closed).not.toHaveBeenCalled();
  });

  it('switches language live', async () => {
    await open();
    TestBed.inject(I18nService).setLanguage('en');
    await settle();

    expect(root.textContent).toContain('Current password');
    expect(root.textContent).not.toContain('Mật khẩu hiện tại');
  });
});
