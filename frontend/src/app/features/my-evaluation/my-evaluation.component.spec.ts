import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { MyEvaluationComponent } from './my-evaluation.component';
import { AuthService } from '../../core/auth.service';
import { signal } from '@angular/core';
import { AuthenticatedUser } from '../../core/auth.models';
import { By } from '@angular/platform-browser';

describe('MyEvaluationComponent', () => {
  let component: MyEvaluationComponent;
  let fixture: ComponentFixture<MyEvaluationComponent>;
  let router: Router;

  const mockUser = signal<AuthenticatedUser | null>({
    id: 'emp-101',
    email: 'khangdm@bank.example',
    fullName: 'Đỗ Minh Khang',
    roles: ['EMPLOYEE', 'SALES_OFFICER'],
    permissions: ['EVALUATION_READ', 'WORK_READ'],
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyEvaluationComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser: mockUser.asReadonly(),
          },
        },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));

    fixture = TestBed.createComponent(MyEvaluationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders page header and published status badge', () => {
    const titleEl = fixture.debugElement.query(By.css('.eval-title'));
    expect(titleEl).toBeTruthy();
    expect(titleEl.nativeElement.textContent).toContain('2026');

    const badgeEl = fixture.debugElement.query(By.css('.badge-published'));
    expect(badgeEl).toBeTruthy();
    expect(badgeEl.nativeElement.textContent).toContain('Published');
  });

  it('renders 3 summary metric cards with initial Q2 values', () => {
    const heroScore = fixture.debugElement.query(By.css('.card-hero-score'));
    expect(heroScore).toBeTruthy();
    expect(heroScore.nativeElement.textContent.trim()).toBe('82,0');

    const heroTier = fixture.debugElement.query(By.css('.tier-hero-pill'));
    expect(heroTier.nativeElement.textContent.trim()).toBe('A2');

    const activeTierPill = fixture.debugElement.query(By.css('.tier-pill-active'));
    expect(activeTierPill.nativeElement.textContent.trim()).toBe('A2');

    const rankingVal = fixture.debugElement.query(By.css('.ranking-hero-val'));
    expect(rankingVal.nativeElement.textContent.trim()).toBe('Top 40%');
  });

  it('renders all KPI criteria table rows and modifiers', () => {
    const rows = fixture.debugElement.queryAll(By.css('.criteria-row'));
    expect(rows.length).toBe(4);

    const firstRowCriteria = rows[0].query(By.css('.criteria-link-btn'));
    expect(firstRowCriteria.nativeElement.textContent).toContain('Sales – Tiền gửi – xử lý');

    const modifierRows = fixture.debugElement.queryAll(By.css('.modifier-row'));
    expect(modifierRows.length).toBe(2);

    const totalRow = fixture.debugElement.query(By.css('.total-row .total-score'));
    expect(totalRow.nativeElement.textContent.trim()).toBe('82,0');
  });

  it('switches evaluation period when selecting from dropdown', () => {
    component.onPeriodChange('2026-Q1');
    fixture.detectChanges();

    const heroScore = fixture.debugElement.query(By.css('.card-hero-score'));
    expect(heroScore.nativeElement.textContent.trim()).toBe('78,5');

    const heroTier = fixture.debugElement.query(By.css('.tier-hero-pill'));
    expect(heroTier.nativeElement.textContent.trim()).toBe('B');
  });

  it('opens acknowledge modal and confirms acknowledgment (UC108)', () => {
    const ackBtn = fixture.debugElement.query(By.css('.ack-btn'));
    expect(ackBtn).toBeTruthy();

    ackBtn.nativeElement.click();
    fixture.detectChanges();

    const dialog = fixture.debugElement.query(By.css('.ack-dialog'));
    expect(dialog).toBeTruthy();

    // Confirm acknowledgment
    component.confirmAcknowledge();
    fixture.detectChanges();

    expect(component['evaluation']().isAcknowledged).toBeTrue();
    expect(fixture.debugElement.query(By.css('.alert-success'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.badge-acknowledged'))).toBeTruthy();
  });

  it('cancels acknowledge modal', () => {
    component.openAcknowledgeModal();
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.ack-dialog'))).toBeTruthy();

    component.closeAcknowledgeModal();
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.ack-dialog'))).toBeFalsy();
  });

  it('navigates to appeals when clicking appeal button', () => {
    const appealBtn = fixture.debugElement.query(By.css('.appeal-btn'));
    appealBtn.nativeElement.click();

    expect(router.navigate).toHaveBeenCalledWith(['/appeals/mine']);
  });

  it('opens and closes drilldown ledger modal for clickable KPI rows', () => {
    const depositCrit = component['evaluation']().criteria[0];
    component.openDrilldown(depositCrit);
    fixture.detectChanges();

    const ledgerDialog = fixture.debugElement.query(By.css('.ledger-dialog'));
    expect(ledgerDialog).toBeTruthy();
    expect(component['ledgerItems']().length).toBeGreaterThan(0);

    // Escape closes drilldown
    component.onEscape();
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.ledger-dialog'))).toBeFalsy();
  });

  it('does not open drilldown for qualitative criteria', () => {
    const qualCrit = component['evaluation']().criteria[3];
    expect(qualCrit.hasDrilldown).toBeFalse();

    component.openDrilldown(qualCrit);
    expect(component['selectedDrilldownCriteria']()).toBeNull();
  });
});
