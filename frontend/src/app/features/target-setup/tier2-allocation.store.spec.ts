import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  DepartmentAllocationRow,
  KpiAllocation,
  QuarterSplit,
} from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { TargetSuggestionService } from '../../core/target-suggestion.service';
import { unitsOf } from './target-math';
import { Tier2AllocationStore, Tier2SaveResult } from './tier2-allocation.store';

function department(
  id: string,
  year: number,
  quarter: number,
  extra: Partial<DepartmentAllocationRow> = {},
): DepartmentAllocationRow {
  return {
    departmentId: id,
    departmentName: `Dept ${id}`,
    headcount: 5,
    year,
    q1: quarter,
    q2: quarter,
    q3: quarter,
    q4: quarter,
    previousYear: null,
    previousYearActual: null,
    matched: true,
    deptYearId: `dy-${id}`,
    ...extra,
  };
}

function allocation(departments: DepartmentAllocationRow[]): KpiAllocation {
  return {
    kpiId: 'k1',
    kpiName: 'KPI 1',
    unit: 'count',
    workType: 'W',
    branch: { year: 100, q1: 25, q2: 25, q3: 25, q4: 25 },
    departments,
    totals: {},
    remaining: {},
    hiddenDepartments: [],
  } as unknown as KpiAllocation;
}

function split(status = 'DRAFT', lockedQuarter: number | null = null): QuarterSplit {
  return {
    branchYearId: 'by1',
    year: 2026,
    status,
    importedAt: null,
    fileRatios: null,
    quarters: [1, 2, 3, 4].map((quarter) => ({ quarter, locked: quarter === lockedQuarter })),
    rows: [{ kpiId: 'k1', isRatio: false }],
  } as unknown as QuarterSplit;
}

const balanced = (): KpiAllocation[] => [
  allocation([department('d1', 60, 15), department('d2', 40, 10)]),
];

