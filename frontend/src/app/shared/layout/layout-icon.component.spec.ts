import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LayoutIconComponent } from './layout-icon.component';

describe('LayoutIconComponent', () => {
  let fixture: ComponentFixture<LayoutIconComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [LayoutIconComponent] });
    fixture = TestBed.createComponent(LayoutIconComponent);
  });

  function pathElement(): HTMLElement {
    return (fixture.nativeElement as HTMLElement).querySelector('path') as unknown as HTMLElement;
  }

  it('renders the path registered for a known icon name', () => {
    fixture.componentRef.setInput('name', 'bell');
    fixture.detectChanges();
    expect(pathElement().getAttribute('d')).toContain('M13.73 21');
  });

  it('falls back to the dashboard icon for an unknown name', () => {
    fixture.componentRef.setInput('name', 'does-not-exist');
    fixture.detectChanges();
    expect(pathElement().getAttribute('d')).toBe(
      'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
    );
  });

  it('marks the svg as decorative', () => {
    fixture.componentRef.setInput('name', 'dashboard');
    fixture.detectChanges();
    const svg = (fixture.nativeElement as HTMLElement).querySelector('svg');
    expect(svg?.getAttribute('aria-hidden')).toBe('true');
  });
});
