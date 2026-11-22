import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PaceBarComponent } from './pace-bar.component';

describe('PaceBarComponent', () => {
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

  const render = (inputs: Record<string, unknown>) => {
    const fixture = TestBed.createComponent(PaceBarComponent);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
    return fixture;
  };

  it('is on track (green) when the gap is >= 0', () => {
    const element = render({ gapPercent: 5 }).nativeElement as HTMLElement;
    expect(element.querySelector('.pace-bar')?.classList).toContain('is-on_track');
    expect(element.textContent).toContain('Đạt nhịp');
  });

  it('is slow when -10 <= gap < 0', () => {
    const element = render({ gapPercent: -4 }).nativeElement as HTMLElement;
    expect(element.querySelector('.pace-bar')?.classList).toContain('is-slow');
    expect(element.textContent).toContain('Chậm');
  });

  it('is at risk when gap < -10', () => {
    const element = render({ gapPercent: -15 }).nativeElement as HTMLElement;
    expect(element.querySelector('.pace-bar')?.classList).toContain('is-at_risk');
    expect(element.textContent).toContain('Nguy cơ');
  });

  it('shows a level instead of a tick for a balance KPI', () => {
    const element = render({ kind: 'BALANCE', balanceLevelPercent: 92 })
      .nativeElement as HTMLElement;
    expect(element.querySelector('.tick')).toBeNull();
    expect(element.textContent).toContain('Mức 92%');
  });

  it('shows an above-threshold check for a ratio KPI', () => {
    const element = render({ kind: 'RATIO', ratioAboveThreshold: true })
      .nativeElement as HTMLElement;
    expect(element.querySelector('.track')).toBeNull();
    expect(element.querySelector('.ratio-status')?.classList).toContain('is-above');
    expect(element.textContent).toContain('Đạt ngưỡng');
  });
});