describe('Tier2AllocationStore', () => {
  let store: Tier2AllocationStore;
  let targetApi: jasmine.SpyObj<TargetAssignmentService>;

  function load(rows: KpiAllocation[], quarterSplit = split()): void {
    targetApi.getQuarterSplit.and.returnValue(of(quarterSplit));
    targetApi.getAllAllocations.and.returnValue(of(rows));
    store.load('by1').subscribe();
  }

  beforeEach(() => {
    targetApi = jasmine.createSpyObj('TargetAssignmentService', [
      'getQuarterSplit',
      'getAllAllocations',
      'saveKpiAllocation',
    ]);
    TestBed.configureTestingModule({
      providers: [
        Tier2AllocationStore,
        { provide: TargetAssignmentService, useValue: targetApi },
        {
          provide: TargetSuggestionService,
          useValue: jasmine.createSpyObj('TargetSuggestionService', ['suggest']),
        },
      ],
    });
    store = TestBed.inject(Tier2AllocationStore);
  });

  it('loads the split and every allocation, and marks a balanced KPI as matched', () => {
    load(balanced());

    expect(store.loading()).toBeFalse();
    expect(store.failed()).toBeFalse();
    expect(store.kpiState('k1')).toBe('MATCHED');
    expect(store.allMatched()).toBeTrue();
    expect(store.counts()).toEqual({ matched: 1, total: 1, mismatched: 0, notSplit: 0 });
  });

  it('reports a failed load instead of an empty success', () => {
    targetApi.getQuarterSplit.and.returnValue(throwError(() => new Error('boom')));
    targetApi.getAllAllocations.and.returnValue(of([]));

    store.load('by1').subscribe();

    expect(store.failed()).toBeTrue();
    expect(store.loading()).toBeFalse();
  });

  it('treats a KPI whose departments are all 0 as not split', () => {
    load([allocation([department('d1', 0, 0), department('d2', 0, 0)])]);

    expect(store.kpiState('k1')).toBe('NOT_SPLIT');
    expect(store.counts().notSplit).toBe(1);
  });

  it('flags a column that no longer adds up to the branch value after an edit', () => {
    load(balanced());

    store.setCell('k1', 'd1', 'year', 70);

    expect(store.kpiState('k1')).toBe('MISMATCHED');
    expect(store.check('k1')?.columns.year.remainingUnits).toBe(unitsOf(-10));
    expect(store.dirtyKpiIds()).toEqual(['k1']);
    expect(store.cellState('k1', 'd1', 'year')).toBe('MISMATCH');
  });

  it('shows an edited cell as manual once its department adds up again', () => {
    load(balanced());

    store.setCell('k1', 'd1', 'q1', 16);
    expect(store.cellState('k1', 'd1', 'q1')).toBe('MISMATCH');

    store.setCell('k1', 'd1', 'q2', 14);
    expect(store.cellState('k1', 'd1', 'q1')).toBe('MANUAL');
  });

  it('ignores edits once the year is no longer a draft, and locks closed quarters', () => {
    load(balanced(), split('ACTIVE', 2));

    store.setCell('k1', 'd1', 'year', 70);

    expect(store.readOnly()).toBeTrue();
    expect(store.dirtyKpiIds()).toEqual([]);
    expect(store.cellState('k1', 'd1', 'q2')).toBe('LOCKED');
  });

  it('blocks on a department without a saved row until the user confirms it as 0', () => {
    load([
      allocation([
        department('d1', 100, 25),
        department('d2', 0, 0, { deptYearId: null, departmentName: 'New dept' }),
      ]),
    ]);

    expect(store.missingItems()).toEqual([
      { kpiId: 'k1', kpiName: 'KPI 1', departments: ['New dept'] },
    ]);
    expect(store.isMissing('k1', 'd2')).toBeTrue();
    expect(store.canConfirmMissing()).toBeTrue();

    store.confirmAllMissing();

    expect(store.isMissing('k1', 'd2')).toBeFalse();
    expect(store.canConfirmMissing()).toBeFalse();
    expect(store.dirtyKpiIds()).toEqual(['k1']);
  });

  it('saves nothing when there are no unsaved changes', () => {
    load(balanced());
    let saved = -1;

    store.save('by1').subscribe((result) => (saved = result.saved));

    expect(saved).toBe(0);
    expect(targetApi.saveKpiAllocation).not.toHaveBeenCalled();
  });

  it('saves every changed KPI with all its departments and clears the dirty state', () => {
    load(balanced());
    store.setCell('k1', 'd1', 'year', 50);
    store.setCell('k1', 'd2', 'year', 50);
    targetApi.saveKpiAllocation.and.returnValue(
      of(allocation([department('d1', 50, 15), department('d2', 50, 10)])),
    );
    let result: Tier2SaveResult | undefined;

    store.save('by1').subscribe((value) => (result = value));

    const [, kpiId, body] = targetApi.saveKpiAllocation.calls.mostRecent().args;
    expect(kpiId).toBe('k1');
    expect(body.departments.map((d) => d.departmentId)).toEqual(['d1', 'd2']);
    expect(body.departments[0].year).toBe(50);
    expect(result?.saved).toBe(1);
    expect(store.dirtyKpiIds()).toEqual([]);
    expect(store.saving()).toBeFalse();
  });

  it('keeps the error code of a KPI that fails to save and does not count it as saved', () => {
    load(balanced());
    store.setCell('k1', 'd1', 'year', 50);
    targetApi.saveKpiAllocation.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 409, error: { errorCode: 'PERIOD_LOCKED' } }),
      ),
    );
    let result: Tier2SaveResult | undefined;

    store.save('by1').subscribe((value) => (result = value));

    expect(result?.saved).toBe(0);
    expect(result?.errors.get('k1')).toBe('PERIOD_LOCKED');
    expect(store.dirtyKpiIds()).toEqual(['k1']);
  });
});
