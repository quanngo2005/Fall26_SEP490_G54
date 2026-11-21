import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { DepartmentSummary } from '../../../core/department.models';
import { I18nService } from '../../../core/i18n/i18n.service';
import { KpiAllocation, TargetSetup } from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { PublishStepComponent } from './publish-step.component';

function allocation(kpiId: string, d1Year: number, d1Previous: number | null): KpiAllocation {
  return {
    kpiId,
    kpiName: kpiId,
    unit: 'VND',
    workType: 'Teller',
    branch: { year: 300, q1: 75, q2: 75, q3: 75, q4: 75 },
    departments: [
      {
        departmentId: 'd1',
        departmentName: 'KHCN',
        headcount: 3,
        year: d1Year,
        q1: 0,
        q2: 0,
        q3: 0,
        q4: 0,
        previousYear: d1Previous,
        previousYearActual: null,
        matched: true,
      },
      {
        departmentId: 'd2',
        departmentName: 'Dương Nội',
        headcount: 2,
        year: 300 - d1Year,
        q1: 0,
        q2: 0,
        q3: 0,
        q4: 0,
        previousYear: null,
        previousYearActual: null,
        matched: true,
      },
    ],
    totals: { year: 300, q1: 75, q2: 75, q3: 75, q4: 75 },
    remaining: { year: 0, q1: 0, q2: 0, q3: 0, q4: 0 },
    hiddenDepartments: [],
  };
}

const setup: TargetSetup = {
  branchYearId: 'by-1',
  hasBranchYear: true,
  importedAt: null,
  rowCount: 3,
  step: 'PUBLISH',
  status: 'DRAFT',
  quarters: { done: 3, total: 3 },
  allocation: { done: 2, total: 2 },
  canPublish: true,
  blockers: [],
};

describe('PublishStepComponent (W7)', () => {
  function render(load: 'ok' | 'error' = 'ok'): ComponentFixture<PublishStepComponent> {
    const api = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'getAllAllocations',
    ]);
    api.getAllAllocations.and.returnValue(
      load === 'ok'
        ? of([allocation('k1', 110, 100), allocation('k2', 130, 100)])
        : throwError(() => new Error('boom')),
    );
    TestBed.configureTestingModule({
      providers: [{ provide: TargetAssignmentService, useValue: api }],
    });
    TestBed.inject(I18nService).setLanguage('vi');
    const fixture = TestBed.createComponent(PublishStepComponent);
    fixture.componentRef.setInput('branchYearId', 'by-1');
    fixture.componentRef.setInput('year', 2027);
    fixture.componentRef.setInput('setup', setup);
    fixture.componentRef.setInput('departments', [
      { id: 'd1', managerName: 'Phạm Thu Hà' } as DepartmentSummary,
    ]);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<PublishStepComponent>) =>
    fixture.nativeElement as HTMLElement;

  afterEach(() => localStorage.clear());

  it('shows the summary cards and one row per department receiving targets', () => {
    const fixture = render();

    expect(
      Array.from(root(fixture).querySelectorAll('.summary-value')).map((el) =>
        el.textContent?.trim(),
      ),
    ).toEqual(['3 · 2 phân bổ · 1 kế thừa', '2']);
    const rows = Array.from(root(fixture).querySelectorAll('.publish-table tbody tr')).map((row) =>
      Array.from(row.querySelectorAll('td')).map((td) => td.textContent?.trim()),
    );
    expect(rows).toEqual([
      ['KHCN', 'Phạm Thu Hà', '2', '▲ +20,0%', '—'],
      ['Dương Nội', 'Chưa xác định', '2', '—', 'Chưa có chỉ tiêu 2026'],
    ]);
  });

  it('emits publish from "Phát hành cho 2 phòng" and back from "Về bước 3"', () => {
    const fixture = render();
    const publish = spyOn(fixture.componentInstance.publish, 'emit');
    const back = spyOn(fixture.componentInstance.back, 'emit');
    const buttons = Array.from(
      root(fixture).querySelectorAll<HTMLButtonElement>('.publish-footer button'),
    );

    expect(buttons.map((b) => b.textContent?.trim())).toEqual([
      'Về bước 3',
      'Phát hành cho 2 phòng',
    ]);
    buttons[1].click();
    buttons[0].click();

    expect(publish).toHaveBeenCalled();
    expect(back).toHaveBeenCalled();
  });

  it('shows the load error state', () => {
    const fixture = render('error');

    expect(root(fixture).querySelector('.state-error')?.textContent).toContain(
      'Không tải được danh sách phòng nhận chỉ tiêu.',
    );
  });
});
