import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../../core/i18n/i18n.service';
import { targetSharedLocalizationStub } from '../testing/target-shared-i18n.stub';
import { SuggestionPreviewBannerComponent } from './suggestion-preview-banner.component';

describe('SuggestionPreviewBannerComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: I18nService,
          useValue: jasmine.createSpyObj('I18nService', ['formatNumber'], {
            localization: targetSharedLocalizationStub(),
          }),
        },
      ],
    });
    (TestBed.inject(I18nService).formatNumber as jasmine.Spy).and.callFake((v: number) =>
      String(v),
    );
  });

  const render = (inputs: Record<string, unknown>) => {
    const fixture = TestBed.createComponent(SuggestionPreviewBannerComponent);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
    return fixture;
  };

  it('shows the cell/KPI counts, basis, and rounding method', () => {
    const element = render({
      cellCount: 48,
      kpiCount: 6,
      basis: 'ACTUAL_LAST_YEAR',
      remainderMethod: 'BUFFER_UNIT',
    }).nativeElement as HTMLElement;
    const text = element.querySelector('.banner-text')?.textContent ?? '';
    expect(text).toContain('48');
    expect(text).toContain('6');
    expect(text).toContain('Thực hiện năm trước');
    expect(text).toContain('Dồn vào đơn vị đệm');
  });

  it('emits changeBasis, undo, and apply', () => {
    const fixture = render({ cellCount: 1, kpiCount: 1, basis: 'TARGET_LAST_YEAR' });
    const changeBasis = jasmine.createSpy('changeBasis');
    const undo = jasmine.createSpy('undo');
    const apply = jasmine.createSpy('apply');
    fixture.componentInstance.changeBasis.subscribe(changeBasis);
    fixture.componentInstance.undo.subscribe(undo);
    fixture.componentInstance.apply.subscribe(apply);
    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
      '.banner-actions button',
    );
    buttons[0].click();
    buttons[1].click();
    buttons[2].click();
    expect(changeBasis).toHaveBeenCalled();
    expect(undo).toHaveBeenCalled();
    expect(apply).toHaveBeenCalled();
  });
});
