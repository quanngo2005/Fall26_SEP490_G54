import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../../core/i18n/i18n.service';
import { targetSharedLocalizationStub } from '../testing/target-shared-i18n.stub';
import { TierStatusLineComponent } from './tier-status-line.component';

describe('TierStatusLineComponent', () => {
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

  const render = () => {
    const fixture = TestBed.createComponent(TierStatusLineComponent);
    fixture.componentRef.setInput('tierLabel', 'Tier 2 - Phòng ban');
    fixture.componentRef.setInput('counts', { matched: 18, total: 24, mismatched: 4, notSplit: 2 });
    fixture.detectChanges();
    return fixture;
  };

  it('shows the tier label and the matched/mismatched/not-split counts', () => {
    const element = render().nativeElement as HTMLElement;
    expect(element.querySelector('.tier-label')?.textContent).toBe('Tier 2 - Phòng ban');
    expect(element.querySelector('.counts')?.textContent).toContain('18 / 24');
    expect(element.querySelector('.counts')?.textContent).toContain('4 lệch');
    expect(element.querySelector('.counts')?.textContent).toContain('2 chưa chia');
  });

  it('emits filterChange when a chip is clicked', () => {
    const fixture = render();
    const filterChange = jasmine.createSpy('filterChange');
    fixture.componentInstance.filterChange.subscribe(filterChange);
    const chips = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
      '.chip',
    );
    chips[1].click();
    expect(filterChange).toHaveBeenCalledWith('MISMATCHED');
  });

  it('marks the active filter chip', () => {
    const fixture = render();
    fixture.componentRef.setInput('activeFilter', 'NOT_SPLIT');
    fixture.detectChanges();
    const chips = (fixture.nativeElement as HTMLElement).querySelectorAll('.chip');
    expect(chips[2].classList).toContain('is-active');
    expect(chips[0].classList).not.toContain('is-active');
  });
});
