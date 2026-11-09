import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { EvaluationsComponent } from './evaluations.component';
import { EvaluationFormTargetService } from '../../core/evaluation-form-target.service';
import { EvaluationService } from '../../core/evaluation.service';
import { DepartmentEmployeeEvaluation, EvaluationPeriodOption } from '../../core/evaluation.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

@Component({ selector: 'app-header', standalone: true, template: '' })
class StubHeaderComponent {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class StubSidebarComponent {}

@Component({ selector: 'app-layout-icon', standalone: true, template: '' })
class StubIconComponent {
  readonly name = input<string>();
}

const period: EvaluationPeriodOption = {
  id: 'period-1',
  code: 'Q-TEST',
  name: 'Test quarter',
  startDate: '2026-10-01',
  endDate: '2026-12-31',
  periodType: 'QUARTER',
  status: 'OPEN',
  isActive: true,
};

const monthPeriod: EvaluationPeriodOption = {
  ...period,
  id: 'period-2',
  code: 'M-TEST',
  name: 'Test month',
  startDate: '2026-11-01',
  endDate: '2026-11-30',
  periodType: 'MONTH',
};

function row(overrides: Partial<DepartmentEmployeeEvaluation>): DepartmentEmployeeEvaluation {
  return {
    employeeId: 'e1',
    employeeCode: 'NV0001',
    fullName: 'Nguyễn Văn A',
    jobPositionTitle: 'Sales',
    jobPositionId: 'p1',
    departmentId: 'd1',
    departmentName: 'Phòng Kinh doanh',
    evaluationId: null,
    totalScore: null,
    evaluationStatus: 'NOT_STARTED',
    proposedTier: null,
    calculatedAt: null,
    quantitativeScore: null,
    qualitativeScore: null,
    hasOpenDispute: false,
    pendingSubmissionCount: 0,
    canEvaluate: true,
    updatedAt: null,
    quantitativeScoreChange: null,
    ...overrides,
  };
}

const rows: DepartmentEmployeeEvaluation[] = [
  row({
    employeeId: 'e1',
    employeeCode: 'NV0001',
    evaluationStatus: 'DRAFT',
    quantitativeScore: 99.64,
    qualitativeScore: 85,
    totalScore: 96.71,
    pendingSubmissionCount: 2,
    updatedAt: '2026-10-26T08:00:00+07:00',
  }),
  row({
    employeeId: 'e2',
    employeeCode: 'NV0002',
    fullName: 'Phạm Văn Long',
    evaluationStatus: 'CALCULATED',
    totalScore: 90.48,
    canEvaluate: false,
  }),
  row({
    employeeId: 'e3',
    employeeCode: 'NV0003',
    fullName: 'Đỗ Thị Giang',
    jobPositionTitle: 'Teller',
    jobPositionId: 'p2',
    hasOpenDispute: true,
  }),
];

describe('EvaluationsComponent', () => {
  let component: EvaluationsComponent;
  let fixture: ComponentFixture<EvaluationsComponent>;
  let api: jasmine.SpyObj<EvaluationService>;

  beforeEach(async () => {
    sessionStorage.clear();
    api = jasmine.createSpyObj<EvaluationService>('EvaluationService', [
      'periods',
      'departmentEmployees',
    ]);
    api.periods.and.returnValue(of([period, monthPeriod]));
    api.departmentEmployees.and.returnValue(of(rows));

    await TestBed.configureTestingModule({
      imports: [EvaluationsComponent],
      providers: [provideRouter([]), { provide: EvaluationService, useValue: api }, I18nService],
    })
      .overrideComponent(EvaluationsComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent, LayoutIconComponent] },
        add: { imports: [StubHeaderComponent, StubSidebarComponent, StubIconComponent] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(EvaluationsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => sessionStorage.clear());

  it('loads the periods and then the rows of the active period', () => {
    expect(api.periods).toHaveBeenCalled();
    expect(api.departmentEmployees).toHaveBeenCalledWith('period-1');
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(3);
  });

  it('opens the open quarter that contains today instead of the latest month', () => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 10, 1));
    try {
      const month: EvaluationPeriodOption = {
        ...period,
        id: 'month-12',
        code: '2026-12',
        periodType: 'MONTH',
        startDate: '2026-12-01',
      };
      const november: EvaluationPeriodOption = {
        ...period,
        id: 'month-11',
        code: '2026-11',
        periodType: 'MONTH',
        startDate: '2026-11-01',
        endDate: '2026-11-30',
      };
      const defaultPeriod = (
        component as unknown as {
          defaultPeriod(items: EvaluationPeriodOption[]): EvaluationPeriodOption | undefined;
        }
      ).defaultPeriod([month, november, period]);
      expect(defaultPeriod?.id).toBe('period-1');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('renders page title from localization', () => {
    const title = fixture.nativeElement.querySelector('h1.page-title')?.textContent?.trim();
    expect(title).toBe(TestBed.inject(I18nService).localization.evaluations.pageTitle);
  });

  it('shows the year, the department and the deadline from the API in the context bar', () => {
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.year-select')?.textContent).toContain('2026');
    const info = el.querySelector('.dept-info')?.textContent ?? '';
    expect(info).toContain('Phòng Kinh doanh');
    expect(info).toContain('31/12/2026');
  });

  it('derives the stat cards and the progress from the rows', () => {
    expect(component.statSummary()).toEqual({
      allCount: 3,
      notEvaluatedCount: 1,
      draftCount: 1,
      completedCount: 1,
      attentionCount: 2,
    });
    expect(component.periodInfo().progressPercent).toBe(33);
  });

  it('shows N/A for scores the API has no value for', () => {
    const cells = fixture.nativeElement.querySelectorAll('tbody tr')[2].querySelectorAll('td');
    const emptyValue = TestBed.inject(I18nService).localization.evaluations.emptyValue;
    expect(cells[5].textContent?.trim()).toBe(emptyValue);
    expect(cells[5].textContent).not.toContain('0');
  });

  it('maps NOT_STARTED, DRAFT and later states to the list statuses', () => {
    expect(component.listStatus(rows[0])).toBe('DRAFT');
    expect(component.listStatus(rows[1])).toBe('COMPLETED');
    expect(component.listStatus(rows[2])).toBe('NOT_EVALUATED');
  });

  it('filters items when selecting a stat card', () => {
    component.setStatFilter('DRAFT');
    expect(component.displayedEvaluations().map((i) => i.employeeCode)).toEqual(['NV0001']);

    component.setStatFilter('COMPLETED');
    expect(component.displayedEvaluations().map((i) => i.employeeCode)).toEqual(['NV0002']);

    component.setStatFilter('ATTENTION');
    expect(component.displayedEvaluations().length).toBe(2);
  });

  it('filters items by keyword', () => {
    component.searchKeyword.set('Giang');
    expect(component.displayedEvaluations().map((i) => i.employeeCode)).toEqual(['NV0003']);
  });

  it('reloads the rows when another period is chosen', () => {
    component.onMonthChange({ target: { value: '11' } } as unknown as Event);
    expect(api.departmentEmployees).toHaveBeenCalledWith('period-2');
  });

  it('shows the offline state instead of rows', () => {
    api.departmentEmployees.and.returnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    component.onMonthChange({ target: { value: '11' } } as unknown as Event);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(1);
    expect(fixture.nativeElement.querySelector('tbody td').textContent).toContain(
      TestBed.inject(I18nService).localization.evaluations.offline,
    );
  });

  it('shows the forbidden state', () => {
    api.departmentEmployees.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 403 })),
    );
    component.onMonthChange({ target: { value: '11' } } as unknown as Event);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('tbody td').textContent).toContain(
      TestBed.inject(I18nService).localization.evaluations.forbidden,
    );
  });

  it('shows the empty state for a genuine empty list', () => {
    api.departmentEmployees.and.returnValue(of([]));
    component.onMonthChange({ target: { value: '11' } } as unknown as Event);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('tbody td').textContent).toContain(
      TestBed.inject(I18nService).localization.evaluations.emptyList,
    );
    expect(component.periodInfo().progressPercent).toBeNull();
  });

  it('opens /evaluations/form with the employee and period saved for the tab, not in the address', () => {
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate');
    component.openEvaluationForm(rows[0]);
    expect(TestBed.inject(EvaluationFormTargetService).read()).toEqual({
      employeeId: rows[0].employeeId,
      periodId: 'period-1',
    });
    expect(router.navigate).toHaveBeenCalledOnceWith(['/evaluations/form']);
  });

  it('opens nothing when no period is selected', () => {
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate');
    component.periods.set([]);
    component.openEvaluationForm(rows[0]);
    expect(TestBed.inject(EvaluationFormTargetService).read()).toBeNull();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('links the pending review count to the work of that employee', () => {
    const link = fixture.nativeElement.querySelector('a.badge-warning') as HTMLAnchorElement;
    expect(link).not.toBeNull();
    const url = TestBed.inject(Router).parseUrl(link.getAttribute('href')!);
    expect(url.root.children['primary'].segments.map((segment) => segment.path)).toEqual([
      'my-work',
    ]);
    expect(url.queryParams).toEqual({ employeeCode: 'NV0001' });
  });

  it('shows the number of units when the rows span several departments', () => {
    api.departmentEmployees.and.returnValue(
      of([
        row({ employeeId: 'e1', departmentName: 'Team 1' }),
        row({ employeeId: 'e2', departmentName: 'Team 2' }),
        row({ employeeId: 'e3', departmentName: 'Team 2' }),
      ]),
    );
    component = TestBed.createComponent(EvaluationsComponent).componentInstance;
    expect(component.periodInfo().departmentName).toBe(
      TestBed.inject(I18nService).translate('evaluations.departmentCount', { count: 2 }),
    );
  });

  it('keeps the department name when every row belongs to one department', () => {
    expect(component.periodInfo().departmentName).toBe(rows[0].departmentName);
  });
});
