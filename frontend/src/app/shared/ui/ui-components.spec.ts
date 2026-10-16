import { Component, signal } from '@angular/core';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { FormControl, Validators } from '@angular/forms';
import { ConfirmDialogComponent } from './confirm-dialog.component';
import { FilterBarComponent } from './filter-bar.component';
import { FormErrorComponent } from './form-error.component';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  it('adds and auto-dismisses toasts', fakeAsync(() => {
    const service = TestBed.inject(ToastService);
    service.success('Saved', 1000);
    expect(service.toasts().map((t) => t.message)).toEqual(['Saved']);
    tick(1000);
    expect(service.toasts()).toEqual([]);
  }));

  it('dismisses a toast manually', () => {
    const service = TestBed.inject(ToastService);
    const id = service.error('Failed', 0);
    service.dismiss(id);
    expect(service.toasts()).toEqual([]);
  });
});

describe('FormErrorComponent', () => {
  it('shows validation messages only after the control is touched', () => {
    const control = new FormControl('', Validators.required);
    const fixture = TestBed.createComponent(FormErrorComponent);
    fixture.componentRef.setInput('control', control);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toBe('');

    control.markAsTouched();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('bắt buộc');
  });

  it('shows server errors and custom messages', () => {
    const control = new FormControl('', Validators.required);
    control.markAsTouched();
    const fixture = TestBed.createComponent(FormErrorComponent);
    fixture.componentRef.setInput('control', control);
    fixture.componentRef.setInput('customMessages', { required: 'Nhập tên.' });
    fixture.componentRef.setInput('serverErrors', ['Tên đã tồn tại.']);
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Nhập tên.');
    expect(text).toContain('Tên đã tồn tại.');
  });
});

describe('ConfirmDialogComponent', () => {
  function create(busy: boolean) {
    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.componentRef.setInput('title', 'Xóa?');
    fixture.componentRef.setInput('message', 'Không thể hoàn tác.');
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('busy', busy);
    fixture.detectChanges();
    return fixture;
  }

  it('emits confirmed and cancelled', () => {
    const fixture = create(false);
    const confirmed = jasmine.createSpy('confirmed');
    const cancelled = jasmine.createSpy('cancelled');
    fixture.componentInstance.confirmed.subscribe(confirmed);
    fixture.componentInstance.cancelled.subscribe(cancelled);

    const buttons = fixture.nativeElement.querySelectorAll('button.btn') as NodeListOf<HTMLElement>;
    buttons[0].click();
    buttons[1].click();
    expect(cancelled).toHaveBeenCalledTimes(1);
    expect(confirmed).toHaveBeenCalledTimes(1);
  });

  it('opens the native dialog modally and disables actions while busy', () => {
    const fixture = create(true);
    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.open).toBeTrue();
    const buttons = fixture.nativeElement.querySelectorAll(
      'button.btn',
    ) as NodeListOf<HTMLButtonElement>;
    expect(buttons[0].disabled).toBeTrue();
    expect(buttons[1].disabled).toBeTrue();
  });
});

@Component({
  imports: [FilterBarComponent],
  template: `
    <app-filter-bar
      [searchValue]="search()"
      [filters]="filters"
      [filterValues]="values()"
      (searchChange)="search.set($event)"
      (filterChange)="setFilter($event.key, $event.value)"
      (filtersReset)="clear()"
    />
  `,
})
class FilterHostComponent {
  readonly search = signal('');
  readonly values = signal<Record<string, string>>({ position: '' });
  readonly filters = [
    {
      key: 'position',
      label: 'Chức danh',
      allLabel: 'Tất cả',
      options: [{ value: 'a', label: 'A' }],
    },
  ];

  setFilter(key: string, value: string): void {
    this.values.set({ [key]: value });
  }

  clear(): void {
    this.search.set('');
    this.values.set({ position: '' });
  }
}

describe('FilterBarComponent', () => {
  it('debounces search, emits filter changes and resets', fakeAsync(() => {
    const fixture = TestBed.createComponent(FilterHostComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const input = el.querySelector('input') as HTMLInputElement;
    input.value = ' an ';
    input.dispatchEvent(new Event('input'));
    expect(fixture.componentInstance.search()).toBe('');
    tick(250);
    expect(fixture.componentInstance.search()).toBe('an');

    const select = el.querySelector('select') as HTMLSelectElement;
    select.value = 'a';
    select.dispatchEvent(new Event('change'));
    expect(fixture.componentInstance.values()['position']).toBe('a');

    fixture.detectChanges();
    (el.querySelector('.reset') as HTMLButtonElement).click();
    expect(fixture.componentInstance.search()).toBe('');
  }));
});
