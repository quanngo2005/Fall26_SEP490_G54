import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MetricCardComponent } from './metric-card.component';

describe('MetricCardComponent', () => {
  let fixture: ComponentFixture<MetricCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [MetricCardComponent] }).compileComponents();
    fixture = TestBed.createComponent(MetricCardComponent);
    fixture.componentRef.setInput('label', 'Total');
    fixture.componentRef.setInput('pillText', '▲ 1,2');
    fixture.componentRef.setInput('pillClass', 'pill-success');
    fixture.detectChanges();
  });

  it('renders the label', () => {
    const title = fixture.debugElement.query(By.css('.metric-title'));
    expect(title.nativeElement.textContent.trim()).toBe('Total');
  });

  it('shows the pill with the given class when pill text is set', () => {
    const pill = fixture.debugElement.query(By.css('.metric-pill'));
    expect(pill.nativeElement.textContent.trim()).toBe('▲ 1,2');
    expect(pill.classes['pill-success']).toBeTrue();
  });

  it('hides the pill when pill text is null', () => {
    fixture.componentRef.setInput('pillText', null);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.metric-pill'))).toBeNull();
  });
});
