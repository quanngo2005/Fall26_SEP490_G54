import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
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
import { DepartmentAllocationComponent } from './department-allocation.component';

function kpiRow(over: Partial<QuarterSplitRow>): QuarterSplitRow {
  return {
    kpiId: 'k1',
    kpiCode: 'KPI_1',
    kpiName: 'Tiền gửi',
    unit: 'triệu VND',
    isRatio: false,
    yearValue: 1000,
    q1: 250,
    q2: 250,
    q3: 250,
    q4: 250,
    previousYear: null,
    sum: 1000,
    diff: 0,
    matched: true,
    ...over,
  };
}

const split: QuarterSplit = {
  branchYearId: 'by-1',
  year: 2026,
  status: 'DRAFT',
  importedAt: null,
  fileRatios: null,
  quarters: [1, 2, 3, 4].map((quarter) => ({ periodId: `p${quarter}`, quarter, locked: false })),
  rows: [
    kpiRow({}),
    kpiRow({ kpiId: 'k2', kpiName: 'Mở thẻ', matched: false, sum: 990, diff: 10 }),
    kpiRow({ kpiId: 'k3', kpiName: 'Số giao dịch' }),
    kpiRow({ kpiId: 'k4', kpiName: 'Chất lượng dịch vụ', isRatio: true, yearValue: 4.5 }),
  ],
};

function department(over: Partial<DepartmentAllocationRow>): DepartmentAllocationRow {
  return {
    departmentId: 'd1',
    departmentName: 'Phòng Giao dịch',
    headcount: 3,
    year: 0,
    q1: 0,
    q2: 0,
    q3: 0,
    q4: 0,
    previousYear: null,
    matched: true,
    ...over,
  };
}

function allocation(over: Partial<KpiAllocation> = {}): KpiAllocation {
  return {
    kpiId: 'k1',
    kpiName: 'Tiền gửi',
    unit: 'triệu VND',
    workType: 'Teller',
    branch: { year: 1000, q1: 250, q2: 250, q3: 250, q4: 250 },
    departments: [
      department({ previousYear: 100 }),
      department({ departmentId: 'd2', departmentName: 'PGD Văn Quán', previousYear: 300 }),
    ],
    totals: { year: 0, q1: 0, q2: 0, q3: 0, q4: 0 },
    remaining: { year: 1000, q1: 250, q2: 250, q3: 250, q4: 250 },
    hiddenDepartments: [
      { departmentId: 'd9', departmentName: 'Phòng KHCN', reason: 'no_headcount' },
    ],
    ...over,
  };
}

const setup: TargetSetup = {
  branchYearId: 'by-1',
  hasBranchYear: true,
  importedAt: null,
  rowCount: 4,
  step: 'ALLOCATE',
  status: 'DRAFT',
  quarters: { done: 2, total: 3 },
  allocation: { done: 1, total: 3 },
  canPublish: false,
  blockers: [{ kpiId: 'k1', kpiCode: 'KPI_1', kpiName: 'Tiền gửi' }],
};

