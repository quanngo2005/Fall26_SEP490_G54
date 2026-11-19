import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../../core/i18n/i18n.service';
import { targetSharedLocalizationStub } from '../testing/target-shared-i18n.stub';
import { CellLegendComponent } from './cell-legend.component';

describe('CellLegendComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: I18nService,
          useValue: jasmine.createSpyObj('I18nService', [], {
            localization: targetSharedLocalizationStub(),
          }),
        },
      ],
    });
  });

  it('renders the 4 legend entries', () => {
    const fixture = TestBed.createComponent(CellLegendComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('.legend-item').length).toBe(4);
    expect(element.textContent).toContain('Đã gợi ý');
    expect(element.textContent).toContain('Nhập tay');
    expect(element.textContent).toContain('Lệch');
    expect(element.textContent).toContain('Đã khoá');
  });
});
