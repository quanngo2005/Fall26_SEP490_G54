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
import { SuggestTargetsResponseDto, TargetSetupApiService } from '../target-setup-api.service';
import { DepartmentAllocationComponent } from './department-allocation.component';

function kpiRow(over: Partial<QuarterSplitRow>): QuarterSplitRow {
  return {
    kpiId: 'k1',
    kpiCode: 'KPI_1',
    kpiName: 'Tiền gửi',
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
  quarters: [1, 2, 3, 4].map((quarter) => ({ periodId: `p${quarter}`, quarter, locked: false })),
  rows: [
    kpiRow({}),
    kpiRow({ kpiId: 'k2', kpiCode: 'KPI_2', kpiName: 'Số dư CASA' }),
    kpiRow({ kpiId: 'k3', kpiCode: 'KPI_3', kpiName: 'Mở thẻ', groupCode: 'G2', groupName: 'Thẻ' }),
    kpiRow({ kpiId: 'k4', kpiCode: 'KPI_4', kpiName: 'Tỷ lệ nợ xấu', isRatio: true }),
  ],
};

function department(over: Partial<DepartmentAllocationRow>): DepartmentAllocationRow {
  return {
    departmentId: 'd1',
    departmentName: 'KHCN',
    headcount: 8,
    year: 0,
    q1: 0,
    q2: 0,
    q3: 0,
    q4: 0,
    previousYear: null,
    previousYearActual: null,
    matched: true,
    ...over,
  };
}

function allocation(over: Partial<KpiAllocation> = {}): KpiAllocation {
  return {
    kpiId: 'k1',
    kpiName: 'Tiền gửi',
    unit: 'triệu VND',
    workType: 'Sales Officer',
    branch: { year: 1000, q1: 250, q2: 250, q3: 250, q4: 250 },
    departments: [
      department({ year: 600, q1: 150, q2: 150, q3: 150, q4: 150, previousYearActual: 300 }),
      department({
        departmentId: 'd2',
        departmentName: 'Văn Quán',
        headcount: 6,
        year: 300,
        q1: 75,
        q2: 75,
        q3: 75,
        q4: 75,
        previousYearActual: 100,
      }),
    ],
    totals: { year: 900, q1: 225, q2: 225, q3: 225, q4: 225 },
    remaining: { year: 100, q1: 25, q2: 25, q3: 25, q4: 25 },
    hiddenDepartments: [{ departmentId: 'd9', departmentName: 'KHDN', reason: 'no_headcount' }],
    ...over,
  };
}

/** k1 mismatched (100 left), k2 matched; k3 is not in the bulk response (quarters not ready). */
function allocations(): KpiAllocation[] {
  return [
    allocation(),
    allocation({
      kpiId: 'k2',
      kpiName: 'Số dư CASA',
      departments: [
        department({ year: 700, q1: 175, q2: 175, q3: 175, q4: 175 }),
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
      hiddenDepartments: [],
    }),
  ];
}

const setup: TargetSetup = {
  branchYearId: 'by-1',
  hasBranchYear: true,
  importedAt: null,
  rowCount: 4,
  step: 'ALLOCATE',
  status: 'DRAFT',
  quarters: { done: 3, total: 4 },
  allocation: { done: 1, total: 3 },
  canPublish: false,
  blockers: [],
};

describe('DepartmentAllocationComponent (W5)', () => {
  let api: jasmine.SpyObj<TargetAssignmentService>;
  let suggestApi: jasmine.SpyObj<TargetSetupApiService>;

  function render(
    options: {
      save?: 'ok' | 'notReady';
      initialKpiId?: string;
      setup?: TargetSetup;
      all?: KpiAllocation[];
      lockedQuarter?: number;
    } = {},
  ): ComponentFixture<DepartmentAllocationComponent> {
    api = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'getQuarterSplit',
      'getAllAllocations',
      'saveKpiAllocation',
    ]);
    api.getQuarterSplit.and.returnValue(
      of(
        options.lockedQuarter
          ? {
              ...split,
              quarters: split.quarters.map((q) => ({
                ...q,
                locked: q.quarter === options.lockedQuarter,
              })),
            }
          : split,
      ),
    );
    api.getAllAllocations.and.returnValue(of(options.all ?? allocations()));
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
    suggestApi = jasmine.createSpyObj<TargetSetupApiService>('TargetSetupApiService', ['suggest']);

    TestBed.configureTestingModule({
      providers: [
        { provide: TargetAssignmentService, useValue: api },
        { provide: TargetSetupApiService, useValue: suggestApi },
      ],
    });
    TestBed.inject(I18nService).setLanguage('vi');

    const fixture = TestBed.createComponent(DepartmentAllocationComponent);
    fixture.componentRef.setInput('branchYearId', 'by-1');
    fixture.componentRef.setInput('year', 2027);
    fixture.componentRef.setInput('setup', options.setup ?? setup);
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

  function cellInputs(
    fixture: ComponentFixture<DepartmentAllocationComponent>,
  ): HTMLInputElement[] {
    return Array.from(root(fixture).querySelectorAll<HTMLInputElement>('tbody .cell-input'));
  }

  function commit(
    fixture: ComponentFixture<DepartmentAllocationComponent>,
    input: HTMLInputElement,
    value: string,
  ): void {
    input.value = value;
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  function footer(fixture: ComponentFixture<DepartmentAllocationComponent>, row: string): string[] {
    return Array.from(root(fixture).querySelectorAll(`tfoot .${row} td`)).map(
      (cell) => cell.textContent?.trim() ?? '',
    );
  }

  afterEach(() => localStorage.clear());

  it('shows the tier line and lists KPIs by group with Khớp / Lệch / Chưa', () => {
    const fixture = render();

    expect(root(fixture).querySelector('.tier-status-line')?.textContent).toContain(
      'TẦNG 2 · CHI NHÁNH → PHÒNG',
    );
    expect(root(fixture).querySelector('.counts')?.textContent?.replace(/\s+/g, ' ')).toContain(
      '1 / 3',
    );
    const cards = Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('.kpi-card'));
    expect(cards.map((card) => card.querySelector('.state-badge')?.textContent?.trim())).toEqual([
      'Lệch',
      'Khớp',
      'Chưa',
    ]);
    expect(cards[2].disabled).toBeTrue();
    expect(root(fixture).querySelector('h2')?.textContent?.trim()).toBe('Tiền gửi');
    expect(root(fixture).textContent).toContain('1 KPI tỷ lệ / ngưỡng: kế thừa, không phân bổ.');
  });

  it('starts on the KPI chosen in step 2', () => {
    const fixture = render({ initialKpiId: 'k2' });

    expect(root(fixture).querySelector('h2')?.textContent?.trim()).toBe('Số dư CASA');
  });

  it('filters the KPI list with the tier line chips', () => {
    const fixture = render();

    button(fixture, 'Lệch (1)').click();
    fixture.detectChanges();

    expect(
      Array.from(root(fixture).querySelectorAll('.kpi-card-name')).map((el) =>
        el.textContent?.trim(),
      ),
    ).toEqual(['Tiền gửi']);
  });

  it('renders the columns of the mockup, last year actual share and hidden departments', () => {
    const fixture = render();

    expect(
      Array.from(root(fixture).querySelectorAll('thead th')).map((th) => th.textContent?.trim()),
    ).toEqual(['Phòng', 'Số NV phụ trách', 'Thực tế 2026', 'Năm 2027', 'Q1', 'Q2', 'Q3', 'Q4']);
    const firstRow = root(fixture).querySelectorAll('tbody tr')[0];
    expect(firstRow.querySelectorAll('td')[2].textContent?.trim()).toBe('75,0%');
    expect(root(fixture).querySelector('.not-applicable')?.textContent?.trim()).toBe(
      'Không có Sales Officer',
    );
    expect(footer(fixture, 'remaining-row')).toEqual(['100', '25', '25', '25', '25']);
  });

  it('has no equal-split or headcount suggestion methods', () => {
    const fixture = render();
    const text = root(fixture).textContent ?? '';

    expect(text).not.toContain('Chia đều');
    expect(text).not.toContain('Theo số GDV');
  });

  it('marks an edited department red when its quarters stop adding up and keeps Tiếp disabled', () => {
    const fixture = render({ initialKpiId: 'k2', setup: { ...setup, canPublish: true } });

    commit(fixture, cellInputs(fixture)[0], '800');

    expect(root(fixture).querySelector('tbody .cell.is-mismatch')).not.toBeNull();
    expect(root(fixture).querySelector('.quarter-banner')?.textContent).toContain(
      'KHCN có tổng 4 quý khác chỉ tiêu năm',
    );
    expect(button(fixture, 'Tiếp: Phát hành ›').disabled).toBeTrue();
  });

  it('enables Tiếp only when every KPI matches, is saved and the server allows publishing', () => {
    const matched = allocations()[1];
    const fixture = render({
      all: [{ ...matched, kpiId: 'k1' }, matched, { ...matched, kpiId: 'k3' }],
      setup: { ...setup, canPublish: true },
    });
    const emitted = spyOn(fixture.componentInstance.publish, 'emit');

    button(fixture, 'Tiếp: Phát hành ›').click();

    expect(emitted).toHaveBeenCalled();
  });

  it('"✦ Gợi ý phân bổ" calls POST /target-suggestions tier 2 for the open KPI and previews it', () => {
    const fixture = render();
    const response: SuggestTargetsResponseDto = {
      tier: 2,
      basis: 'TARGET_LAST_YEAR',
      overwrite: 'EMPTY_ONLY',
      warnings: [],
      kpis: [
        {
          kpiId: 'k1',
          kpiCode: 'KPI_1',
          kpiName: 'Tiền gửi',
          unit: 'triệu VND',
          aggregationType: 'SUM',
          cells: [
            {
              unitId: 'd2',
              unitName: 'Văn Quán',
              year: 400,
              q1: 100,
              q2: 100,
              q3: 100,
              q4: 100,
              usedFallbackAverage: false,
              overwritten: true,
              locked: false,
              lockReason: null,
            },
          ],
          lockedUnits: [],
          total: 1000,
          sumMatches: true,
          disabledReason: null,
          warnings: [],
        },
      ],
    };
    suggestApi.suggest.and.returnValue(of(response));

    button(fixture, 'Gợi ý phân bổ').click();
    fixture.detectChanges();
    button(fixture, 'Tạo gợi ý').click();
    fixture.detectChanges();

    const [year, request] = suggestApi.suggest.calls.mostRecent().args;
    expect(year).toBe(2027);
    expect(request.tier).toBe(2);
    expect(request.anchorId).toBe('by-1');
    expect(request.kpiIds).toEqual(['k1']);
    expect(request.currentValues.map((value) => value.unitId)).toEqual(['d1', 'd2']);
    expect(root(fixture).querySelector('.preview-banner')).not.toBeNull();
    expect(root(fixture).querySelectorAll('tbody .cell.is-suggested').length).toBe(5);
    expect(footer(fixture, 'remaining-row')).toEqual(['0', '0', '0', '0', '0']);

    button(fixture, 'Hoàn tác').click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('.preview-banner')).toBeNull();
    expect(footer(fixture, 'remaining-row')).toEqual(['100', '25', '25', '25', '25']);
  });

  it('a suggestion leaves a locked quarter alone and the year follows the quarters', () => {
    const fixture = render({ lockedQuarter: 1 });
    const cell = (q1: number, q2: number) => ({
      unitId: 'd2',
      unitName: 'Văn Quán',
      year: q1 + q2 + 200,
      q1,
      q2,
      q3: 100,
      q4: 100,
      usedFallbackAverage: false,
      overwritten: true,
      locked: false,
      lockReason: null,
    });
    suggestApi.suggest.and.returnValue(
      of({
        tier: 2,
        basis: 'TARGET_LAST_YEAR',
        overwrite: 'UNLOCKED',
        warnings: [],
        kpis: [
          {
            kpiId: 'k1',
            kpiCode: 'KPI_1',
            kpiName: 'Tiền gửi',
            unit: 'triệu VND',
            aggregationType: 'SUM',
            cells: [cell(90, 110)],
            lockedUnits: [],
            total: 1000,
            sumMatches: true,
            disabledReason: null,
            warnings: [],
          },
        ],
      } as SuggestTargetsResponseDto),
    );

    button(fixture, 'Gợi ý phân bổ').click();
    fixture.detectChanges();
    button(fixture, 'Tạo gợi ý').click();
    fixture.detectChanges();

    const row = Array.from(root(fixture).querySelectorAll('tbody tr'))
      .find((tr) => tr.textContent?.includes('Văn Quán'))!
      .querySelectorAll('input');
    const values = Array.from(row).map((input) => Number((input as HTMLInputElement).value));
    // Locked Q1 has no input (it keeps its saved 75); the year is Q1 + the suggested Q2-Q4, not the suggested year.
    expect(values).toEqual([75 + 110 + 100 + 100, 110, 100, 100]);
  });

  it('saves every changed KPI and shows a toast', () => {
    const fixture = render({ initialKpiId: 'k2' });
    const saved = spyOn(fixture.componentInstance.saved, 'emit');

    commit(fixture, cellInputs(fixture)[1], '200');
    button(fixture, 'Lưu nháp').click();
    fixture.detectChanges();

    expect(api.saveKpiAllocation).toHaveBeenCalledTimes(1);
    const [, kpiId, request] = api.saveKpiAllocation.calls.mostRecent().args;
    expect(kpiId).toBe('k2');
    expect(request.departments[0]).toEqual({
      departmentId: 'd1',
      year: 700,
      q1: 200,
      q2: 175,
      q3: 175,
      q4: 175,
    });
    expect(saved).toHaveBeenCalled();
    expect(root(fixture).querySelector('.quarter-toast')?.textContent).toContain('Đã lưu phân bổ.');
  });

  it('keeps the edits and names the KPI when saving fails', () => {
    const fixture = render({ save: 'notReady', initialKpiId: 'k2' });

    commit(fixture, cellInputs(fixture)[1], '200');
    button(fixture, 'Lưu nháp').click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('.quarter-save-error')?.textContent).toContain(
      'Số dư CASA: Chỉ tiêu này chưa chia quý xong ở bước 2.',
    );
    expect(cellInputs(fixture)[1].value).toBe('200');
  });

  it('shows the load error state with a retry button', () => {
    api = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'getQuarterSplit',
      'getAllAllocations',
    ]);
    api.getQuarterSplit.and.returnValue(throwError(() => new Error('boom')));
    api.getAllAllocations.and.returnValue(of([]));
    TestBed.configureTestingModule({
      providers: [
        { provide: TargetAssignmentService, useValue: api },
        { provide: TargetSetupApiService, useValue: {} },
      ],
    });
    TestBed.inject(I18nService).setLanguage('vi');
    const fixture = TestBed.createComponent(DepartmentAllocationComponent);
    fixture.componentRef.setInput('branchYearId', 'by-1');
    fixture.componentRef.setInput('year', 2027);
    fixture.componentRef.setInput('setup', setup);
    fixture.detectChanges();

    expect(root(fixture).querySelector('.state-error')?.textContent).toContain(
      'Không tải được phân bổ cho phòng.',
    );
  });
});
