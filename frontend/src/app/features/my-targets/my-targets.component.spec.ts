import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { MyTargetsApiService } from './my-targets-api.service';
import { MyTargetYear, MyTargetProgress } from './my-targets.models';
import { MyTargetsComponent } from './my-targets.component';

@Component({ selector: 'app-header', standalone: true, template: '' })
class HeaderStub {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class SidebarStub {}

const ME = { id: 'e1', fullName: 'Phạm Thu Hà', managerName: 'Trưởng phòng A' } as EmployeeDetail;
const KPIS = [
  { id: 'k1', kpiGroupName: 'Huy động vốn' },
  { id: 'k2', kpiGroupName: 'Tín dụng' },
] as KpiDefinition[];

function yearData(overrides: Partial<MyTargetYear> = {}): MyTargetYear {
  return {
    year: 2027,
    employeeId: 'e1',
    employeeName: 'Phạm Thu Hà',
    departmentId: 'd1',
    departmentName: 'Phòng KHCN',
    ackStatus: 'PENDING',
    confirmedAt: null,
    kpis: [
      {
        kpiId: 'k1',
        kpiCode: 'KPI_ACC_OPEN',
        kpiName: 'Tiền gửi – xử lý',
        unit: 'tr VND',
        aggregationType: 'SUM',
        year: 6400,
        q1: 1440,
        q2: 1600,
        q3: 1760,
        q4: 1600,
        previousYearTarget: 6000,
        previousYearActual: 6120,
        changed: false,
        changedOldValue: null,
        changedNewValue: null,
        hasFeedback: false,
      },
      {
        kpiId: 'k2',
        kpiCode: 'KPI_LOAN_NEW',
        kpiName: 'Thẻ tín dụng kích hoạt',
        unit: 'thẻ',
        aggregationType: 'SUM',
        year: 30,
        q1: 7,
        q2: 7,
        q3: 8,
        q4: 8,
        previousYearTarget: null,
        previousYearActual: null,
        changed: false,
        changedOldValue: null,
        changedNewValue: null,
        hasFeedback: false,
      },
    ],
    ...overrides,
  };
}

function progressData(): MyTargetProgress {
  return {
    year: 2027,
    period: '2027-Q2',
    kpis: [
      {
        kpiId: 'k1',
        kpiCode: 'KPI_ACC_OPEN',
        kpiName: 'Tiền gửi – xử lý',
        unit: 'tr VND',
        progress: {
          target: 1600,
          achieved: 690,
          inReviewCount: 3,
          achievedPercent: 43,
          expectedPacePercent: 48,
          paceStatus: 'SLOW',
        },
      },
      {
        kpiId: 'k2',
        kpiCode: 'KPI_LOAN_NEW',
        kpiName: 'Thẻ tín dụng kích hoạt',
        unit: 'thẻ',
        progress: {
          target: 7,
          achieved: 7,
          inReviewCount: 0,
          achievedPercent: 100,
          expectedPacePercent: 48,
          paceStatus: 'ACHIEVED',
        },
      },
    ],
  };
}

describe('MyTargetsComponent', () => {
  let api: jasmine.SpyObj<MyTargetsApiService>;
  let queryParamMap: BehaviorSubject<ParamMap>;
  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const root = (fixture: ComponentFixture<unknown>) => fixture.nativeElement as HTMLElement;
  const click = (fixture: ComponentFixture<unknown>, selector: string) => {
    root(fixture).querySelector<HTMLElement>(selector)?.click();
    fixture.detectChanges();
  };

  async function create(): Promise<ComponentFixture<MyTargetsComponent>> {
    const fixture = TestBed.createComponent(MyTargetsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function paramMap(entries: Record<string, string>): ParamMap {
    return {
      get: (key: string) => entries[key] ?? null,
      has: (key: string) => key in entries,
      getAll: (key: string) => (key in entries ? [entries[key]] : []),
      keys: Object.keys(entries),
    };
  }

  beforeEach(async () => {
    // Pin the year query param so the component requests the same year (2027) the mocked
    // `MyTargetsApiService` responses use, instead of defaulting to the real current year.
    queryParamMap = new BehaviorSubject(paramMap({ year: '2027' }));
    api = jasmine.createSpyObj<MyTargetsApiService>('MyTargetsApiService', [
      'year',
      'confirm',
      'feedback',
      'reply',
      'progress',
    ]);
    api.year.and.returnValue(of(yearData()));
    api.progress.and.returnValue(of(progressData()));

    await TestBed.configureTestingModule({
      imports: [MyTargetsComponent],
      providers: [
        { provide: MyTargetsApiService, useValue: api },
        { provide: EmployeeService, useValue: { me: () => of(ME) } },
        {
          provide: KpiDefinitionService,
          useValue: { list: () => of({ items: KPIS, totalCount: 2, page: 1, pageSize: 200 }) },
        },
        {
          provide: I18nService,
          useValue: jasmine.createSpyObj(
            'I18nService',
            ['formatNumber', 'formatDate', 'translate'],
            {
              localization: new I18nService().localization,
              currentLanguage: () => 'vi',
            },
          ),
        },
        {
          provide: ActivatedRoute,
          useValue: { queryParamMap, snapshot: { queryParamMap: queryParamMap.value } },
        },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) },
      ],
    })
      .overrideComponent(MyTargetsComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();

    const i18n = TestBed.inject(I18nService);
    (i18n.formatNumber as jasmine.Spy).and.callFake((v: number | null) =>
      v === null ? 'N/A' : String(v),
    );
    (i18n.formatDate as jasmine.Spy).and.callFake((v: string | null) => (v ? String(v) : '-'));
    (i18n.translate as jasmine.Spy).and.callFake(
      (key: string, params?: Record<string, unknown>) => {
        const path = key.split('.');
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        let node: any =
          path[0] === 'targetEmp'
            ? new I18nService().localization.targetEmp
            : new I18nService().localization;
        for (const segment of path.slice(1)) node = node?.[segment];
        let resolved = String(node ?? key);
        for (const [k, v] of Object.entries(params ?? {})) {
          resolved = resolved.replace(new RegExp(`\\{${k}\\}`, 'g'), String(v));
        }
        return resolved;
      },
    );
  });

  it('shows the pending banner, summary cards and confirm action', async () => {
    const fixture = await create();

    expect(api.year).toHaveBeenCalledWith(2027);
    expect(text(root(fixture).querySelector('.banner'))).toContain(
      'Trưởng phòng đã giao 2 chỉ tiêu',
    );
    expect(text(root(fixture).querySelector('#my-targets-confirm'))).toContain(
      'Xác nhận 2 chỉ tiêu',
    );
    const cards = root(fixture).querySelectorAll('.summary-card strong');
    expect(text(cards[0])).toBe('2');
    expect(root(fixture).querySelectorAll('.targets-table tbody tr').length).toBe(2);
  });

  it('opens the confirm dialog then confirms', async () => {
    api.confirm.and.returnValue(
      of({ year: 2027, ackStatus: 'CONFIRMED', confirmedAt: '2027-01-10T09:00:00Z' }),
    );
    const fixture = await create();

    click(fixture, '#my-targets-confirm');
    expect(root(fixture).querySelector('.modal-dialog')).not.toBeNull();

    click(fixture, '.modal-footer .button-primary');
    expect(api.confirm).toHaveBeenCalledWith(2027);
    expect(api.year).toHaveBeenCalledTimes(2);
  });

  it('shows the reconfirm banner when the manager changed a KPI', async () => {
    api.year.and.returnValue(
      of(
        yearData({
          ackStatus: 'RECONFIRM',
          kpis: [
            {
              ...yearData().kpis[0],
              changed: true,
              changedOldValue: 6000,
              changedNewValue: 6400,
            },
            yearData().kpis[1],
          ],
        }),
      ),
    );
    const fixture = await create();

    expect(text(root(fixture).querySelector('.banner'))).toContain('Trưởng phòng đã sửa');
    expect(root(fixture).querySelector('#my-targets-reconfirm')).not.toBeNull();
    expect(root(fixture).querySelector('.struck')).not.toBeNull();
  });

  it('opens the feedback drawer and validates an empty reason', async () => {
    const fixture = await create();

    click(fixture, '.targets-table tbody tr .button-secondary');
    expect(root(fixture).querySelector('app-right-drawer')).not.toBeNull();

    click(fixture, '.drawer-footer .button-primary');
    expect(api.feedback).not.toHaveBeenCalled();
    expect(root(fixture).querySelector('.form-error')).not.toBeNull();
  });

  it('sends feedback with the typed reason', async () => {
    api.feedback.and.returnValue(
      of({
        id: 'f1',
        kpiId: 'k1',
        employeeId: 'e1',
        authorName: 'Phạm Thu Hà',
        reason: 'Khách lớn chuyển phòng',
        proposedYear: null,
        proposedQ1: null,
        proposedQ2: null,
        proposedQ3: null,
        proposedQ4: null,
        reply: null,
        repliedBy: null,
        repliedByName: null,
        repliedAt: null,
        createdAt: '2027-01-06T10:12:00Z',
      }),
    );
    const fixture = await create();

    click(fixture, '.targets-table tbody tr .button-secondary');
    const textarea = root(fixture).querySelector<HTMLTextAreaElement>(
      '#my-targets-feedback-reason',
    )!;
    textarea.value = 'Khách lớn chuyển phòng';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    click(fixture, '.drawer-footer .button-primary');
    expect(api.feedback).toHaveBeenCalledWith(
      2027,
      jasmine.objectContaining({ kpiId: 'k1', reason: 'Khách lớn chuyển phòng' }),
    );
  });

  it('switches to the quarter progress view and filters by bucket', async () => {
    const fixture = await create();

    queryParamMap.next(paramMap({ view: 'quarter' }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.progress).toHaveBeenCalled();
    expect(root(fixture).querySelectorAll('.targets-table tbody tr').length).toBe(1);

    click(fixture, '.chip-row .chip-filter:nth-child(2)');
    expect(root(fixture).querySelectorAll('.targets-table tbody tr').length).toBe(2);
  });

  it('shows an error state with the trace id and a retry', async () => {
    api.year.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500, error: { traceId: 'abc-123' } })),
    );
    const fixture = await create();

    expect(text(root(fixture).querySelector('.state-error'))).toContain('abc-123');

    api.year.and.returnValue(of(yearData()));
    click(fixture, '.state-error button');
    expect(root(fixture).querySelector('.targets-table')).not.toBeNull();
  });

  it('says so when the employee has no targets yet', async () => {
    api.year.and.returnValue(of(yearData({ kpis: [] })));
    const fixture = await create();

    expect(text(root(fixture).querySelector('.state-card'))).toContain(
      'Bạn chưa được giao chỉ tiêu',
    );
  });
});
