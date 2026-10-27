import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import {
  DepartmentDetail,
  DepartmentMember,
  DepartmentMembers,
  DepartmentOverview,
  DepartmentSummary,
} from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { DepartmentDetailDrawerComponent } from './department-detail-drawer.component';

function summary(code: string, extra: Partial<DepartmentSummary> = {}): DepartmentSummary {
  return {
    id: `id-${code}`,
    code,
    name: `Name ${code}`,
    levelType: 'DEPARTMENT',
    status: 'ACTIVE',
    parentDepartmentId: 'id-ROOT',
    parentDepartmentName: 'Root unit',
    employeeCount: 5,
    childDepartmentCount: 0,
    managerName: 'Boss',
    tellerCount: 2,
    salesCount: 3,
    lastSyncAt: '2026-10-01T00:00:00+00:00',
    ...extra,
  };
}

function detail(code: string, extra: Partial<DepartmentDetail> = {}): DepartmentDetail {
  return {
    id: `id-${code}`,
    code,
    name: `Name ${code}`,
    description: `About ${code}`,
    levelType: 'DEPARTMENT',
    status: 'ACTIVE',
    parentDepartmentId: 'id-ROOT',
    ancestors: [{ id: 'id-ROOT', code: 'ROOT', name: 'Root unit' }],
    manager: {
      id: 'm1',
      employeeCode: `M-${code}`,
      fullName: 'Tran Thi Binh',
      jobPositionTitle: 'Lead',
    },
    directEmployeeCount: 1,
    employeeCount: 5,
    childDepartmentCount: 0,
    lastSyncAt: '2026-10-02T03:04:00+00:00',
    updatedAt: '2026-10-02T03:04:00+00:00',
    ...extra,
  };
}

function member(n: number, prefix = 'E'): DepartmentMember {
  return {
    id: `${prefix}${n}`,
    employeeCode: `${prefix}${String(n).padStart(3, '0')}`,
    fullName: `Employee ${prefix}${n}`,
    departmentId: 'd',
    departmentName: 'D',
    jobPositionId: 'p',
    jobPositionTitle: 'Teller',
    status: 'ACTIVE',
    openWorkCount: 0,
    performanceScore: null,
  };
}

function roster(count: number, totalCount = count, prefix = 'E'): DepartmentMembers {
  return {
    items: Array.from({ length: count }, (_, i) => member(i + 1, prefix)),
    totalCount,
    page: 1,
    pageSize: 100,
  };
}

function overview(kpiCount: number, period: string | null = 'Tháng 10/2026'): DepartmentOverview {
  return {
    period: period
      ? {
          id: 'p1',
          code: 'P',
          name: period,
          periodType: 'MONTH',
          startDate: '2026-10-01',
          endDate: '2026-10-31',
        }
      : null,
    performance: {
      kpis: Array.from({ length: kpiCount }, (_, i) => ({
        code: `K${i}`,
        achievementPercent: 35,
      })),
    },
  } as unknown as DepartmentOverview;
}

