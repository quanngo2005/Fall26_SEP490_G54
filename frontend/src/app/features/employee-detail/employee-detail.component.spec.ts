import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { AuthenticatedUser } from '../../core/auth.models';
import { AuthService } from '../../core/auth.service';
import { DepartmentService } from '../../core/department.service';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ProfileService } from '../../core/profile.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { EmployeeDetailComponent } from './employee-detail.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const employee: EmployeeDetail = {
  id: '5eed0000-0000-0000-0007-000000000004',
  employeeCode: 'NV004',
  fullName: 'Lê Minh Châu',
  email: 'chau@example.test',
  phone: null,
  departmentId: '5eed0000-0000-0000-0001-000000000004',
  departmentCode: 'CN-HN-KD1',
  departmentName: 'Nhóm Kinh doanh 1',
  jobPositionId: '5eed0000-0000-0000-0006-000000000001',
  jobPositionCode: 'CV-KD',
  jobPositionTitle: 'Chuyên viên Kinh doanh',
  managerId: null,
  managerCode: null,
  managerName: null,
  status: 'ACTIVE',
  directReportsCount: 0,
  lastSyncAt: '2026-10-01T00:00:00+00:00',
  updatedAt: '2026-10-01T00:00:00+00:00',
};
const withManager: EmployeeDetail = {
  ...employee,
  phone: '0901000004',
  managerId: '5eed0000-0000-0000-0007-000000000002',
  managerCode: 'NV002',
  managerName: 'Trần Thị Bình',
};
const emptyPage = { items: [], totalCount: 0, page: 1, pageSize: 20 };
const guid = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i;

