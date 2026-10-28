import { Component, input, output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { EmployeeService } from '../../core/employee.service';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeListItem } from '../../core/employee-management.models';
import { EmployeeListComponent } from './employee-list.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { EmployeeDetailDrawerComponent } from './employee-detail-drawer.component';

@Component({ selector: 'app-header', standalone: true, template: '' })
class HeaderStub {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class SidebarStub {}

@Component({ selector: 'app-layout-icon', standalone: true, template: '' })
class IconStub {
  readonly name = input<string>();
}

@Component({
  selector: 'app-employee-detail-drawer',
  standalone: true,
  template: '<div class="employee-detail-drawer">{{employee().code}}</div>',
})
class DrawerStub {
  readonly employee = input.required<EmployeeListItem>();
  readonly closeDrawer = output<void>();
}

const mockEmployee1: EmployeeDetail = {
  id: 'emp-0001',
  employeeCode: 'NV0001',
  fullName: 'Nguyễn Văn A',
  username: 'nva',
  email: 'nva@bank.vn',
  phone: '0901234567',
  departmentId: 'd1',
  departmentCode: 'KD',
  departmentName: 'Phòng Kinh doanh',
  jobPositionId: 'p1',
  jobPositionCode: 'GDV',
  jobPositionTitle: 'Giao dịch viên',
  managerId: null,
  managerCode: null,
  managerName: null,
  status: 'ACTIVE',
  directReportsCount: 0,
  lastSyncAt: '2026-10-24T08:15:00+07:00',
  updatedAt: '2026-10-24T08:15:00+07:00',
};

const mockEmployee2: EmployeeDetail = {
  id: 'emp-0002',
  employeeCode: 'NV0002',
  fullName: 'Trần Thị B',
  username: null,
  email: 'ttb@bank.vn',
  phone: null,
  departmentId: 'd1',
  departmentCode: 'KD',
  departmentName: 'Phòng Kinh doanh',
  jobPositionId: 'p2',
  jobPositionCode: 'CV',
  jobPositionTitle: 'Chuyên viên',
  managerId: 'emp-0001',
  managerCode: 'NV0001',
  managerName: 'Nguyễn Văn A',
  status: 'ON_LEAVE',
  directReportsCount: 0,
  lastSyncAt: '2026-10-24T08:15:00+07:00',
  updatedAt: '2026-10-24T08:15:00+07:00',
};

describe('EmployeeListComponent', () => {
  let fixture: ComponentFixture<EmployeeListComponent>;
  let component: EmployeeListComponent;
  let api: jasmine.SpyObj<EmployeeService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<EmployeeService>('EmployeeService', [
      'list',
      'departments',
      'jobPositions',
    ]);

    api.departments.and.returnValue(
      of([{ id: 'd1', code: 'KD', name: 'Phòng Kinh doanh', levelType: 1 }]),
    );
    api.jobPositions.and.returnValue(of([{ id: 'p1', code: 'GDV', title: 'Giao dịch viên' }]));
    api.list.and.returnValue(
      of({ items: [mockEmployee1, mockEmployee2], totalCount: 2, page: 1, pageSize: 20 }),
    );

    await TestBed.configureTestingModule({
      imports: [EmployeeListComponent],
      providers: [provideRouter([]), { provide: EmployeeService, useValue: api }, I18nService],
    })
      .overrideComponent(EmployeeListComponent, {
        remove: {
          imports: [
            HeaderComponent,
            SidebarComponent,
            LayoutIconComponent,
            EmployeeDetailDrawerComponent,
          ],
        },
        add: {
          imports: [HeaderStub, SidebarStub, IconStub, DrawerStub],
        },
      })
      .compileComponents();

    fixture = TestBed.createComponent(EmployeeListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders page heading with title and read-only badge', () => {
    const h1: HTMLElement = fixture.nativeElement.querySelector('h1');
    const badge: HTMLElement = fixture.nativeElement.querySelector('.read-only-badge');
    expect(h1).toBeTruthy();
    expect(badge).toBeTruthy();
    expect(badge.textContent?.trim()).toBe('Chỉ xem');
  });

  it('renders sync banner showing N/A and unavailable note', () => {
    const banner: HTMLElement = fixture.nativeElement.querySelector('.sync-banner');
    expect(banner).toBeTruthy();
    expect(banner.textContent).toContain('N/A');
  });

  it('populates department and position filter dropdowns', () => {
    const dropdowns: NodeListOf<HTMLSelectElement> =
      fixture.nativeElement.querySelectorAll('.dropdown-select');
    expect(dropdowns.length).toBe(2);
    // Department dropdown has placeholder + 1 item
    expect(dropdowns[0].options.length).toBe(2);
    expect(dropdowns[0].options[1].value).toBe('d1');
  });

  it('renders table rows with employees from API', () => {
    const rows = fixture.nativeElement.querySelectorAll('.employee-row');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('NV0001');
    expect(rows[0].textContent).toContain('Nguyễn Văn A');
    expect(rows[0].textContent).toContain('nva');

    expect(rows[1].textContent).toContain('NV0002');
    expect(rows[1].textContent).toContain('Chưa có'); // fallback for null username
  });

  it('selects an employee row and opens drawer', () => {
    const rows: NodeListOf<HTMLElement> = fixture.nativeElement.querySelectorAll('.employee-row');
    rows[0].click();
    fixture.detectChanges();

    const drawer = fixture.nativeElement.querySelector('.employee-detail-drawer');
    expect(drawer).toBeTruthy();
    expect(drawer.textContent).toContain('NV0001');
  });

  it('closes drawer when closeDrawer is called', () => {
    component['selectEmployee']({
      id: mockEmployee1.id,
      code: mockEmployee1.employeeCode,
      fullName: mockEmployee1.fullName,
      departmentCode: mockEmployee1.departmentCode,
      departmentName: mockEmployee1.departmentName,
      positionCode: mockEmployee1.jobPositionCode,
      positionTitle: mockEmployee1.jobPositionTitle,
      status: mockEmployee1.status,
      account: 'nva',
      managerName: 'N/A',
      joinDate: 'N/A',
      email: mockEmployee1.email,
      phone: 'N/A',
      lastSyncAt: mockEmployee1.lastSyncAt,
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.employee-detail-drawer')).toBeTruthy();

    component['closeDrawer']();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.employee-detail-drawer')).toBeNull();
  });

  it('triggers search when keyword changes', () => {
    component['onSearchChange']('NV0002');
    expect(api.list).toHaveBeenCalledWith(
      jasmine.objectContaining({
        keyword: 'NV0002',
        page: 1,
      }),
    );
  });

  it('filters by department', () => {
    component['changeDepartment']('d1');
    expect(api.list).toHaveBeenCalledWith(
      jasmine.objectContaining({
        departmentId: 'd1',
        page: 1,
      }),
    );
  });

  it('filters by job position', () => {
    component['changePosition']('p1');
    expect(api.list).toHaveBeenCalledWith(
      jasmine.objectContaining({
        jobPositionId: 'p1',
        page: 1,
      }),
    );
  });

  it('navigates through employees with ArrowDown and ArrowUp keys', () => {
    const rows: NodeListOf<HTMLElement> = fixture.nativeElement.querySelectorAll('.employee-row');
    rows[0].click();
    fixture.detectChanges();

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    fixture.detectChanges();
    let drawer = fixture.nativeElement.querySelector('.employee-detail-drawer');
    expect(drawer.textContent).toContain('NV0002');

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
    fixture.detectChanges();
    drawer = fixture.nativeElement.querySelector('.employee-detail-drawer');
    expect(drawer.textContent).toContain('NV0001');
  });

  it('handles offline network error state', () => {
    api.list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    component['applyFilters']();
    fixture.detectChanges();

    const emptyCell = fixture.nativeElement.querySelector('.empty-cell');
    expect(emptyCell).toBeTruthy();
    expect(emptyCell.textContent).toContain('Không kết nối được máy chủ');
  });

  it('handles 403 forbidden state', () => {
    api.list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
    component['applyFilters']();
    fixture.detectChanges();

    const emptyCell = fixture.nativeElement.querySelector('.empty-cell');
    expect(emptyCell).toBeTruthy();
    expect(emptyCell.textContent).toContain('Bạn không có quyền');
  });
});
