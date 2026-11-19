import { provideRouter, Router } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { DepartmentService } from '../../core/department.service';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationService } from '../../core/evaluation.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import {
  QuarterSplit,
  QuarterSplitRow,
  TargetAssignment,
  TargetSetup,
} from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { BranchTargetOverviewComponent } from './branch-target-overview.component';
import { DepartmentTargetOverviewComponent } from './department-target-overview.component';

const paged = <T>(items: T[]) => ({ items, totalCount: items.length, page: 1, pageSize: 100 });
const periods = [
  { id: 'y', periodType: 'YEAR', startDate: '2026-01-01' },
  { id: 'q4', periodType: 'QUARTER', startDate: '2026-10-01' },
];
const kpis = [{ id: 'k1', name: 'Tiền gửi', unit: 'VND', aggregationType: 'SUM' }];
const employees = [
  {
    id: 'e1',
    employeeCode: 'NV1',
    fullName: 'Trần Bảo',
    departmentId: 'd1',
    departmentName: 'Phòng KHCN',
    jobPositionTitle: 'Sales Officer',
    status: 'ACTIVE',
  },
  {
    id: 'e2',
    employeeCode: 'NV2',
    fullName: 'Vũ Đức',
    departmentId: 'd1',
    departmentName: 'Phòng KHCN',
    jobPositionTitle: 'Teller',
    status: 'ACTIVE',
  },
];
const row = (over: Partial<TargetAssignment>): TargetAssignment => ({
  id: 'a',
  kpiDefinitionId: 'k1',
  evaluationPeriodId: 'q4',
  departmentId: null,
  employeeId: null,
  parentAssignmentId: null,
  targetValue: 0,
  status: 'ACTIVE',
  updatedAt: '2026-10-01T00:00:00+00:00',
  feedbackStatus: null,
  feedbackAt: null,
  ...over,
});
const assignments = [
  row({ id: 'b', departmentId: 'b', targetValue: 160 }),
  row({ id: 'da', departmentId: 'd1', targetValue: 128 }),
  row({ id: 'ea', employeeId: 'e1', targetValue: 40, feedbackStatus: 'COMMENTED' }),
];

const departments = [
  { id: 'b', name: 'Chi nhánh Hà Đông', levelType: 'BRANCH', parentDepartmentId: null },
  { id: 'd1', name: 'Phòng KHCN', levelType: 'DEPARTMENT', parentDepartmentId: 'b' },
];

function setup(over: Partial<TargetSetup> = {}): TargetSetup {
  return {
    branchYearId: 'by-1',
    hasBranchYear: true,
    importedAt: null,
    rowCount: 1,
    step: 'QUARTER',
    status: 'DRAFT',
    quarters: { done: 0, total: 1 },
    allocation: { done: 0, total: 1 },
    canPublish: false,
    blockers: [],
    ...over,
  };
}

const quarterSplitRow = (over: Partial<QuarterSplitRow> = {}): QuarterSplitRow => ({
  kpiId: 'k1',
  kpiCode: 'KPI_DEP',
  kpiName: 'Tiền gửi',
  unit: 'VND',
  groupCode: 'OPS',
  groupName: 'Huy động vốn',
  aggregationType: 'SUM',
  isRatio: false,
  yearValue: 160,
  q1: 40,
  q2: 40,
  q3: 40,
  q4: 40,
  previousYearValue: 100,
  previousYear: { q1: 25, q2: 25, q3: 25, q4: 25 },
  previousYearActual: null,
  sum: 160,
  diff: 0,
  matched: true,
  ...over,
});

function quarterSplit(rows: QuarterSplitRow[], status = 'DRAFT'): QuarterSplit {
  return {
    branchYearId: 'by-1',
    year: 2026,
    status,
    importedAt: null,
    fileRatios: null,
    quarters: [1, 2, 3, 4].map((quarter) => ({ periodId: `p${quarter}`, quarter, locked: false })),
    rows,
  };
}