describe('EmployeeDetailComponent', () => {
  const employees = {
    detail: jasmine.createSpy('detail'),
    byCode: jasmine.createSpy('byCode'),
    me: jasmine.createSpy('me'),
    list: jasmine.createSpy('list'),
  };
  const departments = { members: jasmine.createSpy('members') };
  const profiles = { updatePhone: jasmine.createSpy('updatePhone') };
  const auth = {
    currentUser: signal<AuthenticatedUser | null>(null),
    changePassword: jasmine.createSpy('changePassword'),
  };
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let navigate: jasmine.Spy;

  beforeEach(async () => {
    for (const spy of [
      ...Object.values(employees),
      departments.members,
      profiles.updatePhone,
      auth.changePassword,
    ]) {
      spy.calls.reset();
    }
    employees.list.and.returnValue(of(emptyPage));
    employees.byCode.and.returnValue(of(employee));
    employees.me.and.returnValue(of(employee));
    employees.detail.and.returnValue(of(employee));
    departments.members.and.returnValue(of(emptyPage));
    auth.currentUser.set({
      id: 'account-9',
      email: 'someone.else@example.test',
      fullName: 'Someone Else',
      roles: ['EMPLOYEE'],
      permissions: [],
    });
    params = new BehaviorSubject(convertToParamMap({}));
    query = new BehaviorSubject(convertToParamMap({}));
    await TestBed.configureTestingModule({
      imports: [EmployeeDetailComponent],
      providers: [
        provideRouter([]),
        { provide: EmployeeService, useValue: employees },
        { provide: DepartmentService, useValue: departments },
        { provide: ProfileService, useValue: profiles },
        { provide: AuthService, useValue: auth },
        { provide: ActivatedRoute, useValue: { paramMap: params, queryParamMap: query } },
      ],
    })
      .overrideComponent(EmployeeDetailComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
  });

  async function render(): Promise<ComponentFixture<EmployeeDetailComponent>> {
    const fixture = TestBed.createComponent(EmployeeDetailComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const actions = (root: HTMLElement) => root.querySelector('.hero-actions');

  it('shows a real empty picker without sample employees and switches language live', async () => {
    const fixture = await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(employees.byCode).not.toHaveBeenCalled();
    expect(employees.me).not.toHaveBeenCalled();
    expect(root.textContent).toContain('Không tìm thấy cán bộ');
    expect(root.textContent).not.toContain('Nguyễn Văn An');
    expect(root.querySelector('input[type="search"]')?.getAttribute('aria-label')).toBe(
      'Tìm theo tên, mã cán bộ hoặc email...',
    );
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(root.textContent).toContain('No employees found');
    expect(root.querySelector('input[type="search"]')?.getAttribute('aria-label')).toBe(
      'Search by name, employee code or email...',
    );
  });

  // ---- keys in the URL --------------------------------------------------------------------------

  it('loads the employee by the code in the path and renders N/A for missing data', async () => {
    params.next(convertToParamMap({ code: 'NV004' }));
    const fixture = await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(employees.byCode).toHaveBeenCalledWith('NV004');
    expect(employees.detail).not.toHaveBeenCalled();
    expect(departments.members).toHaveBeenCalledWith(employee.departmentId, {
      keyword: 'NV004',
      pageSize: 10,
    });
    expect(root.textContent).toContain('Lê Minh Châu');
    expect(root.textContent).toContain('N/A');
    expect(root.textContent).not.toContain('(024)');
  });

  it('loads the signed-in user\'s own record for the "me" key', async () => {
    params.next(convertToParamMap({ code: 'me' }));
    await render();

    expect(employees.me).toHaveBeenCalledTimes(1);
    expect(employees.byCode).not.toHaveBeenCalled();
  });

  it('resolves a legacy GUID link and swaps the GUID in the address for the code', async () => {
    for (const legacy of [
      () => query.next(convertToParamMap({ id: employee.id })),
      () => params.next(convertToParamMap({ code: employee.id })),
    ]) {
      employees.detail.calls.reset();
      navigate.calls.reset();
      legacy();
      await render();

      expect(employees.detail).toHaveBeenCalledWith(employee.id);
      expect(employees.byCode).not.toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledOnceWith(['/employee-detail', 'NV004'], {
        replaceUrl: true,
      });
    }
  });

  it('shows roster figures when present', async () => {
    params.next(convertToParamMap({ code: 'NV004' }));
    departments.members.and.returnValue(
      of({
        items: [
          {
            id: employee.id,
            employeeCode: 'NV004',
            fullName: employee.fullName,
            departmentId: employee.departmentId,
            departmentName: employee.departmentName,
            jobPositionId: employee.jobPositionId,
            jobPositionTitle: employee.jobPositionTitle,
            status: 'ACTIVE',
            openWorkCount: 7,
            performanceScore: 88.5,
          },
        ],
        totalCount: 1,
        page: 1,
        pageSize: 10,
      }),
    );
    const fixture = await render();
    expect(fixture.nativeElement.textContent).toContain('88,5');
    expect(fixture.nativeElement.textContent).toContain('7');
  });

  it('still renders the profile when the department roster is forbidden', async () => {
    params.next(convertToParamMap({ code: 'NV004' }));
    departments.members.and.returnValue(throwError(() => new Error('403')));
    const fixture = await render();
    expect(fixture.nativeElement.textContent).toContain('Lê Minh Châu');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('shows an error with retry and never substitutes sample data', async () => {
    params.next(convertToParamMap({ code: 'NV004' }));
    employees.byCode.and.returnValue(throwError(() => new Error('500')));
    const fixture = await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Không thể tải');
    expect(root.textContent).not.toContain('Nguyễn Văn An');
    employees.byCode.and.returnValue(of(employee));
    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(root.textContent).toContain('Lê Minh Châu');
  });

  // ---- no GUID in any link ----------------------------------------------------------------------

  it('links to the manager and the department by code, never by GUID', async () => {
    params.next(convertToParamMap({ code: 'NV004' }));
    employees.byCode.and.returnValue(of(withManager));
    const fixture = await render();
    const hrefs = Array.from(fixture.nativeElement.querySelectorAll('a[href]')).map((a) =>
      decodeURIComponent((a as HTMLAnchorElement).getAttribute('href') ?? ''),
    );

    expect(hrefs).toContain('/employee-detail/NV002');
    expect(hrefs).toContain('/departments?department=CN-HN-KD1');
    expect(hrefs.filter((href) => guid.test(href))).toEqual([]);
  });

  it('links each picker row by employee code, never by GUID', async () => {
    employees.list.and.returnValue(
      of({
        items: [employee, { ...withManager, id: 'another-id', employeeCode: 'NV005' }],
        totalCount: 2,
        page: 1,
        pageSize: 20,
      }),
    );
    const fixture = await render();
    const root: HTMLElement = fixture.nativeElement;
    const hrefs = Array.from(root.querySelectorAll('td a[href]')).map((a) =>
      a.getAttribute('href'),
    );

    expect(hrefs).toEqual(['/employee-detail/NV004', '/employee-detail/NV005']);
    expect(hrefs.filter((href) => guid.test(href ?? ''))).toEqual([]);
  });

  // ---- own record: change phone / password ------------------------------------------------------

  it('enables the change buttons on /employee-detail/me', async () => {
    params.next(convertToParamMap({ code: 'me' }));
    const fixture = await render();

    const labels = Array.from(actions(fixture.nativeElement)!.querySelectorAll('button')).map((b) =>
      b.textContent?.trim(),
    );
    expect(labels).toEqual(['Đổi số điện thoại', 'Đổi mật khẩu']);
  });

  it('recognises the own record when opened by code (same email as the signed-in user)', async () => {
    auth.currentUser.set({ ...auth.currentUser()!, email: 'CHAU@example.test' });
    params.next(convertToParamMap({ code: 'NV004' }));
    const fixture = await render();

    expect(actions(fixture.nativeElement)).not.toBeNull();
  });

  it("shows no change buttons on someone else's record", async () => {
    params.next(convertToParamMap({ code: 'NV004' }));
    const fixture = await render();

    expect(actions(fixture.nativeElement)).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();
  });

  it('shows no change buttons in the picker or while signed out', async () => {
    auth.currentUser.set(null);
    params.next(convertToParamMap({ code: 'NV004' }));
    const fixture = await render();

    expect(actions(fixture.nativeElement)).toBeNull();
  });

  it('opens the phone popup prefilled with the stored number', async () => {
    params.next(convertToParamMap({ code: 'me' }));
    employees.me.and.returnValue(of(withManager));
    const fixture = await render();
    const root: HTMLElement = fixture.nativeElement;

    root.querySelector<HTMLButtonElement>('.hero-actions button')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('[role="dialog"]')!.getAttribute('aria-labelledby')).toBe(
      'phone-title',
    );
    expect(root.querySelector<HTMLInputElement>('#profile-phone')!.value).toBe('0901000004');
  });

  it('closes the popup on Cancel without reloading', async () => {
    params.next(convertToParamMap({ code: 'me' }));
    const fixture = await render();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('.hero-actions button')!.click();
    fixture.detectChanges();

    Array.from(root.querySelectorAll<HTMLButtonElement>('.modal-footer button'))
      .find((b) => b.textContent?.includes('Hủy'))!
      .click();
    fixture.detectChanges();

    expect(root.querySelector('[role="dialog"]')).toBeNull();
    expect(employees.me).toHaveBeenCalledTimes(1);
  });

  it('reloads the record and confirms after the phone number was changed', async () => {
    params.next(convertToParamMap({ code: 'me' }));
    employees.me.and.returnValue(of(withManager));
    profiles.updatePhone.and.returnValue(of({ phone: '0987654321' }));
    const fixture = await render();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('.hero-actions button')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const input = root.querySelector<HTMLInputElement>('#profile-phone')!;
    input.value = '0987654321';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('.modal-footer button[type="submit"]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(profiles.updatePhone).toHaveBeenCalledOnceWith('0987654321');
    expect(root.querySelector('[role="dialog"]')).toBeNull();
    expect(employees.me).toHaveBeenCalledTimes(2);
    expect(root.querySelector('.page-notice')!.textContent).toContain('Đã cập nhật số điện thoại');
  });

  it('confirms after the password was changed and clears the confirmation when another popup opens', async () => {
    params.next(convertToParamMap({ code: 'me' }));
    auth.changePassword.and.returnValue(of({} as never));
    const fixture = await render();
    const root: HTMLElement = fixture.nativeElement;
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    const type = async (selector: string, value: string) => {
      const input = root.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await settle();
    };

    Array.from(root.querySelectorAll<HTMLButtonElement>('.hero-actions button'))
      .find((b) => b.textContent?.includes('Đổi mật khẩu'))!
      .click();
    await settle();
    await type('#current-password', 'Current-Password-1');
    await type('#new-password', 'Brand-New-Password-2');
    await type('#confirm-password', 'Brand-New-Password-2');
    root.querySelector<HTMLButtonElement>('.modal-footer button[type="submit"]')!.click();
    await settle();

    expect(root.querySelector('[role="dialog"]')).toBeNull();
    expect(root.querySelector('.page-notice')!.textContent).toContain('Đã đổi mật khẩu');
    expect(employees.me).toHaveBeenCalledTimes(1);

    root.querySelector<HTMLButtonElement>('.hero-actions button')!.click();
    await settle();
    expect(root.querySelector('.page-notice')).toBeNull();
  });

  it('forgets the popup and confirmation when the user moves to another record', async () => {
    params.next(convertToParamMap({ code: 'me' }));
    const fixture = await render();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('.hero-actions button')!.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="dialog"]')).not.toBeNull();

    params.next(convertToParamMap({ code: 'NV004' }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('[role="dialog"]')).toBeNull();
    expect(actions(root)).toBeNull();
  });
});