describe('DepartmentAllocationComponent', () => {
  let api: jasmine.SpyObj<TargetAssignmentService>;

  function render(
    options: {
      save?: 'ok' | 'notReady';
      allocations?: Record<string, KpiAllocation>;
      initialKpiId?: string;
    } = {},
  ): ComponentFixture<DepartmentAllocationComponent> {
    api = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'getQuarterSplit',
      'getKpiAllocation',
      'saveKpiAllocation',
    ]);
    api.getQuarterSplit.and.returnValue(of(split));
    const allocations = options.allocations ?? {};
    api.getKpiAllocation.and.callFake((_, kpiId) =>
      of(allocations[kpiId] ?? allocation({ kpiId, kpiName: kpiId })),
    );
    api.saveKpiAllocation.and.callFake((_, kpiId, request: SaveKpiAllocationRequest) =>
      options.save === 'notReady'
        ? throwError(
            () =>
              new HttpErrorResponse({
                status: 409,
                error: { success: false, errorCode: 'TARGET_QUARTERS_NOT_READY' },
              }),
          )
        : of(
            allocation({
              kpiId,
              departments: request.departments.map((d) =>
                department({ ...d, departmentName: d.departmentId }),
              ),
            }),
          ),
    );

    TestBed.configureTestingModule({
      providers: [{ provide: TargetAssignmentService, useValue: api }],
    });
    TestBed.inject(I18nService).setLanguage('vi');

    const fixture = TestBed.createComponent(DepartmentAllocationComponent);
    fixture.componentRef.setInput('branchYearId', 'by-1');
    fixture.componentRef.setInput('year', 2026);
    fixture.componentRef.setInput('setup', setup);
    fixture.componentRef.setInput('initialKpiId', options.initialKpiId ?? null);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<DepartmentAllocationComponent>) =>
    fixture.nativeElement as HTMLElement;

  function button(
    fixture: ComponentFixture<DepartmentAllocationComponent>,
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

  function footerCells(fixture: ComponentFixture<DepartmentAllocationComponent>, row: string) {
    return Array.from(root(fixture).querySelectorAll(`tfoot .${row} td.num`)).map((cell) =>
      cell.textContent?.trim(),
    );
  }

  afterEach(() => localStorage.clear());

  it('lists the KPIs with their state and opens the first allocatable one', () => {
    const fixture = render();
    const cards = Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('.kpi-card'));

    expect(cards.map((card) => card.querySelector('.kpi-card-name')?.textContent?.trim())).toEqual([
      'Tiền gửi',
      'Mở thẻ',
      'Số giao dịch',
    ]);
    expect(cards[1].disabled).toBeTrue();
    expect(cards[1].textContent).toContain('Chưa chia quý xong (bước 2)');
    expect(cards[2].textContent).toContain('✓ Đủ');
    expect(root(fixture).textContent).toContain('Chất lượng dịch vụ: ngưỡng chung, không phân bổ.');
    expect(api.getKpiAllocation).toHaveBeenCalledWith('by-1', 'k1');
  });

  it('starts on the KPI chosen in step 2', () => {
    const fixture = render({ initialKpiId: 'k3' });

    expect(api.getKpiAllocation).toHaveBeenCalledOnceWith('by-1', 'k3');
    expect(root(fixture).querySelector('.kpi-card.is-selected')?.textContent).toContain(
      'Số giao dịch',
    );
  });

  it('fills the year by last year shares and the quarters by the branch ratio with exact totals', () => {
    const fixture = render();

    button(fixture, 'Chia năm theo tỷ lệ 2025').click();
    button(fixture, 'Chia quý theo tỷ lệ của chi nhánh').click();
    fixture.detectChanges();

    expect(footerCells(fixture, 'total-row')).toEqual(['2', '1.000', '250', '250', '250', '250']);
    expect(footerCells(fixture, 'remaining-row')).toEqual(['0 ✓', '0 ✓', '0 ✓', '0 ✓', '0 ✓']);
    expect(root(fixture).querySelector('.quarter-banner')).toBeNull();
    expect(button(fixture, 'Lưu và sang chỉ tiêu tiếp').disabled).toBeFalse();
  });

  it('shows the mismatch per department, per column and in the banner', () => {
    const fixture = render({
      allocations: {
        k1: allocation({
          departments: [
            department({ year: 600, q1: 150, q2: 150, q3: 150, q4: 100 }),
            department({
              departmentId: 'd2',
              departmentName: 'PGD Văn Quán',
              year: 400,
              q1: 100,
              q2: 100,
              q3: 100,
              q4: 100,
            }),
          ],
        }),
      },
    });

    const checks = Array.from(root(fixture).querySelectorAll('tbody .total-cell')).map((cell) =>
      cell.textContent?.trim(),
    );
    expect(checks).toEqual(['lệch 50', '✓']);
    expect(footerCells(fixture, 'remaining-row')).toEqual(['0 ✓', '0 ✓', '0 ✓', '0 ✓', '50']);
    const banner = root(fixture).querySelector('.quarter-banner')?.textContent ?? '';
    expect(banner).toContain('Quý 4 còn thiếu 50 triệu VND');
    expect(banner).toContain('Phòng Giao dịch có tổng 4 quý khác chỉ tiêu năm của phòng');
    expect(button(fixture, 'Lưu và sang chỉ tiêu tiếp').disabled).toBeTrue();
  });

  it('converts the year column between values and percentages', () => {
    const fixture = render();
    button(fixture, 'Nhập tỷ lệ %').click();
    fixture.detectChanges();

    const yearInput = root(fixture).querySelector<HTMLInputElement>('tbody tr input')!;
    yearInput.dispatchEvent(new Event('focus'));
    yearInput.value = '25';
    yearInput.dispatchEvent(new Event('input'));
    yearInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(yearInput.value).toBe('25%');
    button(fixture, 'Nhập giá trị').click();
    fixture.detectChanges();
    expect(root(fixture).querySelector<HTMLInputElement>('tbody tr input')!.value).toBe('250');
  });

  it('saves every department and moves to the next KPI', () => {
    const fixture = render();
    button(fixture, 'Chia năm theo tỷ lệ 2025').click();
    button(fixture, 'Chia quý theo tỷ lệ của chi nhánh').click();
    fixture.detectChanges();
    const saved = jasmine.createSpy('saved');
    fixture.componentInstance.saved.subscribe(saved);

    button(fixture, 'Lưu và sang chỉ tiêu tiếp').click();
    fixture.detectChanges();

    const request = api.saveKpiAllocation.calls.mostRecent().args[2];
    expect(request.departments.map((d) => d.departmentId)).toEqual(['d1', 'd2']);
    expect(request.departments.map((d) => d.year)).toEqual([250, 750]);
    expect(saved).toHaveBeenCalled();
    expect(api.getKpiAllocation).toHaveBeenCalledWith('by-1', 'k3');
  });

  it('keeps the edits and shows the API error when saving fails', () => {
    const fixture = render({ save: 'notReady' });
    button(fixture, 'Chia năm theo tỷ lệ 2025').click();
    button(fixture, 'Lưu nháp').click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('.quarter-save-error')?.textContent).toContain(
      'Chỉ tiêu này chưa chia quý xong ở bước 2.',
    );
    expect(footerCells(fixture, 'total-row')[1]).toBe('1.000');
  });

  it('explains which departments are hidden', () => {
    const fixture = render();
    expect(root(fixture).textContent).toContain('Phòng KHCN bị ẩn vì không có Teller.');
  });
});
