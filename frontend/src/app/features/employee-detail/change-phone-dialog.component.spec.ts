import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { PhoneUpdate } from '../../core/profile.models';
import { ProfileService } from '../../core/profile.service';
import { ChangePhoneDialogComponent } from './change-phone-dialog.component';

const saved$ = { phone: '0901000999' } as PhoneUpdate;

describe('ChangePhoneDialogComponent', () => {
  const profiles = { updatePhone: jasmine.createSpy('updatePhone') };
  let fixture: ComponentFixture<ChangePhoneDialogComponent>;
  let root: HTMLElement;
  let opener: HTMLButtonElement;
  let saved: jasmine.Spy;
  let closed: jasmine.Spy;

  beforeEach(async () => {
    profiles.updatePhone.calls.reset();
    await TestBed.configureTestingModule({
      imports: [ChangePhoneDialogComponent],
      providers: [{ provide: ProfileService, useValue: profiles }],
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

  async function open(current: string | null = '0901000004'): Promise<void> {
    opener.focus();
    fixture = TestBed.createComponent(ChangePhoneDialogComponent);
    fixture.componentRef.setInput('current', current);
    saved = jasmine.createSpy('saved');
    closed = jasmine.createSpy('closed');
    fixture.componentInstance.saved.subscribe(saved);
    fixture.componentInstance.closed.subscribe(closed);
    root = fixture.nativeElement;
    await settle();
  }

  async function type(value: string): Promise<void> {
    const input = root.querySelector<HTMLInputElement>('#profile-phone')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  const submit = () => root.querySelector<HTMLButtonElement>('button[type="submit"]')!;
  const cancel = () =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      b.textContent?.includes('Hủy'),
    )!;

  it('is an accessible modal, prefilled with the stored number and focused', async () => {
    await open();

    const dialog = root.querySelector('[role="dialog"]')!;
    const input = root.querySelector<HTMLInputElement>('#profile-phone')!;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(dialog.getAttribute('aria-labelledby')).toBe('phone-title');
    expect(root.querySelector('#phone-title')!.textContent).toContain('Đổi số điện thoại');
    expect(input.value).toBe('0901000004');
    expect(input.maxLength).toBe(10);
    expect(document.activeElement).toBe(input);
  });

  it('starts empty when no number is stored', async () => {
    await open(null);

    expect(root.querySelector<HTMLInputElement>('#profile-phone')!.value).toBe('');
    expect(submit().disabled).toBeTrue();
  });

  it('keeps Save disabled until the number changed and is exactly 10 digits starting with 0', async () => {
    await open();
    expect(submit().disabled).toBeTrue();

    for (const invalid of ['123', '1901000999', '090100099', '+84901000999']) {
      await type(invalid);
      expect(submit().disabled).withContext(invalid).toBeTrue();
      expect(root.textContent).toContain('đúng 10 chữ số');
      expect(root.querySelector('#profile-phone')!.getAttribute('aria-invalid')).toBe('true');
    }

    await type('0901000999');
    expect(submit().disabled).toBeFalse();
    expect(root.querySelector('#profile-phone')!.getAttribute('aria-invalid')).toBe('false');
  });

  it('saves the trimmed number and tells the parent', async () => {
    profiles.updatePhone.and.returnValue(of(saved$));
    await open();

    await type('  0901000999 ');
    submit().click();
    await settle();

    expect(profiles.updatePhone).toHaveBeenCalledOnceWith('0901000999');
    expect(saved).toHaveBeenCalledTimes(1);
    expect(closed).not.toHaveBeenCalled();
  });

  it('clears the number by sending null', async () => {
    profiles.updatePhone.and.returnValue(of({ phone: null }));
    await open();

    await type('   ');
    submit().click();
    await settle();

    expect(profiles.updatePhone).toHaveBeenCalledOnceWith(null);
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it('stays open with the typed number and an error when saving fails', async () => {
    profiles.updatePhone.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
    await open();

    await type('0901000999');
    submit().click();
    await settle();

    expect(saved).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')!.textContent).toContain(
      'Không thể cập nhật số điện thoại',
    );
    expect(root.querySelector<HTMLInputElement>('#profile-phone')!.value).toBe('0901000999');
  });

  it('closes without saving from Cancel, ×, Escape and the backdrop, and returns focus', async () => {
    const closers: Array<() => void> = [
      () => cancel().click(),
      () => root.querySelector<HTMLButtonElement>('.modal-close')!.click(),
      () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })),
      () => root.querySelector<HTMLElement>('.modal-backdrop')!.click(),
    ];

    for (const close of closers) {
      await open();
      await type('0901000999');
      close();
      await settle();

      expect(closed).toHaveBeenCalledTimes(1);
      expect(document.activeElement).toBe(opener);
    }
    expect(profiles.updatePhone).not.toHaveBeenCalled();
  });

  it('emits closed only once when Escape reaches both the dialog and the document', async () => {
    await open();

    root
      .querySelector('[role="dialog"]')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('does not close when the dialog itself is clicked', async () => {
    await open();

    root
      .querySelector('[role="dialog"]')!
      .dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await settle();

    expect(closed).not.toHaveBeenCalled();
  });

  it('cannot be closed or submitted twice while the request is in flight', async () => {
    const pending = new Subject<PhoneUpdate>();
    profiles.updatePhone.and.returnValue(pending);
    await open();
    await type('0901000999');

    submit().click();
    await settle();

    expect(submit().disabled).toBeTrue();
    expect(cancel().disabled).toBeTrue();
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await settle();

    expect(profiles.updatePhone).toHaveBeenCalledTimes(1);
    expect(closed).not.toHaveBeenCalled();
  });

  it('is fully translated and switches language live', async () => {
    await open();
    TestBed.inject(I18nService).setLanguage('en');
    await settle();

    expect(root.textContent).toContain('Change phone number');
    expect(root.textContent).toContain('Exactly 10 digits');
    expect(root.textContent).not.toContain('Đổi số điện thoại');
  });
});
