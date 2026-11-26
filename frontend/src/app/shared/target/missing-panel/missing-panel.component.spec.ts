import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../../core/i18n/i18n.service';
import { MissingPanelComponent } from './missing-panel.component';

describe('MissingPanelComponent', () => {
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
  });

  const render = (inputs: Record<string, unknown>) => {
    const fixture = TestBed.createComponent(MissingPanelComponent);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
    return fixture;
  };

  it('lists each KPI with its missing departments and jumps to the KPI', () => {
    const fixture = render({
      items: [{ kpiId: 'k1', kpiName: 'Huy động vốn', departments: ['Phòng A', 'Phòng B'] }],
    });
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Huy động vốn');
    expect(element.textContent).toContain('Phòng A, Phòng B');

    const selected = jasmine.createSpy('selectKpi');
    fixture.componentInstance.selectKpi.subscribe(selected);
    (element.querySelector('.missing-link') as HTMLButtonElement).click();
    expect(selected).toHaveBeenCalledWith('k1');
  });

  it('offers "set 0" only when allowed and shows other blockers', () => {
    const fixture = render({
      items: [{ kpiId: 'k1', kpiName: 'K', departments: ['A'] }],
      otherBlockers: ['KPI X'],
      canFillZero: true,
    });
    const element = fixture.nativeElement as HTMLElement;
    const filled = jasmine.createSpy('fillZero');
    fixture.componentInstance.fillZero.subscribe(filled);
    (element.querySelector('.missing-actions button') as HTMLButtonElement).click();
    expect(filled).toHaveBeenCalled();
    expect(element.querySelector('.missing-other')?.textContent).toContain('KPI X');
  });
});
