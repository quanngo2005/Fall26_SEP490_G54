import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { I18nService } from '../../../core/i18n/i18n.service';
import {
  DepartmentAllocationRow,
  KpiAllocation,
  QuarterSplit,
  QuarterSplitRow,
  SaveKpiAllocationRequest,
  TargetSetup,
} from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { TargetSetupApiService } from '../target-setup-api.service';
import { DepartmentAllocationMatrixComponent } from './department-allocation-matrix.component';

function kpiRow(over: Partial<QuarterSplitRow>): QuarterSplitRow {
  return {
    kpiId: 'k1',
    kpiCode: 'KPI_1',
    kpiName: 'TG xử lý',
    unit: 'triệu VND',
    groupCode: 'G1',
    groupName: 'Huy động vốn',
    aggregationType: 'SUM',
    isRatio: false,
    yearValue: 1000,
    q1: 250,
    q2: 250,
    q3: 250,
    q4: 250,
    previousYearValue: null,
    previousYear: null,
    previousYearActual: null,
    sum: 1000,
    diff: 0,
    matched: true,
    ...over,
  };
}

const split: QuarterSplit = {
  branchYearId: 'by-1',
  year: 2027,
  status: 'DRAFT',
  importedAt: null,
  fileRatios: null,
  quarters: [1, 2, 3, 4].map((quarter) => ({
    periodId: `p${quarter}`,
    quarter,
    locked: quarter === 1,
  })),
  rows: [
    kpiRow({}),
    kpiRow({
      kpiId: 'k2',
      kpiCode: 'KPI_2',
      kpiName: 'Giải ngân',
      groupCode: 'G2',
      groupName: 'Tín dụng',
    }),
    kpiRow({ kpiId: 'k3', kpiCode: 'KPI_3', kpiName: 'Mở thẻ', groupCode: 'G3', groupName: 'Thẻ' }),
  ],
};

function department(over: Partial<DepartmentAllocationRow>): DepartmentAllocationRow {
  return {
    departmentId: 'd1',
    departmentName: 'KHCN',
    headcount: 8,
    year: 600,
    q1: 150,
    q2: 150,
    q3: 150,
    q4: 150,
    previousYear: null,
    previousYearActual: null,
    matched: true,
    ...over,
  };
}

function allocation(over: Partial<KpiAllocation>): KpiAllocation {
  return {
    kpiId: 'k1',
    kpiName: 'TG xử lý',
    unit: 'triệu VND',
    workType: 'Sales Officer',
    branch: { year: 1000, q1: 250, q2: 250, q3: 250, q4: 250 },
    departments: [
      department({}),
      department({
        departmentId: 'd2',
        departmentName: 'Văn Quán',
        year: 400,
        q1: 100,
        q2: 100,
        q3: 100,
        q4: 100,
      }),
    ],
    totals: { year: 1000, q1: 250, q2: 250, q3: 250, q4: 250 },
    remaining: { year: 0, q1: 0, q2: 0, q3: 0, q4: 0 },
    hiddenDepartments: [],
    ...over,
  };
}

/** k1 matched; k2 short by 100 and KHDN not applicable; k3 not ready (quarters not split). */
const allocations: KpiAllocation[] = [
  allocation({}),
  allocation({
    kpiId: 'k2',
    kpiName: 'Giải ngân',
    departments: [
      department({}),
      department({
        departmentId: 'd2',
        departmentName: 'Văn Quán',
        year: 300,
        q1: 75,
        q2: 75,
        q3: 75,
        q4: 75,
      }),
    ],
    hiddenDepartments: [{ departmentId: 'd9', departmentName: 'KHDN', reason: 'no_headcount' }],
  }),
];

const setup: TargetSetup = {
  branchYearId: 'by-1',
  hasBranchYear: true,
  importedAt: null,
  rowCount: 3,
  step: 'ALLOCATE',
  status: 'DRAFT',
  quarters: { done: 2, total: 3 },
  allocation: { done: 1, total: 3 },
  canPublish: false,
  blockers: [],
};

