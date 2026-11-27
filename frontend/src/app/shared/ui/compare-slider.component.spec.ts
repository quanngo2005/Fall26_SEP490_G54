import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CompareSliderComponent } from './compare-slider.component';

describe('CompareSliderComponent', () => {
  let fixture: ComponentFixture<CompareSliderComponent>;
  let component: CompareSliderComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompareSliderComponent],
    });
    fixture = TestBed.createComponent(CompareSliderComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders both slider buttons with default labels', () => {
    const buttons = fixture.nativeElement.querySelectorAll('.compare-slider-btn');
    expect(buttons.length).toBe(2);
    expect(buttons[0].textContent.trim()).toBe('Kỳ trước');
    expect(buttons[1].textContent.trim()).toBe('Kỳ này năm trước');
  });

  it('marks PREVIOUS active by default', () => {
    const buttons = fixture.nativeElement.querySelectorAll('.compare-slider-btn');
    expect(buttons[0].classList.contains('active')).toBeTrue();
    expect(buttons[1].classList.contains('active')).toBeFalse();
  });

  it('emits modeChange when clicking SAME_LAST_YEAR', () => {
    let emittedMode: string | null = null;
    component.modeChange.subscribe((m) => (emittedMode = m));

    const buttons = fixture.nativeElement.querySelectorAll('.compare-slider-btn');
    buttons[1].click();
    expect<string | null>(emittedMode).toBe('SAME_LAST_YEAR');
  });

  it('does not emit modeChange when clicking the already active mode', () => {
    let emitted = false;
    component.modeChange.subscribe(() => (emitted = true));

    const buttons = fixture.nativeElement.querySelectorAll('.compare-slider-btn');
    buttons[0].click();
    expect(emitted).toBeFalse();
  });

  it('disables SAME_LAST_YEAR button when hasSameLastYear is false', () => {
    fixture.componentRef.setInput('hasSameLastYear', false);
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('.compare-slider-btn');
    expect(buttons[1].disabled).toBeTrue();

    let emitted = false;
    component.modeChange.subscribe(() => (emitted = true));
    buttons[1].click();
    expect(emitted).toBeFalse();
  });

  it('disables entire slider when disabled input is true', () => {
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();

    const root = fixture.nativeElement.querySelector('.compare-slider-root');
    expect(root.classList.contains('is-disabled')).toBeTrue();

    let emitted = false;
    component.modeChange.subscribe(() => (emitted = true));
    const buttons = fixture.nativeElement.querySelectorAll('.compare-slider-btn');
    buttons[1].click();
    expect(emitted).toBeFalse();
  });
});
