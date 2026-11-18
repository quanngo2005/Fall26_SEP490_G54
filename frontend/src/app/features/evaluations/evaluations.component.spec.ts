import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { EvaluationsComponent } from './evaluations.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { I18nService } from '../../core/i18n/i18n.service';
import { DepartmentEvaluationDashboard } from '../../core/evaluation.models';
import { environment } from '../../../environments/environment';

@Component({ selector: 'app-header', standalone: true, template: '' })
class StubHeaderComponent {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class StubSidebarComponent {}

@Component({ selector: 'app-layout-icon', standalone: true, template: '' })
class StubIconComponent {
  readonly name = input<string>();
}

const MOCK_DASHBOARD: DepartmentEvaluationDashboard = {
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
  comparePeriod: {
    id: 'period-10',
    code: '2026-M10',
    name: 'Tháng 10/2026',
    startDate: '2026-10-01',
    endDate: '2026-10-31',
    periodType: 'MONTH',
    status: 'LOCKED',
    isActive: false,
  },
  availablePeriods: [
    {
      id: 'period-11',
      code: '2026-M11',
      name: 'Tháng 11/2026',
      startDate: '2026-11-01',
      endDate: '2026-11-30',
      periodType: 'MONTH',
      status: 'OPEN',
      isActive: true,
    },
    {
      id: 'period-10',
      code: '2026-M10',
      name: 'Tháng 10/2026',
      startDate: '2026-10-01',
      endDate: '2026-10-31',
      periodType: 'MONTH',
      status: 'LOCKED',
      isActive: false,
    },
  ],
  compareOptions: [
    {
      id: 'period-10',
      code: '2026-M10',
      name: 'Tháng 10/2026',
      startDate: '2026-10-01',
      endDate: '2026-10-31',
      periodType: 'MONTH',
      status: 'LOCKED',
      isActive: false,
    },
  ],
  performance: {
    average: 90.3,
    previousAverage: 89.3,
    changeFromPrevious: 1.0,
    evaluatedEmployeeCount: 4,
    totalEmployeeCount: 6,
    unscoredEmployeeCount: 2,
    pipCount: 1,
    appealCount: 1,
    kpiGroups: [
      {
        name: 'Kinh doanh',
        displayOrder: 1,
        weightPercent: 70,
        averageScore: 92.4,
        kpis: [
          {
            kpiDefinitionId: 'kpi-1',
            code: 'KPI-01',
            name: 'Huy động tiền gửi tiết kiệm',
            unit: 'tỷ',
            direction: 'HIGHER_BETTER',
            targetValue: 15,
            actualValue: 11.4,
            achievementPercent: 76,
            previousAchievementPercent: 84,
            changeFromPreviousPercent: -8,
          },
          {
            kpiDefinitionId: 'kpi-2',
            code: 'KPI-02',
            name: 'Doanh số giải ngân cho vay',
            unit: 'tỷ',
            direction: 'HIGHER_BETTER',
            targetValue: 20,
            actualValue: 21.2,
            achievementPercent: 106,
            previousAchievementPercent: 101,
            changeFromPreviousPercent: 5,
          },
        ],
      },
    ],
    trend: [
      { periodId: 'period-10', periodCode: '10', periodName: 'Tháng 10/2026', average: 89.3 },
      { periodId: 'period-11', periodCode: '11', periodName: 'Tháng 11/2026', average: 90.3 },
    ],
  },
  operations: {
    onTimeCompletedCount: 142,
    overdueCount: 6,
    inProgressCount: 18,
    onTimeRatePercent: 95.9,
    loadPerStaff: 3.0,
  },
  tierDistribution: {
    tierACapPercent: 20,
    tiers: [
      { tierCode: 'A1', rankOrder: 1, minScore: 95, maxScore: null, count: 1, percent: 25 },
      { tierCode: 'A2', rankOrder: 2, minScore: 90, maxScore: 95, count: 1, percent: 25 },
      { tierCode: 'A3', rankOrder: 3, minScore: 85, maxScore: 90, count: 0, percent: 0 },
      { tierCode: 'B', rankOrder: 4, minScore: 70, maxScore: 85, count: 1, percent: 25 },
      { tierCode: 'C', rankOrder: 5, minScore: 0, maxScore: 70, count: 1, percent: 25 },
    ],
    actionAlerts: [
      {
        kind: 'PIP',
        employeeId: 'emp-7',
        employeeCode: 'NV007',
        fullName: 'Ngô Thanh Mai',
        tierCode: 'C',
        totalScore: 68.5,
        metaText: 'CV Kinh doanh',
      },
      {
        kind: 'APPEAL',
        employeeId: 'emp-3',
        employeeCode: 'NV003',
        fullName: 'Demo Employee',
        tierCode: null,
        totalScore: null,
        metaText: 'CV Kinh doanh',
      },
      {
        kind: 'TOP',
        employeeId: 'emp-4',
        employeeCode: 'NV004',
        fullName: 'Lê Minh Châu',
        tierCode: 'A1',
        totalScore: 98.2,
        metaText: 'CV Kinh doanh',
      },
    ],
  },
  profileGroups: [
    { name: 'Kinh doanh', displayOrder: 1, weightPercent: 70 },
    { name: 'Dịch vụ', displayOrder: 2, weightPercent: 20 },
    { name: 'Tuân thủ', displayOrder: 3, weightPercent: 10 },
  ],
  staffRows: [
    {
      employeeId: 'emp-4',
      employeeCode: 'NV004',
      fullName: 'Lê Minh Châu',
      jobPositionId: 'pos-1',
      jobPositionTitle: 'CV Kinh doanh',
      groupScores: [
        { groupName: 'Kinh doanh', displayOrder: 1, weightPercent: 70, score: 100 },
        { groupName: 'Dịch vụ', displayOrder: 2, weightPercent: 20, score: 91 },
        { groupName: 'Tuân thủ', displayOrder: 3, weightPercent: 10, score: 100 },
      ],
      totalScore: 98.2,
      previousTotalScore: 92.7,
      scoreDelta: 5.5,
      tierCode: 'A1',
      onTimeSlaPercent: 98.6,
      status: 'FINAL',
      evaluationStatus: 'APPROVED',
      canOpenForm: true,
    },
    {
      employeeId: 'emp-7',
      employeeCode: 'NV007',
      fullName: 'Ngô Thanh Mai',
      jobPositionId: 'pos-1',
      jobPositionTitle: 'CV Kinh doanh',
      groupScores: [
        { groupName: 'Kinh doanh', displayOrder: 1, weightPercent: 70, score: 65.7 },
        { groupName: 'Dịch vụ', displayOrder: 2, weightPercent: 20, score: 78 },
        { groupName: 'Tuân thủ', displayOrder: 3, weightPercent: 10, score: 69 },
      ],
      totalScore: 68.5,
      previousTotalScore: 75.0,
      scoreDelta: -6.5,
      tierCode: 'C',
      onTimeSlaPercent: 81.0,
      status: 'PIP',
      evaluationStatus: 'CALCULATED',
      canOpenForm: true,
    },
    {
      employeeId: 'emp-8',
      employeeCode: 'NV008',
      fullName: 'Phạm Hoàng Nam',
      jobPositionId: 'pos-1',
      jobPositionTitle: 'Mới tiếp nhận',
      groupScores: [],
      totalScore: null,
      previousTotalScore: null,
      scoreDelta: null,
      tierCode: null,
      onTimeSlaPercent: 100,
      status: 'PENDING',
      evaluationStatus: 'NOT_STARTED',
      canOpenForm: false,
    },
  ],
  generatedAt: '2026-11-16T08:00:00Z',
};

describe('EvaluationsComponent', () => {
  let component: EvaluationsComponent;
  let fixture: ComponentFixture<EvaluationsComponent>;
  let httpMock: HttpTestingController;
  let i18n: I18nService;
  let router: Router;

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [EvaluationsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    })
      .overrideComponent(EvaluationsComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent, LayoutIconComponent] },
        add: { imports: [StubHeaderComponent, StubSidebarComponent, StubIconComponent] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(EvaluationsComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    i18n = TestBed.inject(I18nService);
    router = TestBed.inject(Router);
    i18n.setLanguage('vi');
    fixture.detectChanges();

    const req = httpMock.expectOne(`${environment.apiUrl}/evaluations/department-dashboard`);
    req.flush({ success: true, data: MOCK_DASHBOARD });
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('loads department dashboard data and renders 5 ranking tiers A1..C in the chart legend', () => {
    expect(component.departmentScore()).toBe(90.3);
    expect(component.tierItems().map((t) => t.tierCode)).toEqual(['A1', 'A2', 'A3', 'B', 'C']);
    expect(fixture.nativeElement.querySelectorAll('.tier-mini-chip').length).toBe(5);
    expect(fixture.nativeElement.querySelector('.status-badge-open')).toBeNull();
  });

  it('leaves unscored staff without a total and filters by code or name', () => {
    const pending = component.staff().filter((item) => item.totalScore === null);
    expect(pending.map((item) => item.employeeCode)).toEqual(['NV008']);

    component.searchKeyword.set('mai');
    expect(component.filteredStaff().map((item) => item.employeeCode)).toEqual(['NV007']);

    component.searchKeyword.set('');
    expect(component.filteredStaff().length).toBe(3);

    component.tierFilter.set('C');
    expect(component.filteredStaff().map((item) => item.employeeCode)).toEqual(['NV007']);
    component.tierFilter.set('');

    component.statusFilter.set('PENDING');
    expect(component.filteredStaff().map((item) => item.employeeCode)).toEqual(['NV008']);
    component.statusFilter.set('');

    component.sortBy.set('nameAsc');
    expect(component.filteredStaff().map((item) => item.employeeCode)).toEqual([
      'NV004',
      'NV007',
      'NV008',
    ]);
  });

  it('renders 6-period trend bar chart, staff performance column chart, task distribution chart, and diverging bars', () => {
    expect(component.trendBars().length).toBe(2);
    expect(fixture.nativeElement.querySelectorAll('.trend-bar').length).toBe(2);
    expect(component.staffChartBars().length).toBe(2);
    expect(fixture.nativeElement.querySelectorAll('.staff-col-bar').length).toBe(2);
    expect(component.taskDistributionSegments().length).toBe(3);
    expect(fixture.nativeElement.querySelectorAll('.task-bar-row').length).toBe(3);
    expect(fixture.nativeElement.querySelectorAll('.diverge').length).toBe(4);

    component.setTaskViewMode('staff');
    fixture.detectChanges();
    expect(component.staffTaskStackedBars().length).toBe(3);
    expect(fixture.nativeElement.querySelectorAll('.staff-task-col').length).toBe(3);
  });

  it('reloads dashboard when compare period changes', () => {
    const select = fixture.nativeElement.querySelector('.compare-select') as HTMLSelectElement;
    select.value = 'period-10';
    select.dispatchEvent(new Event('change'));

    const req = httpMock.expectOne(
      `${environment.apiUrl}/evaluations/department-dashboard?periodId=period-11&comparePeriodId=period-10`,
    );
    req.flush({ success: true, data: MOCK_DASHBOARD });
    expect(component.selectedComparePeriodId()).toBe('period-10');
  });

  it('exports the Excel report of the selected period and compare period through the department evaluation dashboard', () => {
    const save = spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    spyOn(URL, 'revokeObjectURL');
    spyOn(HTMLAnchorElement.prototype, 'click');

    component.exportExcel();
    expect(component.exporting()).toBeTrue();

    const req = httpMock.expectOne(
      `${environment.apiUrl}/evaluations/department-dashboard/export?periodId=period-11&comparePeriodId=period-10`,
    );
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['x']));

    expect(save).toHaveBeenCalled();
    expect(component.exporting()).toBeFalse();
    expect(component.exportFailed()).toBeFalse();
  });

  it('shows an error and does not save a file when the export fails', () => {
    const save = spyOn(URL, 'createObjectURL');

    component.exportExcel();
    httpMock
      .expectOne(
        `${environment.apiUrl}/evaluations/department-dashboard/export?periodId=period-11&comparePeriodId=period-10`,
      )
      .flush(new Blob(['x']), { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(save).not.toHaveBeenCalled();
    expect(component.exporting()).toBeFalse();
    expect(component.exportFailed()).toBeTrue();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('opens staff evaluation form when clicking View on a row with canOpenForm', () => {
    const navSpy = spyOn(router, 'navigate').and.resolveTo(true);
    component.openStaffForm(MOCK_DASHBOARD.staffRows[0]);
    expect(navSpy).toHaveBeenCalledWith(['/evaluations/form']);
  });

  it('switches the rendered text between Vietnamese and English without reloading', () => {
    expect(text()).toContain('Đánh giá & Hiệu suất Phòng ban');

    i18n.setLanguage('en');
    fixture.detectChanges();

    expect(text()).toContain('Evaluation & Department Performance');
    expect(text()).not.toContain('Đánh giá & Hiệu suất Phòng ban');
  });
});