describe('DepartmentAllocationMatrixComponent (W6)', () => {
  let api: jasmine.SpyObj<TargetAssignmentService>;

  function render(): ComponentFixture<DepartmentAllocationMatrixComponent> {
    api = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'getQuarterSplit',
      'getAllAllocations',
      'saveKpiAllocation',
    ]);
    api.getQuarterSplit.and.returnValue(of(split));
    api.getAllAllocations.and.returnValue(of(allocations));
    api.saveKpiAllocation.and.callFake((_, kpiId, request: SaveKpiAllocationRequest) =>
      of(
        allocation({
          kpiId,
          departments: request.departments.map((d) =>
            department({ ...d, departmentName: d.departmentId }),
          ),
        }),
      ),
    );
    TestBed.configureTestingModule({
      providers: [
        { provide: TargetAssignmentService, useValue: api },
        {
          provide: TargetSetupApiService,
          useValue: jasmine.createSpyObj<TargetSetupApiService>('TargetSetupApiService', [
            'suggest',
          ]),
        },
      ],
    });
    TestBed.inject(I18nService).setLanguage('vi');
    const fixture = TestBed.createComponent(DepartmentAllocationMatrixComponent);
    fixture.componentRef.setInput('branchYearId', 'by-1');
    fixture.componentRef.setInput('year', 2027);
    fixture.componentRef.setInput('setup', setup);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<DepartmentAllocationMatrixComponent>) =>
    fixture.nativeElement as HTMLElement;

  function button(
    fixture: ComponentFixture<DepartmentAllocationMatrixComponent>,
    label: string,
  ): HTMLButtonElement {
    const found = Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('button')).find(
      (item) => item.textContent?.trim() === label,
    );
    if (!found) {
      throw new Error(`Button "${label}" not found`);
    }
    return found;
  }

  afterEach(() => localStorage.clear());

  it('groups KPI columns under their group with a Khớp / Lệch / Chưa chip', () => {
    const fixture = render();

    expect(
      Array.from(root(fixture).querySelectorAll('.group-head th')).map((th) =>
        th.textContent?.trim(),
      ),
    ).toEqual(['Phòng', 'Huy động vốn', 'Tín dụng', 'Thẻ']);
    expect(
      Array.from(root(fixture).querySelectorAll('.matrix-col-head .state-badge')).map((el) =>
        el.textContent?.trim(),
      ),
    ).toEqual(['Khớp', 'Lệch 100', 'Chưa']);
  });

  it('shows "Không áp dụng" for a department without staff for the KPI and — for a KPI not ready', () => {
    const fixture = render();
    const khdn = Array.from(root(fixture).querySelectorAll('tbody tr')).find(
      (row) => row.querySelector('.kpi-cell')?.textContent?.trim() === 'KHDN',
    )!;
    const cells = Array.from(khdn.querySelectorAll('td.quarter-cell')).map((td) =>
      td.textContent?.trim(),
    );

    expect(cells).toEqual(['—', 'Không áp dụng', '—']);
  });

  it('shows Σ / branch per column for the chosen period and locks a locked quarter', () => {
    const fixture = render();

    expect(
      Array.from(root(fixture).querySelectorAll('tfoot td')).map((td) => td.textContent?.trim()),
    ).toEqual(['1.000 / 1.000', '900 / 1.000', '—']);

    button(fixture, 'Q1').click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('tbody .cell.is-locked')).not.toBeNull();
    expect(root(fixture).querySelector('tbody .cell.is-locked input')).toBeNull();
  });

  it('opens the KPI in the "Theo chỉ tiêu" view from its column header', () => {
    const fixture = render();
    const opened = spyOn(fixture.componentInstance.openKpi, 'emit');

    root(fixture).querySelectorAll<HTMLButtonElement>('.matrix-col-head')[1].click();

    expect(opened).toHaveBeenCalledWith('k2');
  });

  it('edits a cell in place, keeps Tiếp disabled while mismatched and saves only the changed KPI', () => {
    const fixture = render();
    const input = root(fixture).querySelectorAll<HTMLInputElement>('tbody .cell-input')[1];
    input.value = '400';
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(fixture.componentInstance.isDirty()).toBeTrue();
    expect(button(fixture, 'Tiếp: Phát hành ›').disabled).toBeTrue();

    button(fixture, 'Lưu nháp').click();
    fixture.detectChanges();

    expect(api.saveKpiAllocation).toHaveBeenCalledTimes(1);
    expect(api.saveKpiAllocation.calls.mostRecent().args[1]).toBe('k2');
    expect(root(fixture).querySelector('.quarter-toast')?.textContent).toContain(
      'Đã lưu 1 chỉ tiêu.',
    );
  });

  it('has no equal-split or headcount suggestion methods', () => {
    const fixture = render();

    expect(root(fixture).textContent).not.toContain('Chia đều');
    expect(root(fixture).textContent).not.toContain('Theo số GDV');
    expect(button(fixture, 'Gợi ý phân bổ')).toBeTruthy();
  });
});
