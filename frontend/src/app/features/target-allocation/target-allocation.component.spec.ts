import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationService } from '../../core/evaluation.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { TargetAssignment } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { targetDmLocalizationStub } from './testing/target-dm-i18n.stub';
import { TargetAllocationApiService } from './target-allocation-api.service';
import { EmployeeAllocation } from './target-allocation.models';
import { TargetAllocationComponent } from './target-allocation.component';

// The real HeaderComponent/SidebarComponent read `localization.common`, `.layout`, `.menu`, etc. —
// dictionaries this spec's `targetDmLocalizationStub()` does not provide. Swap them for empty stubs
// so this spec only needs the `targetDm`/`targetShared` keys it actually exercises.
@Component({ selector: 'app-header', standalone: true, template: '' })
class HeaderStub {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class SidebarStub {}

const paged = <T>(items: T[]) => ({ items, totalCount: items.length, page: 1, pageSize: 100 });

const kpi = (overrides: Partial<KpiDefinition> = {}): KpiDefinition => ({
  id: 'k1',
  kpiGroupId: 'g1',
  kpiGroupName: 'Huy động vốn',
  code: 'KPI_NFI',
  name: 'Tiền gửi - giới thiệu',
  description: null,
  formula: null,
  unit: 'tr VND',
  aggregationType: 'SUM',
  direction: 'HIGHER_BETTER',
  maxAchievementPercent: null,
  status: 'ACTIVE',
  updatedAt: '2027-01-01T00:00:00Z',
  rejectReason: null,
  requestNote: null,
  creditRole: 'PROCESSOR',
  dataSource: 'TRANSACTION',
  productName: null,
  baselineValue: null,
  jobPositions: [],
  effectiveFrom: null,
  effectiveTo: null,
  submittedAt: null,
  ...overrides,
});

const matchedRow = (overrides: Partial<EmployeeAllocation> = {}): EmployeeAllocation => ({
  deptYearId: 'dy1',
  kpiId: 'k1',
  kpiName: 'Tiền gửi - giới thiệu',
  unit: 'tr VND',
  departmentId: 'd1',
  departmentName: 'Phòng KHCN',
  // 'ACTIVE' here means the employee-year rows are already published (component.published()),
  // which makes the grid read-only — these specs edit/publish an unpublished ('DRAFT') row.
  status: 'DRAFT',
  department: { year: 100, q1: 25, q2: 25, q3: 25, q4: 25 },
  employees: [
    {
      employeeId: 'e1',
      employeeName: 'Nguyễn Văn A',
      jobPositionName: 'Sales Officer',
      year: 100,
      q1: 25,
      q2: 25,
      q3: 25,
      q4: 25,
      previousYear: 90,
      previousYearActual: 92,
      matched: true,
    },
  ],
  totals: { year: 100, q1: 25, q2: 25, q3: 25, q4: 25 },
  remaining: { year: 0, q1: 0, q2: 0, q3: 0, q4: 0 },
  lockedEmployees: [],
  ...overrides,
});

const deptYearAssignment: TargetAssignment = {
  id: 'dy1',
  kpiDefinitionId: 'k1',
  evaluationPeriodId: 'y2027',
  departmentId: 'd1',
  employeeId: null,
  parentAssignmentId: null,
  targetValue: 100,
  status: 'ACTIVE',
  updatedAt: '2027-01-01T00:00:00Z',
  feedbackStatus: null,
  feedbackAt: null,
};

describe('TargetAllocationComponent (W10/W11)', () => {
  let allocationApi: jasmine.SpyObj<TargetAllocationApiService>;
  let targetApi: jasmine.SpyObj<TargetAssignmentService>;

  async function render(
    row: EmployeeAllocation = matchedRow(),
  ): Promise<ComponentFixture<TargetAllocationComponent>> {
    targetApi = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', ['list']);
    targetApi.list.and.returnValue(of([deptYearAssignment]));

    allocationApi = jasmine.createSpyObj<TargetAllocationApiService>('TargetAllocationApiService', [
      'getEmployeeAllocations',
      'saveEmployeeAllocation',
      'publishEmployees',
      'suggest',
    ]);
    allocationApi.getEmployeeAllocations.and.returnValue(of([row]));

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          // Pin the year so the component requests the same year (2027) the mocked
          // `EvaluationService.periods()` / `TargetAssignmentService.list()` responses use,
          // instead of defaulting to the real current year.
          useValue: { snapshot: { queryParamMap: convertToParamMap({ year: '2027' }) } },
        },
        { provide: TargetAssignmentService, useValue: targetApi },
        { provide: TargetAllocationApiService, useValue: allocationApi },
        {
          provide: EvaluationService,
          useValue: {
            periods: () =>
              of([
                { id: 'y2027', periodType: 'YEAR', startDate: '2027-01-01', endDate: '2027-12-31' },
              ]),
          },
        },
        { provide: KpiDefinitionService, useValue: { list: () => of(paged([kpi()])) } },
        {
          provide: EmployeeService,
          useValue: {
            me: () => of({ id: 'e-me', departmentId: 'd1', departmentName: 'Phòng KHCN' }),
          },
        },
        {
          provide: I18nService,
          useValue: jasmine.createSpyObj('I18nService', ['formatNumber', 'translate'], {
            localization: targetDmLocalizationStub(),
            currentLanguage: () => 'vi',
          }),
        },
      ],
    }).overrideComponent(TargetAllocationComponent, {
      remove: { imports: [HeaderComponent, SidebarComponent] },
      add: { imports: [HeaderStub, SidebarStub] },
    });
    const i18n = TestBed.inject(I18nService) as unknown as {
      formatNumber: jasmine.Spy;
      translate: jasmine.Spy;
    };
    i18n.formatNumber.and.callFake((v: number) => String(v));
    i18n.translate.and.callFake((key: string, params?: Record<string, string | number>) => {
      const path = key.split('.').slice(1); // drop the "targetDm" prefix
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      let text: any = targetDmLocalizationStub().targetDm;
      for (const segment of path) text = text?.[segment];
      if (typeof text !== 'string') return key;
      return Object.entries(params ?? {}).reduce(
        (acc, [k, v]) => acc.replace(new RegExp(`\\{${k}\\}`, 'g'), String(v)),
        text,
      );
    });

    const fixture = TestBed.createComponent(TargetAllocationComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  /* eslint-disable @typescript-eslint/no-explicit-any */
  const protectedApi = (fixture: ComponentFixture<TargetAllocationComponent>) =>
    fixture.componentInstance as any;
  /* eslint-enable @typescript-eslint/no-explicit-any */

  it('loads the department-year anchor and the bulk employee-allocation grid', async () => {
    const fixture = await render();
    expect(targetApi.list).toHaveBeenCalledWith({
      departmentId: 'd1',
      evaluationPeriodId: 'y2027',
      status: 'ACTIVE',
    });
    expect(allocationApi.getEmployeeAllocations).toHaveBeenCalledWith('dy1');
    expect(protectedApi(fixture).rows().length).toBe(1);
  });

  it('can publish once the only KPI is fully matched in all 5 periods', async () => {
    const fixture = await render();
    expect(protectedApi(fixture).canPublish()).toBeTrue();
  });

  it('cannot publish while a KPI is short of the department target', async () => {
    // `canPublish`/`kpiMatched` recompute Σemployees vs. department straight from the draft, not
    // from `remaining` — so the employee split itself must fall short of the department target.
    const fixture = await render(
      matchedRow({
        employees: [
          {
            employeeId: 'e1',
            employeeName: 'Nguyễn Văn A',
            jobPositionName: 'Sales Officer',
            year: 90,
            q1: 25,
            q2: 25,
            q3: 25,
            q4: 15,
            previousYear: 90,
            previousYearActual: 92,
            matched: false,
          },
        ],
        remaining: { year: 10, q1: 0, q2: 0, q3: 0, q4: 10 },
      }),
    );
    expect(protectedApi(fixture).canPublish()).toBeFalse();
  });

  it('editing a year cell spreads the value over Q1-Q4 by the department quarter shape', async () => {
    const fixture = await render();
    const component = protectedApi(fixture);
    component.editYear('k1', 'e1', 80);
    const cell = component.draft()['k1']['e1'];
    expect(cell.year).toBe(80);
    expect(cell.q1 + cell.q2 + cell.q3 + cell.q4).toBe(80);
  });

  it('publish() calls the Phase-3 publish-employees endpoint with the anchor id', async () => {
    // `render()` creates a fresh `allocationApi` spy each call, so the stub must be set after
    // rendering — stubbing it beforehand configures a spy object `render()` then discards.
    const fixture = await render();
    allocationApi.publishEmployees.and.returnValue(
      of({
        publishedAt: '2027-01-01T00:00:00Z',
        departmentId: 'd1',
        year: 2027,
        employeeYearCount: 1,
        employeeQuarterCount: 4,
      }),
    );
    protectedApi(fixture).publish();
    expect(allocationApi.publishEmployees).toHaveBeenCalledWith('dy1');
  });

  it('a ratio KPI is always considered matched and shown as "not split"', async () => {
    const fixture = await render(
      matchedRow({
        kpiId: 'k2',
        deptYearId: 'dy2',
        employees: [],
        remaining: { year: 1, q1: 1, q2: 0, q3: 0, q4: 0 },
      }),
    );
    // Not a ratio KPI in the catalog (kpi() default is SUM); row without employees and without locked
    // employees counts as "not split" rather than "mismatched".
    expect(protectedApi(fixture).counts().notSplit).toBe(1);
  });
});