describe('target overview screens', () => {
  const targetApi = {
    list: jasmine.createSpy('list'),
    feedback: jasmine.createSpy('feedback'),
    getSetup: jasmine.createSpy('getSetup'),
    getQuarterSplit: jasmine.createSpy('getQuarterSplit'),
  };
  // What the KPI and employee mocks return; a test may replace them before rendering.
  let kpiList: unknown[];
  let employeeList: typeof employees;

  beforeEach(() => {
    kpiList = kpis;
    employeeList = employees;
    targetApi.list.and.returnValue(of(assignments));
    targetApi.feedback.and.returnValue(
      of([
        {
          id: 'f1',
          type: 'COMMENT',
          comment: 'Chỉ tiêu quá cao',
          createdAt: '2026-10-22T08:00:00Z',
        },
      ]),
    );
    targetApi.list.calls.reset();
    targetApi.getSetup.and.returnValue(of(setup()));
    targetApi.getSetup.calls.reset();
    targetApi.getQuarterSplit.and.returnValue(of(quarterSplit([quarterSplitRow()])));
    targetApi.getQuarterSplit.calls.reset();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: TargetAssignmentService, useValue: targetApi },
        {
          provide: AuthService,
          useValue: { currentUser: () => ({ permissions: ['TARGET_MANAGE'] }) },
        },
        { provide: EvaluationService, useValue: { periods: () => of(periods) } },
        { provide: KpiDefinitionService, useValue: { list: () => of(paged(kpiList)) } },
        { provide: DepartmentService, useValue: { list: () => of(paged(departments)) } },
        {
          provide: EmployeeService,
          useValue: { list: () => of(paged(employeeList)), me: () => of(employeeList[0]) },
        },
      ],
    });
    TestBed.inject(I18nService).setLanguage('vi');
  });

  const render = async <T>(component: new () => T) => {
    const fixture = TestBed.createComponent(component);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  };

  it('branch view shows the branch KPI × quarter table, grouped by KPI group', async () => {
    const fixture = await render(BranchTargetOverviewComponent);
    const element = fixture.nativeElement as HTMLElement;

    expect(targetApi.getSetup).toHaveBeenCalledWith('b', 2026);
    expect(targetApi.getQuarterSplit).toHaveBeenCalledWith('by-1');
    expect(element.querySelector('.view-switch-option.is-active')?.textContent?.trim()).toBe(
      'Chi nhánh',
    );
    expect(element.querySelector('.row-group')?.textContent).toContain('Huy động vốn · 1 KPI');
    expect(element.querySelector('.kpi-name')?.textContent).toContain('Tiền gửi');
    expect(element.querySelector('.subtitle')?.textContent).toContain('toàn chi nhánh');
    expect(element.querySelector('.subtitle')?.textContent).toContain('1 chia quý đủ');
  });

  it('branch view searches by KPI name / code, filters by KPI group and by split state', async () => {
    targetApi.getQuarterSplit.and.returnValue(
      of(
        quarterSplit([
          quarterSplitRow({ kpiId: 'k1', kpiCode: 'KPI_DEP', kpiName: 'Tiền gửi' }),
          quarterSplitRow({
            kpiId: 'k2',
            kpiCode: 'KPI_CASA_RATIO',
            kpiName: 'Tỷ lệ CASA',
            groupName: 'Khác',
            aggregationType: 'AVG',
            isRatio: true,
            yearValue: 0.4,
            q1: 0.4,
            q2: 0.4,
            q3: 0.4,
            q4: 0.4,
            previousYearValue: 0.35,
            matched: true,
          }),
        ]),
      ),
    );
    const fixture = await render(BranchTargetOverviewComponent);
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelectorAll('.row-group').length).toBe(2);

    const kpiSearch = element.querySelector<HTMLInputElement>(
      'input[aria-label="Tìm chỉ tiêu theo tên / mã"]',
    );
    kpiSearch!.value = 'casa';
    kpiSearch!.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(element.querySelectorAll('.row-group').length).toBe(1);
    expect(element.querySelector('.kpi-name')?.textContent).toContain('Tỷ lệ CASA');

    kpiSearch!.value = '';
    kpiSearch!.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const groupSelect = element.querySelector<HTMLSelectElement>(
      'select[aria-label="Nhóm KPI: Tất cả"]',
    );
    groupSelect!.value = 'Khác';
    groupSelect!.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(element.querySelectorAll('.row-group').length).toBe(1);
    expect(element.querySelector('.kpi-name')?.textContent).toContain('Tỷ lệ CASA');

    groupSelect!.value = '';
    groupSelect!.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const splitSelect = element.querySelector<HTMLSelectElement>(
      'select[aria-label="Chia quý: Tất cả"]',
    );
    splitSelect!.value = 'FIXED';
    splitSelect!.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(element.querySelectorAll('.row-group').length).toBe(1);
    expect(element.querySelector('.kpi-name')?.textContent).toContain('Tỷ lệ CASA');
  });

  it('branch view says when the year has not been imported, with the setup button', async () => {
    targetApi.getSetup.and.returnValue(
      of(setup({ branchYearId: null, hasBranchYear: false, status: 'NONE', step: 'IMPORT' })),
    );
    const fixture = await render(BranchTargetOverviewComponent);
    const element = fixture.nativeElement as HTMLElement;

    expect(targetApi.getQuarterSplit).not.toHaveBeenCalled();
    expect(element.textContent).toContain('Năm này chưa nhập chỉ tiêu chi nhánh.');
    expect(
      Array.from(element.querySelectorAll('button')).some(
        (b) => b.textContent?.trim() === 'Thiết lập mục tiêu năm',
      ),
    ).toBeTrue();
  });

  it('branch view opens the quarter-split modal from the KPI name for a target manager', async () => {
    const fixture = await render(BranchTargetOverviewComponent);
    const element = fixture.nativeElement as HTMLElement;

    element.querySelector<HTMLButtonElement>('.kpi-link')?.click();
    fixture.detectChanges();

    expect(element.querySelector('app-kpi-quarter-modal')).not.toBeNull();
  });

  it('branch view shows "✦ Gợi ý chia quý" only while DRAFT, navigating to the setup wizard', async () => {
    const fixture = await render(BranchTargetOverviewComponent);
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const element = fixture.nativeElement as HTMLElement;

    const suggestButton = () =>
      Array.from(element.querySelectorAll('button')).find((b) =>
        b.textContent?.includes('Gợi ý chia quý'),
      );
    expect(suggestButton()).toBeDefined();
    suggestButton()!.click();
    expect(navigate).toHaveBeenCalledWith(['/target-setup', 2026], {
      queryParams: { step: 2, suggest: 1 },
    });

    targetApi.getSetup.and.returnValue(of(setup({ status: 'ACTIVE' })));
    const activeFixture = await render(BranchTargetOverviewComponent);
    expect(
      Array.from((activeFixture.nativeElement as HTMLElement).querySelectorAll('button')).some(
        (b) => b.textContent?.includes('Gợi ý chia quý'),
      ),
    ).toBeFalse();
  });

  it('switches to a department and loads its KPI × quarter table', async () => {
    targetApi.list.calls.reset();
    targetApi.list.and.callFake((filter: { departmentId?: string; evaluationPeriodId: string }) =>
      of(
        filter.departmentId === 'd1'
          ? [row({ id: 'dy', departmentId: 'd1', targetValue: 90 })]
          : [],
      ),
    );
    const fixture = await render(BranchTargetOverviewComponent);
    const element = fixture.nativeElement as HTMLElement;

    const departmentOption = Array.from(element.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Phòng'),
    );
    departmentOption!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(targetApi.list).toHaveBeenCalledWith({ departmentId: 'd1', evaluationPeriodId: 'y' });
    expect(element.querySelector('.subtitle')?.textContent).toContain('Phòng KHCN');
    expect(element.querySelector('.kpi-name')?.textContent).toContain('Tiền gửi');
  });

  it('department view shows the quota, allocation and feedback counts', async () => {
    const element = (await render(DepartmentTargetOverviewComponent)).nativeElement as HTMLElement;

    expect(targetApi.list).toHaveBeenCalledWith({ evaluationPeriodId: 'q4' });
    expect(element.querySelector('.quota-table tbody')?.textContent).toContain('Tiền gửi');
    expect(element.querySelector('.allocated-open')?.textContent).toContain('40');
    expect(element.querySelector('.pill-group')?.textContent).toContain('1 góp ý');
    expect(element.querySelector('.pill-group')?.textContent).toContain('1 chưa xem');
  });

  it('department view shows the latest comment of an employee in the table', async () => {
    const fixture = await render(DepartmentTargetOverviewComponent);
    const element = fixture.nativeElement as HTMLElement;

    expect(targetApi.feedback).toHaveBeenCalledWith('ea');
    expect(element.querySelector('.comment-text')?.textContent).toContain('Chỉ tiêu quá cao');
  });
});
