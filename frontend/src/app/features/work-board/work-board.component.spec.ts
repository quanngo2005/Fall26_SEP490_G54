import { HttpErrorResponse } from '@angular/common/http';
import { Component, input, output, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { DepartmentMembers } from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { JobPositionService } from '../../core/job-position.service';
import { WorkTypeService } from '../../core/work-type.service';
import { WorkDetail, WorkListItem, WorkStatus } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { WorkBoardComponent } from './work-board.component';
import { WorkDetailDrawerComponent } from './work-detail-drawer.component';

@Component({ selector: 'app-header', standalone: true, template: '' })
class HeaderStub {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class SidebarStub {}

@Component({ selector: 'app-work-detail-drawer', standalone: true, template: '' })
class DrawerStub {
  readonly workId = input.required<string>();
  readonly closed = output<void>();
}

function work(code: string, status: WorkStatus, extra: Partial<WorkListItem> = {}): WorkListItem {
  return {
    id: `id-${code}`,
    code,
    title: `Title ${code}`,
    workTypeId: 't1',
    workTypeName: 'Mở thẻ tín dụng',
    businessCampaignId: null,
    assigneeEmployeeId: 'e1',
    assigneeEmployeeCode: 'NV001',
    assigneeEmployeeName: 'Nguyễn Văn A',
    departmentId: 'd1',
    departmentCode: 'D1',
    departmentName: 'Phòng 1',
    priority: 'Medium',
    status,
    plannedStartDate: '2099-01-01',
    plannedEndDate: '2099-12-31',
    updatedAt: '2026-10-20T08:00:00+00:00',
    ...extra,
  };
}

describe('WorkBoardComponent', () => {
  let works: jasmine.SpyObj<WorkService>;
  let data: WorkListItem[];
  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const root = (fixture: ComponentFixture<unknown>) => fixture.nativeElement as HTMLElement;
  const column = (fixture: ComponentFixture<unknown>, title: string) =>
    Array.from(root(fixture).querySelectorAll<HTMLElement>('.column')).find(
      (el) => text(el.querySelector('h2')) === title,
    ) as HTMLElement;
  const cards = (el: HTMLElement) => Array.from(el.querySelectorAll<HTMLElement>('.work-card'));

  let departments: jasmine.SpyObj<DepartmentService>;

  function create(): ComponentFixture<WorkBoardComponent> {
    const fixture = TestBed.createComponent(WorkBoardComponent);
    fixture.detectChanges();
    return fixture;
  }

  /** Signs in with the given role; a department or branch manager also oversees the department's work. */
  async function configure(role: 'EMPLOYEE' | 'DEPT_MANAGER' | 'BRANCH_MANAGER'): Promise<void> {
    employees.me.and.returnValue(of({ id: 'e1', departmentId: 'd1' } as EmployeeDetail));
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [WorkBoardComponent],
      providers: [
        { provide: WorkService, useValue: works },
        { provide: EmployeeService, useValue: employees },
        { provide: DepartmentService, useValue: departments },
        { provide: WorkTypeService, useValue: workTypes },
        { provide: JobPositionService, useValue: jobPositions },
        { provide: AuthService, useValue: { currentUser: signal({ roles: [role] }) } },
      ],
    })
      .overrideComponent(WorkBoardComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent, WorkDetailDrawerComponent] },
        add: { imports: [HeaderStub, SidebarStub, DrawerStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  }

  let employees: jasmine.SpyObj<EmployeeService>;
  let workTypes: jasmine.SpyObj<WorkTypeService>;
  let jobPositions: jasmine.SpyObj<JobPositionService>;

  beforeEach(async () => {
    data = [
      work('WK-1', 'New', { plannedEndDate: '2020-01-01' }),
      work('WK-2', 'New'),
      work('WK-3', 'InProgress'),
      work('WK-4', 'Rejected'),
      work('WK-5', 'Submitted'),
      work('WK-6', 'Done'),
      work('WK-7', 'Done'),
      work('WK-8', 'Done'),
      work('WK-9', 'Overdue', { plannedEndDate: '2020-01-01' }),
    ];
    works = jasmine.createSpyObj<WorkService>('WorkService', ['list', 'detail', 'changeStatus']);
    works.list.and.callFake((filter = {}) => {
      const items = data.filter((item) => filter.status?.includes(item.status) ?? true);
      return of({ items, totalCount: items.length, page: filter.page ?? 1, pageSize: 50 });
    });
    employees = jasmine.createSpyObj<EmployeeService>('EmployeeService', ['me']);
    departments = jasmine.createSpyObj<DepartmentService>('DepartmentService', ['members']);
    departments.members.and.returnValue(
      of({
        items: [
          { id: 'e1', employeeCode: 'NV001', fullName: 'Nguyễn Văn A', jobPositionId: 'p1' },
          { id: 'e2', employeeCode: 'NV002', fullName: 'Phạm Văn D', jobPositionId: 'p2' },
        ],
        totalCount: 2,
        page: 1,
        pageSize: 100,
      } as unknown as DepartmentMembers),
    );
    jobPositions = jasmine.createSpyObj<JobPositionService>('JobPositionService', ['list']);
    jobPositions.list.and.returnValue(
      of({
        items: [
          { id: 'p1', title: 'Giao dịch viên' } as never,
          { id: 'p2', title: 'Chuyên viên kinh doanh' } as never,
        ],
        totalCount: 1,
        page: 1,
        pageSize: 100,
      }),
    );
    workTypes = jasmine.createSpyObj<WorkTypeService>('WorkTypeService', ['list']);
    workTypes.list.and.returnValue(
      of({
        items: [{ id: 't1', name: 'Mở thẻ tín dụng' } as never],
        totalCount: 1,
        page: 1,
        pageSize: 100,
      }),
    );
    await configure('EMPLOYEE');
  });

  it('loads one query per column for the signed-in employee and renders the cards', () => {
    const fixture = create();

    expect(works.list).toHaveBeenCalledTimes(5);
    const inProgressCall = works.list.calls
      .allArgs()
      .map(([filter]) => filter)
      .find((filter) => filter?.status?.includes('InProgress'));
    expect(inProgressCall).toEqual(
      jasmine.objectContaining({
        assigneeEmployeeId: 'e1',
        status: ['InProgress', 'HandedOver', 'Rejected'],
        page: 1,
        pageSize: 50,
      }),
    );
    expect(cards(column(fixture, 'Mới')).length).toBe(2);
    expect(text(column(fixture, 'Mới').querySelector('.column-count'))).toBe('2');
    expect(cards(column(fixture, 'Đang thực hiện')).length).toBe(2);
    expect(cards(column(fixture, 'Đã nộp')).length).toBe(1);
    expect(cards(column(fixture, 'Quá hạn')).length).toBe(1);
    expect(text(root(fixture).querySelector('.result-count'))).toBe('9 việc');
  });

  it('shows an employee only their own work, with no way to switch to the department', () => {
    const fixture = create();

    expect(root(fixture).querySelector('#work-scope-team')).toBeNull();
    expect(works.list.calls.first().args[0]).toEqual(
      jasmine.objectContaining({ assigneeEmployeeId: 'e1' }),
    );
    expect(works.list.calls.first().args[0]?.departmentId).toBeUndefined();
    expect(departments.members).not.toHaveBeenCalled();
    expect(jobPositions.list).not.toHaveBeenCalled();
    expect(text(root(fixture).querySelector('h1'))).toBe('Công việc của tôi');
  });

  it('refuses the department scope for an employee even when asked for it directly', () => {
    const fixture = create();
    works.list.calls.reset();

    fixture.componentInstance['chooseScope']('team');
    fixture.detectChanges();

    expect(works.list).not.toHaveBeenCalled();
    expect(text(root(fixture).querySelector('h1'))).toBe('Công việc của tôi');
  });
  it('shows the late badge on an overdue card and a KPI overdue count', () => {
    const fixture = create();

    expect(text(cards(column(fixture, 'Quá hạn'))[0].querySelector('.badge-overdue'))).toMatch(
      /^-\d+ ngày$/,
    );
    expect(text(root(fixture).querySelector('#work-kpi-overdue .kpi-value'))).toBe('1');
    expect(text(root(fixture).querySelector('#work-kpi-open .kpi-value'))).toBe('5');
  });

  it('keeps the Done column collapsed to two cards until expanded', () => {
    const fixture = create();
    const done = column(fixture, 'Hoàn thành');

    expect(cards(done).length).toBe(2);
    const more = done.querySelector<HTMLButtonElement>('.more-button');
    expect(text(more)).toBe('Xem thêm 1 việc');
    more?.click();
    fixture.detectChanges();
    expect(cards(column(fixture, 'Hoàn thành')).length).toBe(3);
  });

  it('starts a work, moving it to In progress, and keeps it there on success', async () => {
    works.changeStatus.and.returnValue(
      of({ status: 'InProgress', updatedAt: '2026-10-27T10:00:00+00:00' } as WorkDetail),
    );
    const fixture = await create();

    column(fixture, 'Mới').querySelector<HTMLButtonElement>('.start-button')?.click();
    fixture.detectChanges();

    expect(works.changeStatus).toHaveBeenCalledOnceWith('id-WK-1', { status: 'InProgress' });
    expect(cards(column(fixture, 'Mới')).length).toBe(1);
    expect(cards(column(fixture, 'Đang thực hiện')).length).toBe(3);
    expect(text(root(fixture).querySelector('.toast'))).toContain('WK-1');
  });

  it('puts the card back and explains the conflict when the API refuses the move', async () => {
    works.changeStatus.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 409, error: { errorCode: 'WORK_INVALID_STATE' } }),
      ),
    );
    const fixture = await create();

    column(fixture, 'Mới').querySelector<HTMLButtonElement>('.start-button')?.click();
    fixture.detectChanges();

    expect(cards(column(fixture, 'Mới')).length).toBe(2);
    expect(cards(column(fixture, 'Đang thực hiện')).length).toBe(2);
    const toast = root(fixture).querySelector('.toast');
    expect(toast?.classList).toContain('toast-error');
    expect(text(toast)).toContain('WK-1');
  });

  it('offers no start action on cards that cannot be started', () => {
    const fixture = create();

    expect(column(fixture, 'Đã nộp').querySelector('.start-button')).toBeNull();
    expect(column(fixture, 'Quá hạn').querySelector('.start-button')).toBeNull();
    expect(column(fixture, 'Đang thực hiện').querySelectorAll('.start-button').length).toBe(1);
  });

  it('keeps only the Overdue column when the overdue KPI card is toggled on', () => {
    const fixture = create();

    root(fixture).querySelector<HTMLButtonElement>('#work-kpi-overdue')?.click();
    fixture.detectChanges();

    const titles = Array.from(root(fixture).querySelectorAll('.column h2')).map((el) => text(el));
    expect(titles).toEqual(['Quá hạn']);
  });

  it('switches to the list view and requests one page of works', () => {
    const fixture = create();
    works.list.calls.reset();

    root(fixture).querySelector<HTMLButtonElement>('#work-view-list')?.click();
    fixture.detectChanges();

    expect(works.list).toHaveBeenCalledOnceWith(
      jasmine.objectContaining({ assigneeEmployeeId: 'e1', page: 1, pageSize: 20 }),
    );
    expect(root(fixture).querySelectorAll('.work-row').length).toBe(9);
  });

  it('shows a column error when one query fails', async () => {
    works.list.and.callFake((filter = {}) =>
      filter.status?.includes('Done')
        ? throwError(() => new HttpErrorResponse({ status: 500 }))
        : of({ items: [], totalCount: 0, page: 1, pageSize: 50 }),
    );
    const fixture = await create();

    expect(column(fixture, 'Hoàn thành').querySelector('.column-note-error')).not.toBeNull();
    expect(column(fixture, 'Mới').querySelector('.column-note-error')).toBeNull();
  });

  describe('as a manager', () => {
    beforeEach(async () => {
      data = [
        work('WK-1042', 'New', { priority: 'High' }),
        work('WK-1047', 'New', { assigneeEmployeeId: 'e2', assigneeEmployeeName: 'Phạm Văn D' }),
        work('WK-1038', 'InProgress'),
        work('WK-1036', 'InProgress', {
          assigneeEmployeeId: 'e2',
          assigneeEmployeeName: 'Phạm Văn D',
        }),
        work('WK-1031', 'Submitted'),
        work('WK-1010', 'Submitted', { workTypeId: null, workTypeName: null }),
        work('WK-1027', 'Verified', {
          assigneeEmployeeId: 'e2',
          assigneeEmployeeName: 'Phạm Văn D',
        }),
        work('WK-1019', 'Done'),
        work('WK-1022', 'Overdue', {
          plannedEndDate: '2020-01-01',
          assigneeEmployeeId: 'e3',
          assigneeEmployeeName: 'Hoàng Thị E',
        }),
      ];
      await configure('DEPT_MANAGER');
    });

    it("opens on the department, and can switch to the manager's own work and back", () => {
      const fixture = create();
      const scopeButton = (id: string) => root(fixture).querySelector<HTMLButtonElement>(id)!;
      expect(text(root(fixture).querySelector('h1'))).toBe('Công việc của phòng');
      expect(scopeButton('#work-scope-team').getAttribute('aria-pressed')).toBe('true');

      works.list.calls.reset();
      scopeButton('#work-scope-mine').click();
      fixture.detectChanges();

      expect(text(root(fixture).querySelector('h1'))).toBe('Công việc của tôi');
      expect(works.list).toHaveBeenCalledTimes(5);
      for (const [filter] of works.list.calls.allArgs()) {
        expect(filter).toEqual(jasmine.objectContaining({ assigneeEmployeeId: 'e1' }));
        expect(filter?.departmentId).toBeUndefined();
      }
      expect(root(fixture).querySelector('#work-filter-employee')).toBeNull();
      expect(root(fixture).querySelector('#work-kpi-due-today')).not.toBeNull();

      works.list.calls.reset();
      scopeButton('#work-scope-team').click();
      fixture.detectChanges();

      expect(text(root(fixture).querySelector('h1'))).toBe('Công việc của phòng');
      expect(works.list.calls.first().args[0]).toEqual(
        jasmine.objectContaining({ departmentId: 'd1', includeChildDepartments: true }),
      );
    });

    it('clears the filters of one scope when switching to the other', () => {
      const fixture = create();
      root(fixture).querySelector<HTMLButtonElement>('#work-kpi-overdue')?.click();
      fixture.detectChanges();
      expect(root(fixture).querySelectorAll('.column h2').length).toBe(1);

      root(fixture).querySelector<HTMLButtonElement>('#work-scope-mine')?.click();
      fixture.detectChanges();

      expect(root(fixture).querySelectorAll('.column h2').length).toBe(5);
    });

    it('offers the start action on my own board, not the complete drag of the department board', () => {
      const fixture = create();

      root(fixture).querySelector<HTMLButtonElement>('#work-scope-mine')?.click();
      fixture.detectChanges();

      expect(root(fixture).querySelectorAll('.start-button').length).toBeGreaterThan(0);
    });

    it('gives a branch manager the same department board', async () => {
      await configure('BRANCH_MANAGER');
      const fixture = create();

      expect(root(fixture).querySelector('#work-scope-team')).not.toBeNull();
      expect(text(root(fixture).querySelector('h1'))).toBe('Công việc của phòng');
    });

    it('asks for the department with its child departments, not for one employee', () => {
      create();

      expect(works.list).toHaveBeenCalledTimes(5);
      expect(works.list.calls.first().args[0]).toEqual(
        jasmine.objectContaining({ departmentId: 'd1', includeChildDepartments: true }),
      );
      for (const [filter] of works.list.calls.allArgs()) {
        expect(filter?.assigneeEmployeeId).toBeFalsy();
      }
    });

    it('renders the 5 columns, with Verified work sharing the Done column', () => {
      const fixture = create();

      const titles = Array.from(root(fixture).querySelectorAll('.column h2')).map((el) => text(el));
      expect(titles).toEqual(['Mới', 'Đang thực hiện', 'Đã nộp', 'Hoàn thành', 'Quá hạn']);
      expect(cards(column(fixture, 'Đã nộp')).length).toBe(2);
      expect(cards(column(fixture, 'Hoàn thành')).length).toBe(2);
      const doneCall = works.list.calls
        .allArgs()
        .map(([filter]) => filter)
        .find((filter) => filter?.status?.includes('Done'));
      expect(doneCall?.status).toEqual(['Verified', 'Done']);
    });

    it('labels each Done card with its real status and shows the assignee', () => {
      const fixture = create();

      const done = cards(column(fixture, 'Hoàn thành'));
      expect(done.map((card) => text(card.querySelector('.chip'))).sort()).toEqual([
        'Hoàn thành',
        'Đã thẩm định',
      ]);
      const first = cards(column(fixture, 'Mới'))[0];
      expect(text(first.querySelector('.avatar-circle'))).toBe('VA');
      expect(text(first.querySelector('.assignee-name'))).toBe('Nguyễn Văn A');
    });

    it('offers only the open and overdue KPI cards, from the loaded counts', () => {
      const fixture = create();

      expect(root(fixture).querySelectorAll('.kpi').length).toBe(2);
      expect(text(root(fixture).querySelector('#work-kpi-open .kpi-value'))).toBe('6');
      expect(text(root(fixture).querySelector('#work-kpi-overdue .kpi-value'))).toBe('1');
      expect(text(root(fixture).querySelector('#work-kpi-open .kpi-hint'))).toBe('2 nhân viên');
      expect(text(root(fixture).querySelector('#work-kpi-overdue .tag'))).toBe('Cần xử lý');
    });

    it('shows zero, not an invented number, when the department has no work', () => {
      works.list.and.callFake((filter = {}) =>
        of({ items: [], totalCount: 0, page: filter.page ?? 1, pageSize: 50 }),
      );
      const fixture = create();

      expect(text(root(fixture).querySelector('#work-kpi-open .kpi-value'))).toBe('0');
      expect(text(root(fixture).querySelector('#work-kpi-open .kpi-hint'))).toBe('0 nhân viên');
    });

    it('narrows the columns with the open KPI card', () => {
      const fixture = create();

      root(fixture).querySelector<HTMLButtonElement>('#work-kpi-open')?.click();
      fixture.detectChanges();

      const titles = Array.from(root(fixture).querySelectorAll('.column h2')).map((el) => text(el));
      expect(titles).toEqual(['Mới', 'Đang thực hiện', 'Đã nộp']);
    });

    it('sends the chosen employee to the server', () => {
      const fixture = create();
      works.list.calls.reset();

      const select = root(fixture).querySelector<HTMLSelectElement>('#work-filter-employee')!;
      select.value = 'e2';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(works.list).toHaveBeenCalledTimes(5);
      for (const [filter] of works.list.calls.allArgs()) {
        expect(filter).toEqual(jasmine.objectContaining({ assigneeEmployeeId: 'e2' }));
      }
    });

    it('filters by position among the loaded cards, using the department members', () => {
      const fixture = create();
      expect(text(root(fixture).querySelector('.result-count'))).toBe('9 việc');

      const position = root(fixture).querySelector<HTMLSelectElement>('#work-filter-position')!;
      position.value = 'p2';
      position.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      // Only the cards of Phạm Văn D (position p2); the one of e3 is not a known member, so it is not shown.
      const titles = Array.from(root(fixture).querySelectorAll('.work-card .card-code')).map((el) =>
        text(el),
      );
      expect(titles.sort()).toEqual(['WK-1027', 'WK-1036', 'WK-1047']);
      expect(text(root(fixture).querySelector('.result-count'))).toBe('3 việc');
    });
    it('has no start action, and a Verified work can be completed by dragging into Done', () => {
      works.changeStatus.and.returnValue(
        of({ status: 'Done', updatedAt: '2026-10-28T09:00:00+00:00' } as WorkDetail),
      );
      const fixture = create();

      expect(root(fixture).querySelector('.start-button')).toBeNull();
      const verified = data.find((i) => i.code === 'WK-1027')!;
      fixture.componentInstance['changeStatus'](verified);
      fixture.detectChanges();

      expect(works.changeStatus).toHaveBeenCalledOnceWith('id-WK-1027', { status: 'Done' });
      expect(text(root(fixture).querySelector('.toast'))).toContain('WK-1027');
      const done = column(fixture, 'Hoàn thành');
      expect(cards(done).length).toBe(2); // moved in place, not duplicated
      expect(cards(done).map((card) => text(card.querySelector('.chip')))).toEqual([
        'Hoàn thành',
        'Hoàn thành',
      ]);
    });

    it('puts the card back and says only the direct manager may complete on a 403', () => {
      works.changeStatus.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
      const fixture = create();

      fixture.componentInstance['changeStatus'](data.find((i) => i.code === 'WK-1027')!);
      fixture.detectChanges();

      const toast = root(fixture).querySelector('.toast');
      expect(toast?.classList).toContain('toast-error');
      expect(text(toast)).toContain('quản lý trực tiếp');
      const chips = cards(column(fixture, 'Hoàn thành')).map((card) =>
        text(card.querySelector('.chip')),
      );
      expect(chips.sort()).toEqual(['Hoàn thành', 'Đã thẩm định']);
    });

    it('does nothing for a work that is not Verified', () => {
      const fixture = create();

      fixture.componentInstance['changeStatus'](data.find((i) => i.code === 'WK-1038')!);

      expect(works.changeStatus).not.toHaveBeenCalled();
    });

    it('requests one page of the whole department in the list view and adds an assignee column', () => {
      const fixture = create();
      works.list.calls.reset();

      root(fixture).querySelector<HTMLButtonElement>('#work-view-list')?.click();
      fixture.detectChanges();

      expect(works.list).toHaveBeenCalledOnceWith(
        jasmine.objectContaining({
          departmentId: 'd1',
          includeChildDepartments: true,
          pageSize: 20,
        }),
      );
      expect(root(fixture).querySelectorAll('.work-row').length).toBe(9);
      expect(root(fixture).querySelectorAll('th').length).toBe(8);
    });

    it('shows no invented initials when the assignee has no name', () => {
      const fixture = create();

      expect(fixture.componentInstance['initialsOf'](null)).toBe('');
      expect(fixture.componentInstance['initialsOf']('Nguyễn Văn A')).toBe('VA');
    });
  });
});
