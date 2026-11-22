import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../../core/i18n/i18n.service';
import { AllocationCellComponent } from './allocation-cell.component';

describe('AllocationCellComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: I18nService,
          useValue: jasmine.createSpyObj('I18nService', ['formatNumber'], {
            localization: new I18nService().localization,
          }),
        },
      ],
    });
    (TestBed.inject(I18nService).formatNumber as jasmine.Spy).and.callFake((v: number) =>
      String(v),
    );
  });

  const render = (inputs: Record<string, unknown> = {}) => {
    const fixture = TestBed.createComponent(AllocationCellComponent);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
    return fixture;
  };

  it('shows an em dash when the value is null', () => {
    const element = render().nativeElement as HTMLElement;
    expect(element.querySelector('.cell-empty')?.textContent).toContain('—');
  });

  it('applies the suggested/manual/mismatch/locked classes', () => {
    expect(
      (render({ state: 'SUGGESTED' }).nativeElement as HTMLElement).querySelector('.is-suggested'),
    ).not.toBeNull();
    expect(
      (render({ state: 'MANUAL' }).nativeElement as HTMLElement).querySelector('.is-manual'),
    ).not.toBeNull();
    expect(
      (render({ state: 'MISMATCH' }).nativeElement as HTMLElement).querySelector('.is-mismatch'),
    ).not.toBeNull();
    expect(
      (render({ state: 'LOCKED' }).nativeElement as HTMLElement).querySelector('.is-locked'),
    ).not.toBeNull();
  });

  it('shows a reason tooltip when locked, falling back to the default text', () => {
    const element = render({ state: 'LOCKED' }).nativeElement as HTMLElement;
    expect(element.querySelector('.cell')?.getAttribute('title')).toBe(
      'Ô đã khoá hoặc không áp dụng cho dòng này.',
    );
    const withReason = render({ state: 'LOCKED', lockedReason: 'Khoá bởi SA' })
      .nativeElement as HTMLElement;
    expect(withReason.querySelector('.cell')?.getAttribute('title')).toBe('Khoá bởi SA');
  });

  it('emits the parsed value on blur when editable', () => {
    const fixture = render({ editable: true, value: 10 });
    const valueChange = jasmine.createSpy('valueChange');
    fixture.componentInstance.valueChange.subscribe(valueChange);
    const input = fixture.nativeElement.querySelector('.cell-input') as HTMLInputElement;
    input.value = '25';
    input.dispatchEvent(new Event('blur'));
    expect(valueChange).toHaveBeenCalledWith(25);
  });

  it('parses dot-grouped input and a comma decimal', () => {
    const fixture = render({ editable: true, value: 10 });
    const valueChange = jasmine.createSpy('valueChange');
    fixture.componentInstance.valueChange.subscribe(valueChange);
    const input = fixture.nativeElement.querySelector('.cell-input') as HTMLInputElement;
    input.value = '1.000.000.000';
    input.dispatchEvent(new Event('blur'));
    expect(valueChange).toHaveBeenCalledWith(1_000_000_000);
    input.value = '2,5';
    input.dispatchEvent(new Event('blur'));
    expect(valueChange).toHaveBeenCalledWith(2.5);
  });

  it('never renders an editable input when locked', () => {
    const element = render({ editable: true, state: 'LOCKED' }).nativeElement as HTMLElement;
    expect(element.querySelector('.cell-input')).toBeNull();
  });
});
