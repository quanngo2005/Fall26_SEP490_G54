import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { catchError, finalize, of } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { WorkTypeDetail, WorkTypeKpiDetail } from '../../core/work-type.models';
import { WorkTypeService } from '../../core/work-type.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';

const SAMPLE_WORK_TYPE: WorkTypeDetail = {
  id: '00000000-0000-0000-0000-000000000001',
  code: 'WT-2026-008',
  name: 'Thẩm định hồ sơ tín dụng khách hàng doanh nghiệp SME',
  description:
    'Quy trình thẩm định tín dụng toàn diện dành cho phân khúc khách hàng doanh nghiệp vừa và nhỏ, bao gồm phân tích báo cáo tài chính, thẩm định tài sản bảo đảm và đánh giá rủi ro ngành.',
  workflowDefinitionId: '00000000-0000-0000-0000-000000000010',
  workflowName: 'WF-LND-02 v1.3 (Quy trình cấp tín dụng SME chuẩn)',
  status: 'Active',
  createdAt: '2026-01-01T00:00:00Z',
  kpis: [
    {
      kpiDefinitionId: '00000000-0000-0000-0000-000000000101',
      kpiCode: 'KPI-001',
      kpiName: 'Doanh số huy động mới phát sinh',
      isRequired: true,
      effectiveFrom: '2026-01-01',
      effectiveTo: null,
    },
    {
      kpiDefinitionId: '00000000-0000-0000-0000-000000000102',
      kpiCode: 'KPI-008',
      kpiName: 'Điểm hài lòng khách hàng sau giải quyết khiếu nại (CSAT)',
      isRequired: true,
      effectiveFrom: '2026-01-01',
      effectiveTo: '2026-12-31',
    },
    {
      kpiDefinitionId: '00000000-0000-0000-0000-000000000103',
      kpiCode: 'KPI-002',
      kpiName: 'Tỷ lệ xử lý lỗi nghiệp vụ quy trình tiếp nhận',
      isRequired: false,
      effectiveFrom: '2026-01-01',
      effectiveTo: '2026-06-30',
    },
  ],
};

type RelatedItem = { code: string; name: string; status: string };

@Component({
  selector: 'app-work-type-detail',
  standalone: true,
  imports: [CommonModule, HeaderComponent, SidebarComponent],
  templateUrl: './work-type-detail.component.html',
  styleUrls: ['./work-type-detail.component.scss'],
})
export class WorkTypeDetailComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(WorkTypeService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly workType = signal<WorkTypeDetail | null>(SAMPLE_WORK_TYPE);
  readonly loading = signal<boolean>(false);
  readonly failed = signal<boolean>(false);

  readonly activeTab = signal('overview');
  readonly showMoreOverview = signal(false);
  readonly showHistory = signal(true);
  readonly isEditing = signal(false);
  readonly positionSearch = signal('');
  readonly campaignSearch = signal('');

  readonly jobPositions: RelatedItem[] = [
    { code: 'JP-001', name: 'Teller (Giao dịch viên)', status: 'Active' },
    { code: 'JP-002', name: 'Senior Teller (Kiểm soát viên giao dịch)', status: 'Active' },
    { code: 'JP-005', name: 'Customer Service Specialist (Chuyên viên CSKH)', status: 'Active' },
    {
      code: 'JP-011',
      name: 'Branch Operations Officer (Cán bộ vận hành chi nhánh)',
      status: 'Active',
    },
  ];

  readonly campaigns: RelatedItem[] = [
    { code: 'CMP-001', name: 'Q4 Sales & Service Quality Campaign', status: 'Active' },
    { code: 'CMP-004', name: 'Digital Banking Onboarding 2026', status: 'Active' },
    { code: 'CMP-007', name: 'Priority Customer Care Month', status: 'Active' },
  ];

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = params.get('id');
      this.loadWorkType(id || undefined);
    });
  }

  loadWorkType(id?: string): void {
    this.loading.set(true);
    this.failed.set(false);

    if (id) {
      this.api
        .detail(id)
        .pipe(
          catchError(() => of(SAMPLE_WORK_TYPE)),
          finalize(() => this.loading.set(false)),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe({
          next: (wt) => this.workType.set(wt),
          error: () => this.failed.set(true),
        });
    } else {
      this.api
        .list({ pageSize: 1 })
        .pipe(
          catchError(() => of({ items: [SAMPLE_WORK_TYPE], totalCount: 1, page: 1, pageSize: 1 })),
          finalize(() => this.loading.set(false)),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe({
          next: (res) => {
            if (res.items && res.items.length > 0) {
              this.workType.set(res.items[0]);
            } else {
              this.workType.set(SAMPLE_WORK_TYPE);
            }
          },
          error: () => this.failed.set(true),
        });
    }
  }

  get visibleKpis(): WorkTypeKpiDetail[] {
    const kpis = this.workType()?.kpis ?? [];
    if (this.showHistory()) return kpis;
    const now = new Date().toISOString().slice(0, 10);
    return kpis.filter((k) => !k.effectiveTo || k.effectiveTo >= now);
  }

  get visibleJobPositions(): RelatedItem[] {
    const query = this.positionSearch().trim().toLocaleLowerCase();
    return this.jobPositions.filter((item) =>
      `${item.code} ${item.name}`.toLocaleLowerCase().includes(query),
    );
  }

  get visibleCampaigns(): RelatedItem[] {
    const query = this.campaignSearch().trim().toLocaleLowerCase();
    return this.campaigns.filter((item) =>
      `${item.code} ${item.name}`.toLocaleLowerCase().includes(query),
    );
  }

  selectTab(tab: string): void {
    this.activeTab.set(tab);
  }

  setPositionSearch(event: Event): void {
    this.positionSearch.set((event.target as HTMLInputElement).value);
  }

  setCampaignSearch(event: Event): void {
    this.campaignSearch.set((event.target as HTMLInputElement).value);
  }

  toggleEditMode(): void {
    this.isEditing.update((editing) => !editing);
  }

  toggleOverview(): void {
    this.showMoreOverview.update((expanded) => !expanded);
  }

  toggleHistory(event: Event): void {
    this.showHistory.set((event.target as HTMLInputElement).checked);
  }
}
