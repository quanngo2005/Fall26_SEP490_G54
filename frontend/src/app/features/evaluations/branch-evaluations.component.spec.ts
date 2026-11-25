import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';
import { BranchEvaluationsComponent } from './branch-evaluations.component';
import { EvaluationsComponent } from './evaluations.component';
import { EvaluationService } from '../../core/evaluation.service';
import { DepartmentEvaluationDashboard } from '../../core/evaluation.models';

const MOCK_BRANCH_DASHBOARD: DepartmentEvaluationDashboard = {
  department: {
    id: 'dept-1',
    code: 'KHCN',
    name: 'Phòng Khách hàng Cá nhân',
    managerName: 'Trần Quốc Bảo',
  },
  period: {
    id: 'period-11',
    code: '2026-M11',
    name: 'Tháng 11/2026',
    startDate: '2026-11-01',
    endDate: '2026-11-30',
    periodType: 'MONTH',
    status: 'OPEN',
    isActive: true,
  },
  comparePeriod: null,
  availablePeriods: [],
  compareOptions: [],
  performance: {
    average: 90.3,
    previousAverage: null,
    changeFromPrevious: null,
    evaluatedEmployeeCount: 4,
    totalEmployeeCount: 6,
    unscoredEmployeeCount: 2,
    pipCount: 0,
    appealCount: 0,
    kpiGroups: [],
    trend: [],
  },
  operations: {
    onTimeCompletedCount: 142,
    inProgressCount: 10,
    overdueCount: 14,
    onTimeRatePercent: 93.4,
    loadPerStaff: 12.7,
  },
  tierDistribution: {
    tierACapPercent: 20,
    tiers: [],
    actionAlerts: [],
  },
  profileGroups: [],
  staffRows: [],
  generatedAt: '2026-11-20T10:00:00Z',
  availableDepartments: [
    { id: 'dept-1', code: 'KHCN', name: 'Phòng Khách hàng Cá nhân' },
    { id: 'dept-2', code: 'KHDN', name: 'Phòng Khách hàng Doanh nghiệp' },
  ],
};

describe('BranchEvaluationsComponent', () => {
  let evaluationService: EvaluationService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BranchEvaluationsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    evaluationService = TestBed.inject(EvaluationService);
  });

  it('renders EvaluationsComponent with isBranchMode set to true', () => {
    const branchSpy = spyOn(evaluationService, 'branchDashboard').and.returnValue(
      of(MOCK_BRANCH_DASHBOARD),
    );

    const fixture = TestBed.createComponent(BranchEvaluationsComponent);
    fixture.detectChanges();

    const evalDebugEl = fixture.debugElement.query(By.directive(EvaluationsComponent));
    expect(evalDebugEl).not.toBeNull();
    const evalInstance = evalDebugEl.componentInstance as EvaluationsComponent;
    expect(evalInstance.isBranchMode()).toBeTrue();
    expect(branchSpy).toHaveBeenCalled();
  });
});