describe('DepartmentDetailDrawerComponent', () => {
  const api = {
    detail: jasmine.createSpy('detail'),
    members: jasmine.createSpy('members'),
    overview: jasmine.createSpy('overview'),
  };
  let fixture: ComponentFixture<DepartmentDetailDrawerComponent>;
  let i18n: I18nService;

  const root = () => fixture.nativeElement as HTMLElement;
  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  function open(department: DepartmentSummary) {
    fixture = TestBed.createComponent(DepartmentDetailDrawerComponent);
    fixture.componentRef.setInput('department', department);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    for (const spy of Object.values(api)) spy.calls.reset();
    api.detail.and.callFake((id: string) => of(detail(id.replace('id-', ''))));
    api.members.and.callFake(() => of(roster(5)));
    api.overview.and.callFake(() => of(overview(4)));
    TestBed.configureTestingModule({
      imports: [DepartmentDetailDrawerComponent],
      providers: [provideRouter([]), { provide: DepartmentService, useValue: api }],
    });
    i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
  });

  it('calls the department detail, roster and overview endpoints for the selected department', () => {
    open(summary('D1'));

    expect(api.detail).toHaveBeenCalledOnceWith('id-D1');
    expect(api.members).toHaveBeenCalledOnceWith('id-D1', { pageSize: 100 });
    expect(api.overview).toHaveBeenCalledOnceWith('id-D1');
  });

  it('renders manager, structure counts and the roster preview from API data matching mockup', () => {
    open(summary('D1'));

    expect(text(root().querySelector('.drawer-title'))).toBe('D1 · Name D1');
    expect(text(root().querySelector('.drawer-subtitle'))).toBe('5 nhân viên');
    expect(text(root().querySelector('.manager-name'))).toBe('Tran Thi Binh');
    expect(text(root().querySelector('.manager-meta'))).toBe('M-D1 · Lead');
    expect(Array.from(root().querySelectorAll('.structure-value')).map((v) => text(v))).toEqual([
      '2',
      '3',
    ]);
    expect(root().querySelectorAll('.mini-table tbody tr').length).toBe(3); // preview
    expect(root().querySelector('.emp-link')?.getAttribute('href')).toBe('/employee-detail/E001');
  });

  it('reloads when the list selects another department and ignores the previous request', () => {
    const first = new Subject<DepartmentDetail>();
    api.detail.and.callFake((id: string) => (id === 'id-D1' ? first : of(detail('D2'))));
    open(summary('D1'));
    expect(root().querySelector('[role="status"]')).not.toBeNull();

    fixture.componentRef.setInput('department', summary('D2'));
    fixture.detectChanges();

    expect(api.detail).toHaveBeenCalledWith('id-D2');
    expect(api.members).toHaveBeenCalledWith('id-D2', { pageSize: 100 });
    expect(api.overview).toHaveBeenCalledWith('id-D2');
    first.next(detail('D1', { description: 'STALE' }));
    fixture.detectChanges();
    expect(text(root().querySelector('.drawer-title'))).toBe('D2 · Name D2');
  });

  it('shows the real status of the department instead of always "active"', () => {
    open(summary('D1', { status: 'INACTIVE' }));
    const badge = root().querySelector('.status-badge');

    expect(text(badge)).toBe('Ngừng hoạt động');
    expect(badge?.classList).toContain('is-muted');

    open(summary('D2', { status: 'ARCHIVED' }));
    expect(text(root().querySelector('.status-badge'))).toBe('Đã lưu trữ');
  });

  it('shows an error with retry when any request fails and does not leave a half-filled drawer', () => {
    api.members.and.returnValues(
      throwError(() => new Error('500')),
      of(roster(5)),
    );
    open(summary('D1'));

    expect(root().querySelector('[role="alert"]')).not.toBeNull();

    root().querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    fixture.detectChanges();

    expect(api.detail).toHaveBeenCalledTimes(2);
    expect(root().querySelector('[role="alert"]')).toBeNull();
  });

  it('renders N/A for a missing manager rather than a made-up value', () => {
    api.detail.and.returnValue(
      of(detail('D1', { manager: null, ancestors: [], description: null })),
    );
    open(
      summary('D1', { managerName: null, parentDepartmentId: null, parentDepartmentName: null }),
    );

    expect(text(root().querySelector('.section-block:first-of-type'))).toContain('N/A');
    expect(root().querySelector('.manager-avatar')).toBeNull();
  });

  it('expands the roster and collapses it again', () => {
    api.members.and.returnValue(of(roster(8, 250)));
    open(summary('D1'));
    const toggle = () => root().querySelector<HTMLButtonElement>('.link-btn');

    expect(text(toggle())).toBe('Xem tất cả 250 nhân viên');
    expect(toggle()?.getAttribute('aria-expanded')).toBe('false');
    expect(root().querySelectorAll('.mini-table tbody tr').length).toBe(3);

    toggle()?.click();
    fixture.detectChanges();
    expect(root().querySelectorAll('.mini-table tbody tr').length).toBe(8);
    expect(text(toggle())).toBe('Thu gọn danh sách');

    toggle()?.click();
    fixture.detectChanges();
    expect(root().querySelectorAll('.mini-table tbody tr').length).toBe(3);
  });

  it('shows the empty roster state and no expand control for a small roster', () => {
    api.members.and.returnValue(of(roster(0)));
    open(summary('D1'));
    expect(text(root().querySelector('.mini-table .empty-cell'))).toBe(
      'Không tìm thấy cán bộ phù hợp.',
    );
    expect(root().querySelector('.link-btn')).toBeNull();

    api.members.and.returnValue(of(roster(2)));
    open(summary('D2'));
    expect(root().querySelector('.link-btn')).toBeNull();
  });

  it('derives the period target card from the overview matching mockup', () => {
    open(summary('D1'));
    expect(text(root().querySelector('.section-block:last-of-type .section-heading'))).toBe(
      'MỤC TIÊU KỲ Tháng 10/2026',
    );
    expect(text(root().querySelector('.target-text'))).toContain('Đã giao 35% mục tiêu chi nhánh');
    expect(text(root().querySelector('.target-link'))).toBe('Xem phân bổ');
  });

  it('emits closeDrawer from the close button, which has a localized accessible name', () => {
    open(summary('D1'));
    let closed = 0;
    fixture.componentInstance.closeDrawer.subscribe(() => closed++);
    const button = root().querySelector<HTMLButtonElement>('.close-btn');

    expect(button?.getAttribute('aria-label')).toBe('Đóng');
    button?.click();
    expect(closed).toBe(1);
  });

  it('switches text, aria labels and formatted values language live', () => {
    open(summary('D1'));
    expect(root().querySelector('aside')?.getAttribute('aria-label')).toBe('Chi tiết phòng ban');
    expect(text(root().querySelector('.drawer-subtitle'))).toBe('5 nhân viên');

    i18n.setLanguage('en');
    fixture.detectChanges();

    expect(root().querySelector('aside')?.getAttribute('aria-label')).toBe('Department details');
    expect(root().querySelector('.close-btn')?.getAttribute('aria-label')).toBe('Close');
    expect(text(root().querySelector('.drawer-subtitle'))).toBe('5 employees');
    expect(text(root().querySelector('.status-badge'))).toBe('Active');
    expect(text(root().querySelector('.section-block:last-of-type .section-heading'))).toBe(
      'PERIOD TARGET Tháng 10/2026',
    );
    expect(text(root().querySelector('.target-text'))).toContain('Assigned 35% branch target');
    expect(text(root().querySelector('.target-link'))).toBe('View allocation');
  });
});
