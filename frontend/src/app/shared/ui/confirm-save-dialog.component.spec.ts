import { TestBed } from '@angular/core/testing';
import { ConfirmSaveDialogComponent } from './confirm-save-dialog.component';

describe('ConfirmSaveDialogComponent', () => {
  function create(busy = false) {
    localStorage.clear();
    const fixture = TestBed.createComponent(ConfirmSaveDialogComponent);
    fixture.componentRef.setInput('heading', 'Xác nhận');
    fixture.componentRef.setInput('meta', '2 thay đổi');
    fixture.componentRef.setInput('warnTitle', 'Cảnh báo');
    fixture.componentRef.setInput('warnBody', 'Nội dung');
    fixture.componentRef.setInput('busy', busy);
    fixture.detectChanges();
    const confirmed = jasmine.createSpy('confirmed');
    const dismissed = jasmine.createSpy('dismissed');
    fixture.componentInstance.confirmed.subscribe(confirmed);
    fixture.componentInstance.dismissed.subscribe(dismissed);
    return { fixture, confirmed, dismissed };
  }

  it('is a labelled modal dialog that emits confirm, back and Escape', () => {
    const { fixture, confirmed, dismissed } = create();
    const root: HTMLElement = fixture.nativeElement;
    const dialog = root.querySelector('[role="dialog"]');
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    expect(root.querySelector('#confirm-save-title')?.textContent).toContain('Xác nhận');
    root.querySelector<HTMLButtonElement>('.primary')?.click();
    expect(confirmed).toHaveBeenCalledTimes(1);
    root.querySelector<HTMLButtonElement>('.secondary')?.click();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(dismissed).toHaveBeenCalledTimes(2);
    fixture.destroy();
  });

  it('cannot be dismissed while saving', () => {
    const { fixture, dismissed } = create(true);
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector<HTMLButtonElement>('.primary')?.disabled).toBeTrue();
    root.querySelector<HTMLButtonElement>('.secondary')?.click();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(dismissed).not.toHaveBeenCalled();
    fixture.destroy();
  });
});
