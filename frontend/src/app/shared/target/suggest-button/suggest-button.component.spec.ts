import { TestBed } from '@angular/core/testing';
import { SuggestButtonComponent } from './suggest-button.component';

describe('SuggestButtonComponent', () => {
  function render(disabled = false) {
    const fixture = TestBed.createComponent(SuggestButtonComponent);
    fixture.componentRef.setInput('label', 'Gợi ý phân bổ');
    fixture.componentRef.setInput('disabled', disabled);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the sparkle icon with the label and emits when pressed', () => {
    const fixture = render();
    const element = fixture.nativeElement as HTMLElement;
    const pressed = jasmine.createSpy('pressed');
    fixture.componentInstance.pressed.subscribe(pressed);

    const button = element.querySelector('button')!;
    expect(button.classList).toContain('button-secondary');
    expect(button.querySelector('app-layout-icon svg')).not.toBeNull();
    expect(button.textContent?.trim()).toBe('Gợi ý phân bổ');
    button.click();

    expect(pressed).toHaveBeenCalledTimes(1);
  });

  it('does not emit while disabled', () => {
    const fixture = render(true);
    const pressed = jasmine.createSpy('pressed');
    fixture.componentInstance.pressed.subscribe(pressed);

    fixture.nativeElement.querySelector('button').click();

    expect(pressed).not.toHaveBeenCalled();
  });
});
