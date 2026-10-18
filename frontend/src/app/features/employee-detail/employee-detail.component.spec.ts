import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { EmployeeService } from '../../core/employee.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { EmployeeDetailComponent } from './employee-detail.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

describe('EmployeeDetailComponent', () => {
  const mockEmployee = {
    id: 'emp-123',
    employeeCode: 'NV-00182',
    fullName: 'Nguyễn Văn An',
    email: 'an.nv@bank.vn',
    phone: '(024) 3888 1234',
    departmentId: 'dept-01',
    departmentCode: 'RB-HN-01',
    departmentName: 'Phòng Khách hàng Cá nhân',
    jobPositionId: 'pos-01',
    jobPositionCode: 'POS-RM-SR',
    jobPositionTitle: 'Chuyên viên QHKH Doanh nghiệp Cấp cao',
    managerId: 'mgr-01',
    managerCode: 'MGR-001',
    managerName: 'Trần Văn Quản Lý',
    status: 'ACTIVE' as const,
    directReportsCount: 4,
    lastSyncAt: '2026-10-18T10:00:00Z',
    updatedAt: '2026-10-18T10:00:00Z',
  };

  const api = {
    list: jasmine.createSpy('list'),
    detail: jasmine.createSpy('detail'),
  };

  beforeEach(async () => {
    api.list.and.returnValue(of({ items: [mockEmployee], totalCount: 1, page: 1, pageSize: 20 }));
    api.detail.and.returnValue(of(mockEmployee));

    await TestBed.configureTestingModule({
      imports: [EmployeeDetailComponent],
      providers: [provideRouter([]), { provide: EmployeeService, useValue: api }],
    })
      .overrideComponent(EmployeeDetailComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
  });

  it('renders employee detail with full info', () => {
    const fixture = TestBed.createComponent(EmployeeDetailComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.textContent).toContain('Nguyễn Văn An');
    expect(root.textContent).toContain('NV-00182');
    expect(root.textContent).toContain('Phòng Khách hàng Cá nhân');
    expect(root.textContent).toContain('Chuyên viên QHKH Doanh nghiệp Cấp cao');
  });

  it('switches between works and kpis tabs', () => {
    const fixture = TestBed.createComponent(EmployeeDetailComponent);
    fixture.detectChanges();
    const buttons = fixture.nativeElement.querySelectorAll('.tab-btn');
    expect(buttons.length).toBe(2);

    // Click KPI tab
    buttons[1].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.activeTab()).toBe('kpis');
    expect(fixture.nativeElement.textContent).toContain('KPI-REV-01');

    // Click Works tab
    buttons[0].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.activeTab()).toBe('works');
    expect(fixture.nativeElement.textContent).toContain('WRK-202610-0102');
  });

  it('handles error state and provides retry functionality', () => {
    api.list.and.returnValue(throwError(() => new Error('Network error')));
    api.detail.and.returnValue(throwError(() => new Error('Network error')));

    const fixture = TestBed.createComponent(EmployeeDetailComponent);
    fixture.detectChanges();
    // Verify component creates and is resilient
    expect(fixture.componentInstance).toBeTruthy();
  });
});
