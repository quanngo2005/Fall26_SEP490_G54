import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { DepartmentMember, DepartmentSummary } from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { WorkListItem, WorkStatus } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { WorkWorkloadMonitorComponent } from './work-workload-monitor.component';

function workItem(
  code: string,
  status: WorkStatus,
  extra: Partial<WorkListItem> = {},
): WorkListItem {
  return {
    id: `id-${code}`,
    code,
    title: `Công việc ${code}`,
    workTypeId: 'wt-1',
    workTypeName: 'Mở thẻ tín dụng',
    businessCampaignId: null,
    assigneeEmployeeId: 'emp-1',
    assigneeEmployeeCode: 'NV001',
    assigneeEmployeeName: 'Hoàng Thị E',
    departmentId: 'dept-1',
    departmentCode: 'PGD1',
    departmentName: 'Phòng Giao dịch 1',
    priority: 'Medium',
    status,
    plannedStartDate: '2026-10-01',
    plannedEndDate: '2026-10-30',
    updatedAt: '2026-10-20T08:00:00+00:00',
    ...extra,
  };
}

function memberItem(id: string, fullName: string, jobPositionTitle: string): DepartmentMember {
  return {
    id,
    employeeCode: id.toUpperCase(),
    fullName,
    departmentId: 'dept-1',
    departmentName: 'Phòng Giao dịch 1',
    jobPositionId: 'pos-1',
    jobPositionTitle,
    status: 'ACTIVE',
    openWorkCount: 0,
    performanceScore: null,
    evaluationStatus: null,
    previousPerformanceScore: null,
    changeFromPrevious: null,
    groups: [],
    weakestProfileItemId: null,
    overdueWorkCount: 0,
    pendingReviewCount: 0,
  };
}

