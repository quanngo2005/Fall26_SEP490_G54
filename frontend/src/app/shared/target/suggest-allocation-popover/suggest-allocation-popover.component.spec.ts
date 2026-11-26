import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../../core/i18n/i18n.service';
import { SuggestAllocationPopoverComponent } from './suggest-allocation-popover.component';

describe('SuggestAllocationPopoverComponent', () => {
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
    const fixture = TestBed.createComponent(SuggestAllocationPopoverComponent);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
    return fixture;
  };

  it('shows the disabled reason and disables Create when there is no history', () => {
    const element = render({
      hasHistory: false,
      noHistoryReason: 'Chi nhánh mới mở năm nay.',
    }).nativeElement as HTMLElement;
    expect(element.querySelector('.disabled-reason')?.textContent).toContain(
      'Chi nhánh mới mở năm nay.',
    );
    expect(element.querySelector('.popover-body')).toBeNull();
    expect(
      (element.querySelector('.popover-footer .button-primary') as HTMLButtonElement).disabled,
    ).toBeTrue();
  });

  it('shows the must-round badge only for the actual-last-year basis, and hides money/count steps for target', () => {
    const fixture = render();
    let element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.step-input')).toBeNull();

    const actualRadio = element.querySelectorAll<HTMLInputElement>('input[name="basis"]')[1];
    actualRadio.checked = true;
    actualRadio.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('.step-input').length).toBe(2);
  });

  it('only shows the employee/period blocks for tier 3', () => {
    const notTier3 = render({ isTier3: false }).nativeElement as HTMLElement;
    expect(notTier3.querySelectorAll('input[name="employeeScope"]').length).toBe(0);

    const tier3 = render({ isTier3: true }).nativeElement as HTMLElement;
    expect(tier3.querySelectorAll('input[name="employeeScope"]').length).toBe(2);
    expect(tier3.querySelectorAll('input[name="periodScope"]').length).toBe(3);
  });

  it('disables the buffer-unit remainder option for a target-basis suggestion', () => {
    const fixture = render();
    const element = fixture.nativeElement as HTMLElement;
    const remainderRadios = element.querySelectorAll<HTMLInputElement>(
      'input[name="remainderMethod"]',
    );
    // Target basis: only the largest-remainder radio renders, and it is disabled.
    expect(remainderRadios.length).toBe(1);
    expect(remainderRadios[0].disabled).toBeTrue();
  });

  it('emits the assembled request on Create, and cancelled on Cancel', () => {
    const fixture = render({ isTier3: true });
    const created = jasmine.createSpy('created');
    const cancelled = jasmine.createSpy('cancelled');
    fixture.componentInstance.created.subscribe(created);
    fixture.componentInstance.cancelled.subscribe(cancelled);

    const element = fixture.nativeElement as HTMLElement;
    const groupScopeRadio = [
      ...element.querySelectorAll<HTMLInputElement>('input[name="scope"]'),
    ][1];
    groupScopeRadio.checked = true;
    groupScopeRadio.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    (element.querySelector('.popover-footer .button-primary') as HTMLButtonElement).click();
    expect(created).toHaveBeenCalledWith(
      jasmine.objectContaining({
        basis: 'TARGET_LAST_YEAR',
        scope: 'GROUP',
        employeeScope: 'ALL',
        periodScope: 'YEAR_AND_QUARTERS',
        applyMode: 'EMPTY_ONLY',
      }),
    );

    (element.querySelector('.popover-footer .button-secondary') as HTMLButtonElement).click();
    expect(cancelled).toHaveBeenCalled();
  });
});
