import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../../core/i18n/i18n.service';
import { AllocationCellComponent } from './allocation-cell.component';

describe('AllocationCellComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: I18nService,
          useValue: jasmine.createSpyObj('I18nService', ['formatNumber', 'currentLanguage'], {
            localization: new I18nService().localization,
          }),
        },
      ],
    });
    const i18n = TestBed.inject(I18nService);
    (i18n.formatNumber as jasmine.Spy).and.callFake((v: number) => String(v));
    (i18n.currentLanguage as unknown as jasmine.Spy).and.returnValue('vi');
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

  describe('typed VND hint', () => {
    const type = (fixture: ReturnType<typeof render>, text: string) => {
      const input = fixture.nativeElement.querySelector('.cell-input') as HTMLInputElement;
      input.dispatchEvent(new Event('focus'));
      input.value = text;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      return input;
    };
    const hint = (fixture: ReturnType<typeof render>) =>
      (fixture.nativeElement as HTMLElement).querySelector('.cell-hint')?.textContent?.trim();

    it('shows the typed VND amount in tỷ while typing and hides it on blur', () => {
      const fixture = render({ editable: true, scaleHint: true, value: null });
      const input = type(fixture, '1.500.000.000');
      expect(hint(fixture)).toBe('= 1.5 tỷ');
      input.dispatchEvent(new Event('blur'));
      fixture.detectChanges();
      expect(hint(fixture)).toBeUndefined();
    });

    it('shows triệu for hundreds of millions', () => {
      const fixture = render({ editable: true, scaleHint: true, value: null });
      type(fixture, '500000000');
      expect(hint(fixture)).toBe('= 500 triệu');
    });

    it('shows no hint below 1 triệu or when scaleHint is off', () => {
      const small = render({ editable: true, scaleHint: true, value: null });
      type(small, '999999');
      expect(hint(small)).toBeUndefined();
      const off = render({ editable: true, value: null });
      type(off, '1500000000');
      expect(hint(off)).toBeUndefined();
    });
  });

  it('never renders an editable input when locked', () => {
    const element = render({ editable: true, state: 'LOCKED' }).nativeElement as HTMLElement;
    expect(element.querySelector('.cell-input')).toBeNull();
  });
});
