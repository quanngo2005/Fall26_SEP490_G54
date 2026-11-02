import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { DepartmentService } from '../../core/department.service';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationService } from '../../core/evaluation.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { TargetAssignment } from '../../core/target-assignment.models';
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

describe('target overview screens', () => {
  const targetApi = { list: jasmine.createSpy('list'), feedback: jasmine.createSpy('feedback') };

  beforeEach(() => {
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
    TestBed.configureTestingModule({
      providers: [
        { provide: TargetAssignmentService, useValue: targetApi },
        { provide: EvaluationService, useValue: { periods: () => of(periods) } },
        { provide: KpiDefinitionService, useValue: { list: () => of(paged(kpis)) } },
        {
          provide: DepartmentService,
          useValue: {
            list: () =>
              of(
                paged([
                  {
                    id: 'b',
                    name: 'Chi nhánh Hà Đông',
                    levelType: 'BRANCH',
                    parentDepartmentId: null,
                  },
                  {
                    id: 'd1',
                    name: 'Phòng KHCN',
                    levelType: 'DEPARTMENT',
                    parentDepartmentId: 'b',
                  },
                ]),
              ),
          },
        },
        {
          provide: EmployeeService,
          useValue: { list: () => of(paged(employees)), me: () => of(employees[0]) },
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

  it('branch view lists the branch total and expands a department to its employees', async () => {
    const fixture = await render(BranchTargetOverviewComponent);
    const element = fixture.nativeElement as HTMLElement;

    expect(targetApi.list).toHaveBeenCalledWith({ evaluationPeriodId: 'y' });
    expect(element.querySelector('.row-total')?.textContent).toContain('Chi nhánh Hà Đông');
    expect(element.querySelectorAll('.row-employee').length).toBe(0);

    element.querySelector<HTMLButtonElement>('.row-toggle')?.click();
    fixture.detectChanges();

    expect(element.querySelectorAll('.row-employee').length).toBe(2);
    expect(element.querySelector('.unallocated')?.textContent).toContain('Chưa phân bổ');
  });

  it('department view shows the quota, allocation and feedback counts', async () => {
    const element = (await render(DepartmentTargetOverviewComponent)).nativeElement as HTMLElement;

    expect(targetApi.list).toHaveBeenCalledWith({ evaluationPeriodId: 'q4' });
    expect(element.querySelector('.quota-table tbody')?.textContent).toContain('Tiền gửi');
    expect(element.querySelector('.allocated-open')?.textContent).toContain('40');
    expect(element.querySelector('.pill-group')?.textContent).toContain('1 góp ý');
    expect(element.querySelector('.pill-group')?.textContent).toContain('1 chưa xem');
  });

  it('department view opens the comments of an employee', async () => {
    const fixture = await render(DepartmentTargetOverviewComponent);
    const element = fixture.nativeElement as HTMLElement;

    element.querySelector<HTMLButtonElement>('.link-button')?.click();
    fixture.detectChanges();

    expect(targetApi.feedback).toHaveBeenCalledWith('ea');
    expect(element.querySelector('.drawer')?.textContent).toContain('Chỉ tiêu quá cao');
  });
});
