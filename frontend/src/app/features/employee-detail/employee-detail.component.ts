import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { catchError, finalize, of } from 'rxjs';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { I18N_PIPES } from '../../shared/pipes';

export interface AssignedWorkItem {
  id: string;
  title: string;
  metadata: string;
  priority: string;
  priorityClass: string;
  deadline: string;
  status: string;
  statusClass: string;
  progress: number;
}

export interface KpiProgressItem {
  code: string;
  title: string;
  weight: string;
  target: string;
  actual: string;
  progress: number;
}

const SAMPLE_EMPLOYEE: EmployeeDetail = {
  id: '00000000-0000-0000-0000-000000000001',
  employeeCode: 'NV-00182',
  fullName: 'Nguyễn Văn An',
  email: 'an.nv@bank.vn',
  phone: '(024) 3888 1234',
  departmentId: '00000000-0000-0000-0000-000000000010',
  departmentCode: 'RB-HN-01',
  departmentName: 'Phòng Khách hàng Cá nhân',
  jobPositionId: '00000000-0000-0000-0000-000000000020',
  jobPositionCode: 'POS-RM-SR',
  jobPositionTitle: 'Chuyên viên QHKH Doanh nghiệp Cấp cao',
  managerId: '00000000-0000-0000-0000-000000000030',
  managerCode: 'MGR-001',
  managerName: 'Trần Văn Quản Lý',
  status: 'ACTIVE',
  directReportsCount: 4,
  lastSyncAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
};

@Component({
  selector: 'app-employee-detail',
  standalone: true,
  imports: [CommonModule, HeaderComponent, SidebarComponent, ...I18N_PIPES],
  templateUrl: './employee-detail.component.html',
  styleUrls: ['./employee-detail.component.scss'],
})
export class EmployeeDetailComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(EmployeeService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly employee = signal<EmployeeDetail | null>(SAMPLE_EMPLOYEE);
  readonly loading = signal<boolean>(false);
  readonly failed = signal<boolean>(false);
  readonly showMoreActions = signal<boolean>(false);
  readonly activeTab = signal<'works' | 'kpis' | 'history'>('works');

  readonly works = signal<AssignedWorkItem[]>([
    {
      id: 'WRK-202610-0102',
      title: 'Thẩm định cấp tín dụng Cty CP Minh Phát',
      metadata: 'WT-LND-02 v1.3 • Hồ sơ Doanh nghiệp SME',
      priority: 'GẤP',
      priorityClass: 'badge-danger',
      deadline: 'Còn 04 giờ 20p',
      status: 'Cần sửa đổi (Revision)',
      statusClass: 'badge-warning',
      progress: 60,
    },
    {
      id: 'WRK-202610-0115',
      title: 'Tiếp thị gói thẻ tín dụng cho Ban TGĐ Techcorp',
      metadata: 'WT-CRD-01 • Chương trình Payroll VIP',
      priority: 'CAO',
      priorityClass: 'badge-warning',
      deadline: 'Còn 2 ngày',
      status: 'Đang thực hiện',
      statusClass: 'badge-info',
      progress: 45,
    },
    {
      id: 'WRK-202610-0089',
      title: 'Xử lý khiếu nại phí giao dịch CIF #8812',
      metadata: 'WT-CS-04 • Tiếp nhận qua Tổng đài',
      priority: 'TRUNG BÌNH',
      priorityClass: 'badge-secondary',
      deadline: 'Hôm nay',
      status: 'Chờ thẩm định',
      statusClass: 'badge-primary',
      progress: 90,
    },
  ]);

  readonly kpis = signal<KpiProgressItem[]>([
    {
      code: 'KPI-REV-01',
      title: 'Doanh số huy động mới phát sinh',
      weight: '35%',
      target: '15.000.000.000 VND',
      actual: '12.450.000.000 VND',
      progress: 83,
    },
    {
      code: 'KPI-CSAT-02',
      title: 'Chỉ số hài lòng khách hàng (CSAT)',
      weight: '25%',
      target: '90%',
      actual: '94%',
      progress: 104,
    },
    {
      code: 'KPI-ERR-03',
      title: 'Tỷ lệ hồ sơ lỗi nghiệp vụ tiếp nhận',
      weight: '20%',
      target: '< 2.0%',
      actual: '0.8%',
      progress: 100,
    },
  ]);

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = params.get('id');
      this.loadEmployee(id || undefined);
    });
  }

  loadEmployee(id?: string): void {
    this.loading.set(true);
    this.failed.set(false);

    if (id) {
      this.api
        .detail(id)
        .pipe(
          catchError(() => {
            // Graceful fallback to sample employee for dev preview
            return of(SAMPLE_EMPLOYEE);
          }),
          finalize(() => this.loading.set(false)),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe({
          next: (emp) => this.employee.set(emp),
          error: () => this.failed.set(true),
        });
    } else {
      this.api
        .list({ pageSize: 1 })
        .pipe(
          catchError(() => {
            return of({ items: [SAMPLE_EMPLOYEE], totalCount: 1, page: 1, pageSize: 1 });
          }),
          finalize(() => this.loading.set(false)),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe({
          next: (res) => {
            if (res.items && res.items.length > 0) {
              this.employee.set(res.items[0]);
            } else {
              this.employee.set(SAMPLE_EMPLOYEE);
            }
          },
          error: () => this.failed.set(true),
        });
    }
  }

  toggleMoreActions(): void {
    this.showMoreActions.update((v) => !v);
  }

  setTab(tab: 'works' | 'kpis' | 'history'): void {
    this.activeTab.set(tab);
  }
}

export default EmployeeDetailComponent;
