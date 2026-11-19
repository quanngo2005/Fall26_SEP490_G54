import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
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
import { DmOverviewComponent } from './dm-overview.component';

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

const row: EmployeeAllocation = {
  deptYearId: 'dy1',
  kpiId: 'k1',
  kpiName: 'Tiền gửi - giới thiệu',
  unit: 'tr VND',
  departmentId: 'd1',
  departmentName: 'Phòng KHCN',
  status: 'ACTIVE',
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
      previousYear: null,
      previousYearActual: null,
      matched: true,
    },
  ],
  totals: { year: 100, q1: 25, q2: 25, q3: 25, q4: 25 },
  remaining: { year: 0, q1: 0, q2: 0, q3: 0, q4: 0 },
  lockedEmployees: [],
};

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

describe('DmOverviewComponent (W9)', () => {
  let allocationApi: jasmine.SpyObj<TargetAllocationApiService>;

  async function render(): Promise<ComponentFixture<DmOverviewComponent>> {
    const targetApi = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'list',
    ]);
    targetApi.list.and.returnValue(of([deptYearAssignment]));

    allocationApi = jasmine.createSpyObj<TargetAllocationApiService>('TargetAllocationApiService', [
      'getEmployeeAllocations',
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
    }).overrideComponent(DmOverviewComponent, {
      remove: { imports: [HeaderComponent, SidebarComponent] },
      add: { imports: [HeaderStub, SidebarStub] },
    });
    const i18n = TestBed.inject(I18nService) as unknown as {
      formatNumber: jasmine.Spy;
      translate: jasmine.Spy;
    };
    i18n.formatNumber.and.callFake((v: number) => String(v));
    i18n.translate.and.callFake((key: string, params?: Record<string, string | number>) => {
      const path = key.split('.').slice(1);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      let text: any = targetDmLocalizationStub().targetDm;
      for (const segment of path) text = text?.[segment];
      if (typeof text !== 'string') return key;
      return Object.entries(params ?? {}).reduce(
        (acc, [k, v]) => acc.replace(new RegExp(`\\{${k}\\}`, 'g'), String(v)),
        text,
      );
    });

    const fixture = TestBed.createComponent(DmOverviewComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  /* eslint-disable @typescript-eslint/no-explicit-any */
  const instance = (fixture: ComponentFixture<DmOverviewComponent>) =>
    fixture.componentInstance as any;
  /* eslint-enable @typescript-eslint/no-explicit-any */

  it('summarizes KPI matched count per position tab', async () => {
    const fixture = await render();
    const summaries = instance(fixture).summaries();
    expect(summaries).toEqual([{ label: 'Sales Officer', matched: 1, total: 1 }]);
  });

  it('counts an employee with no previous-year history as new', async () => {
    const fixture = await render();
    expect(instance(fixture).newEmployeeCount()).toBe(1);
  });

  it('navigates to the W10 allocation grid for the selected year', async () => {
    const fixture = await render();
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    instance(fixture).goToAllocation();
    expect(navigate).toHaveBeenCalledWith(['/target-allocation'], {
      queryParams: { year: instance(fixture).year() },
    });
  });
});
