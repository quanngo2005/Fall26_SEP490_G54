import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { DepartmentService } from '../../core/department.service';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { I18nService } from '../../core/i18n/i18n.service';
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
const emptyPage = { items: [], totalCount: 0, page: 1, pageSize: 20 };

describe('EmployeeDetailComponent', () => {
  const employees = { detail: jasmine.createSpy('detail'), list: jasmine.createSpy('list') };
  const departments = { members: jasmine.createSpy('members') };
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(async () => {
    employees.detail.calls.reset();
    employees.list.calls.reset();
    departments.members.calls.reset();
    employees.list.and.returnValue(of(emptyPage));
    employees.detail.and.returnValue(of(employee));
    departments.members.and.returnValue(of(emptyPage));
    params = new BehaviorSubject(convertToParamMap({}));
    query = new BehaviorSubject(convertToParamMap({}));
    await TestBed.configureTestingModule({
      imports: [EmployeeDetailComponent],
      providers: [
        provideRouter([]),
        { provide: EmployeeService, useValue: employees },
        { provide: DepartmentService, useValue: departments },
        { provide: ActivatedRoute, useValue: { paramMap: params, queryParamMap: query } },
      ],
    })
      .overrideComponent(EmployeeDetailComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('shows a real empty picker without sample employees and switches language live', () => {
    const fixture = TestBed.createComponent(EmployeeDetailComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(employees.detail).not.toHaveBeenCalled();
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

  it('loads the employee from the query id and renders N/A for missing data', () => {
    query.next(convertToParamMap({ id: employee.id }));
    const fixture = TestBed.createComponent(EmployeeDetailComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(employees.detail).toHaveBeenCalledWith(employee.id);
    expect(departments.members).toHaveBeenCalledWith(employee.departmentId, {
      keyword: 'NV004',
      pageSize: 10,
    });
    expect(root.textContent).toContain('Lê Minh Châu');
    expect(root.textContent).toContain('N/A');
    expect(root.textContent).not.toContain('(024)');
  });

  it('uses the path id and shows roster figures when present', () => {
    params.next(convertToParamMap({ id: employee.id }));
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
    const fixture = TestBed.createComponent(EmployeeDetailComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('88,5');
    expect(fixture.nativeElement.textContent).toContain('7');
  });

  it('still renders the profile when the department roster is forbidden', () => {
    query.next(convertToParamMap({ id: employee.id }));
    departments.members.and.returnValue(throwError(() => new Error('403')));
    const fixture = TestBed.createComponent(EmployeeDetailComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Lê Minh Châu');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('shows an error with retry and never substitutes sample data', () => {
    query.next(convertToParamMap({ id: employee.id }));
    employees.detail.and.returnValue(throwError(() => new Error('500')));
    const fixture = TestBed.createComponent(EmployeeDetailComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Không thể tải');
    expect(root.textContent).not.toContain('Nguyễn Văn An');
    employees.detail.and.returnValue(of(employee));
    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(root.textContent).toContain('Lê Minh Châu');
  });
});