describe('WorkWorkloadMonitorComponent', () => {
  let worksApi: jasmine.SpyObj<WorkService>;
  let departmentsApi: jasmine.SpyObj<DepartmentService>;
  let i18n: I18nService;

  const fixedNow = new Date('2026-10-24T03:00:00Z'); // 2026-10-24 in UTC+7

  const defaultDepts: DepartmentSummary[] = [
    {
      id: 'dept-1',
      code: 'PGD1',
      name: 'Phòng Giao dịch 1',
      levelType: 'DEPARTMENT',
      status: 'ACTIVE',
      parentDepartmentId: null,
      parentDepartmentName: null,
      employeeCount: 3,
      childDepartmentCount: 0,
      managerName: 'Lê Văn C',
      tellerCount: 2,
      salesCount: 1,
      lastSyncAt: '2026-10-20T00:00:00Z',
    },
    {
      id: 'dept-2',
      code: 'PGD2',
      name: 'Phòng Giao dịch 2',
      levelType: 'DEPARTMENT',
      status: 'ACTIVE',
      parentDepartmentId: null,
      parentDepartmentName: null,
      employeeCount: 2,
      childDepartmentCount: 0,
      managerName: 'Trần Văn B',
      tellerCount: 1,
      salesCount: 1,
      lastSyncAt: '2026-10-20T00:00:00Z',
    },
  ];

  const defaultMembers: DepartmentMember[] = [
    memberItem('emp-1', 'Hoàng Thị E', 'Giao dịch viên'),
    memberItem('emp-2', 'Phạm Văn D', 'CV bán hàng'),
    memberItem('emp-3', 'Nguyễn Văn A', 'Giao dịch viên'),
  ];

  const defaultWorks: WorkListItem[] = [
    workItem('WK-1001', 'NEW', {
      assigneeEmployeeId: 'emp-1',
      assigneeEmployeeName: 'Hoàng Thị E',
    }),
    workItem('WK-1002', 'IN_PROGRESS', {
      assigneeEmployeeId: 'emp-1',
      assigneeEmployeeName: 'Hoàng Thị E',
    }),
    workItem('WK-1003', 'SUBMITTED', {
      assigneeEmployeeId: 'emp-2',
      assigneeEmployeeName: 'Phạm Văn D',
    }),
    workItem('WK-1004', 'VERIFIED', {
      assigneeEmployeeId: 'emp-2',
      assigneeEmployeeName: 'Phạm Văn D',
    }),
    workItem('WK-1005', 'DONE', {
      assigneeEmployeeId: 'emp-3',
      assigneeEmployeeName: 'Nguyễn Văn A',
    }),
    workItem('WK-1006', 'OVERDUE', {
      assigneeEmployeeId: 'emp-1',
      assigneeEmployeeName: 'Hoàng Thị E',
      plannedEndDate: '2026-10-23',
    }),
    workItem('WK-1007', 'OVERDUE', {
      assigneeEmployeeId: 'emp-2',
      assigneeEmployeeName: 'Phạm Văn D',
      plannedEndDate: '2026-10-22',
    }),
    // Outside WorkType: counted in KPI cards & overdue, excluded from KPI stage funnel.
    workItem('WK-1008', 'IN_PROGRESS', {
      workTypeId: null,
      workTypeName: null,
      assigneeEmployeeId: 'emp-1',
      assigneeEmployeeName: 'Hoàng Thị E',
    }),
    // Cancelled: excluded everywhere.
    workItem('WK-1009', 'CANCELLED', {
      assigneeEmployeeId: 'emp-3',
      assigneeEmployeeName: 'Nguyễn Văn A',
    }),
  ];

  function createFixture(): ComponentFixture<WorkWorkloadMonitorComponent> {
    const fixture = TestBed.createComponent(WorkWorkloadMonitorComponent);
    fixture.componentRef.setInput('defaultDepartmentId', 'dept-1');
    fixture.componentRef.setInput('now', fixedNow);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<WorkWorkloadMonitorComponent>) =>
    fixture.nativeElement as HTMLElement;
  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  beforeEach(async () => {
    worksApi = jasmine.createSpyObj<WorkService>('WorkService', ['list']);
    departmentsApi = jasmine.createSpyObj<DepartmentService>('DepartmentService', [
      'list',
      'members',
    ]);

    departmentsApi.list.and.returnValue(
      of({ items: defaultDepts, totalCount: 2, page: 1, pageSize: 100 }),
    );
    departmentsApi.members.and.returnValue(
      of({ items: defaultMembers, totalCount: 3, page: 1, pageSize: 100 }),
    );
    worksApi.list.and.returnValue(
      of({ items: defaultWorks, totalCount: defaultWorks.length, page: 1, pageSize: 100 }),
    );

    await TestBed.configureTestingModule({
      imports: [WorkWorkloadMonitorComponent],
      providers: [
        { provide: WorkService, useValue: worksApi },
        { provide: DepartmentService, useValue: departmentsApi },
      ],
    }).compileComponents();

    i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
  });

  it('Monitor_HappyPath_RendersKpisFourStageFunnelEmployeeLoadAndOverdueAlerts', () => {
    const fixture = createFixture();
    const el = root(fixture);

    // 8 non-cancelled works: 4 inProgress-group (New, 2xInProgress, Submitted), 2 completed (Verified+Done = 25%), 2 overdue
    expect(text(el.querySelector('#monitor-kpi-total'))).toBe('8');
    expect(text(el.querySelector('#monitor-kpi-in-progress'))).toBe('4');
    expect(text(el.querySelector('#monitor-kpi-completed'))).toBe('2');
    expect(text(el.querySelector('#monitor-kpi-overdue .kpi-value'))).toBe('2');

    // Funnel has exactly 4 stages (Verified is merged into Done; WK-1008 without workTypeId is excluded => 7 KPI works)
    const stages = Array.from(el.querySelectorAll<HTMLElement>('.funnel-row'));
    expect(stages.length).toBe(4);
    expect(stages.map((row) => text(row.querySelector('.funnel-label')))).toEqual([
      'Mới',
      'Đang thực hiện',
      'Đã nộp',
      'Hoàn thành',
    ]);
    expect(text(stages[0].querySelector('.funnel-stat'))).toBe('7');
    expect(text(stages[1].querySelector('.funnel-stat'))).toBe('6 · 86%');
    expect(text(stages[2].querySelector('.funnel-stat'))).toBe('3 · 43%');
    expect(text(stages[3].querySelector('.funnel-stat'))).toBe('2 · 29%');

    // Employee load table sorted by openCount desc: Hoàng Thị E (4) -> Phạm Văn D (2) -> Nguyễn Văn A (0)
    const loadRows = Array.from(el.querySelectorAll<HTMLElement>('.load-row'));
    expect(loadRows.length).toBe(3);
    expect(text(loadRows[0].querySelector('.cell-employee'))).toBe('Hoàng Thị E');
    expect(text(loadRows[0].querySelectorAll('td')[2])).toBe('4');
    expect(text(loadRows[0].querySelectorAll('td')[3])).toBe('1');
    expect(text(loadRows[1].querySelector('.cell-employee'))).toBe('Phạm Văn D');
    expect(text(loadRows[2].querySelector('.cell-employee'))).toBe('Nguyễn Văn A');

    // Overdue alerts sorted by daysLate desc: WK-1007 (-2 ngày) before WK-1006 (-1 ngày)
    const alertRows = Array.from(el.querySelectorAll<HTMLElement>('.alert-row'));
    expect(alertRows.length).toBe(2);
    expect(text(alertRows[0])).toContain('-2 ngày');
    expect(text(alertRows[0])).toContain('WK-1007');
    expect(text(alertRows[1])).toContain('-1 ngày');
    expect(text(alertRows[1])).toContain('WK-1006');
  });

  it('Monitor_FilterChanges_ReloadsDepartmentAndFiltersByPeriod', () => {
    const fixture = createFixture();
    const el = root(fixture);

    // Switch department -> reloads works & members for dept-2
    const deptSelect = el.querySelector<HTMLSelectElement>('#monitor-department-select')!;
    deptSelect.value = 'dept-2';
    deptSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(worksApi.list).toHaveBeenCalledWith(
      jasmine.objectContaining({ departmentId: 'dept-2', includeChildDepartments: true }),
    );
    expect(departmentsApi.members).toHaveBeenCalledWith('dept-2', { pageSize: 100 });

    // Switch period to 'week' (week of 2026-10-24 is Mon 2026-10-19 .. Sun 2026-10-25 -> only WK-1006, WK-1007)
    const periodSelect = el.querySelector<HTMLSelectElement>('#monitor-period-select')!;
    periodSelect.value = 'week';
    periodSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(text(el.querySelector('#monitor-kpi-total'))).toBe('2');
    expect(text(el.querySelector('#monitor-kpi-overdue .kpi-value'))).toBe('2');
  });

  it('Monitor_Interactions_EmitsOpenWorkAndTogglesOverdueHighlightAndOpensComposer', () => {
    const router = TestBed.inject(Router);
    const navSpy = spyOn(router, 'navigate').and.resolveTo(true);
    const fixture = createFixture();
    const el = root(fixture);
    const openedIds: string[] = [];
    fixture.componentInstance.openWork.subscribe((id) => openedIds.push(id));

    // Compose button navigates to SCR-WRK-07 (/work-announcements)
    const composeBtn = el.querySelector<HTMLButtonElement>('#monitor-compose-notice')!;
    expect(composeBtn.disabled).toBeFalse();
    composeBtn.click();
    expect(navSpy).toHaveBeenCalledOnceWith(['/work-announcements']);

    // Clicking overdue KPI card toggles highlight state
    const overdueBtn = el.querySelector<HTMLButtonElement>('#monitor-kpi-overdue')!;
    overdueBtn.click();
    fixture.detectChanges();
    expect(overdueBtn.getAttribute('aria-pressed')).toBe('true');
    expect(
      el.querySelector('#monitor-overdue-alerts')?.classList.contains('is-highlighted'),
    ).toBeTrue();

    // Clicking "Xem việc" emits the work ID
    const viewBtn = el.querySelector<HTMLButtonElement>('.alert-action')!;
    viewBtn.click();
    expect(openedIds).toEqual(['id-WK-1007']);
  });

  it('Monitor_ComposeButton_DisabledWithoutNotificationSendPermission', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [WorkWorkloadMonitorComponent],
      providers: [
        { provide: WorkService, useValue: worksApi },
        { provide: DepartmentService, useValue: departmentsApi },
        {
          provide: AuthService,
          useValue: {
            currentUser: signal({ roles: ['EMPLOYEE'], permissions: ['work:read'] }),
          },
        },
      ],
    });
    const fixture = createFixture();
    const composeBtn = root(fixture).querySelector<HTMLButtonElement>('#monitor-compose-notice')!;
    expect(composeBtn.disabled).toBeTrue();
  });

  it('Monitor_ErrorAndRetry_ShowsAlertAndRecoversOnRetry', () => {
    worksApi.list.and.returnValue(throwError(() => new Error('Network error')));
    const fixture = createFixture();
    const el = root(fixture);

    expect(el.querySelector('.state-error[role="alert"]')).toBeTruthy();

    worksApi.list.and.returnValue(
      of({ items: defaultWorks, totalCount: defaultWorks.length, page: 1, pageSize: 100 }),
    );
    el.querySelector<HTMLButtonElement>('#monitor-retry')!.click();
    fixture.detectChanges();

    expect(el.querySelector('.state-error')).toBeNull();
    expect(text(el.querySelector('#monitor-kpi-total'))).toBe('8');
  });

  it('Monitor_EmptyStateAndLanguageSwitch_ShowsEmptyMessagesAndUpdatesLocale', () => {
    worksApi.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 100 }));
    departmentsApi.members.and.returnValue(
      of({ items: [], totalCount: 0, page: 1, pageSize: 100 }),
    );

    const fixture = createFixture();
    const el = root(fixture);

    expect(text(el.querySelector('#monitor-kpi-total'))).toBe('0');
    expect(text(el.querySelector('#monitor-overdue-alerts .panel-empty'))).toBe(
      'Không có công việc quá hạn trong kỳ.',
    );

    i18n.setLanguage('en');
    fixture.detectChanges();

    expect(text(el.querySelector('.monitor-section-title'))).toBe('Department workload & funnel');
    expect(text(el.querySelector('#monitor-overdue-alerts .panel-empty'))).toBe(
      'No overdue works in this period.',
    );
  });
});
