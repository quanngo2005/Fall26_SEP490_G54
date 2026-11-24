import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { RiskSummaryCardComponent } from './risk-summary-card.component';

describe('RiskSummaryCardComponent', () => {
  let fixture: ComponentFixture<RiskSummaryCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RiskSummaryCardComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(RiskSummaryCardComponent);
    fixture.componentRef.setInput('title', 'Risk events');
    fixture.componentRef.setInput('countText', '1 event');
    fixture.componentRef.setInput('events', [
      {
        id: 'r1',
        code: 'R01',
        eventType: 'LATE',
        description: 'Late report',
        severity: 'HIGH',
        penaltyPoints: 2,
        occurredAt: '2026-01-01',
      },
    ]);
    fixture.componentRef.setInput('totalLabel', 'Total deduction');
    fixture.componentRef.setInput('totalPenalty', 2);
    fixture.detectChanges();
  });

  it('renders one row per risk event and the total', () => {
    expect(fixture.debugElement.queryAll(By.css('.risk-row')).length).toBe(1);
    expect(fixture.debugElement.query(By.css('.risk-total'))).toBeTruthy();
  });

  it('hides the count pill when count text is null', () => {
    fixture.componentRef.setInput('countText', null);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.pill-danger'))).toBeNull();
  });
});
