import { Component, signal } from '@angular/core';

type KpiMapping = {
  code: string;
  name: string;
  description: string;
  unit: string;
  aggregation: string;
  direction: string;
  improving: boolean;
  required: boolean;
  period: string;
  status: string;
  expired: boolean;
};

type RelatedItem = { code: string; name: string; status: string };

@Component({
  selector: 'app-work-type-detail',
  templateUrl: './work-type-detail.component.html',
})
export class WorkTypeDetailComponent {
  protected readonly activeTab = signal('overview');
  protected readonly showMoreOverview = signal(false);
  protected readonly showHistory = signal(true);
  protected readonly isEditing = signal(false);
  protected readonly positionSearch = signal('');
  protected readonly campaignSearch = signal('');

  protected readonly mappings: KpiMapping[] = [
    {
      code: 'KPI-001',
      name: 'Revenue',
      description: 'Doanh số huy động mới phát sinh',
      unit: 'VND',
      aggregation: 'SUM',
      direction: 'Tăng trưởng tốt',
      improving: true,
      required: true,
      period: '01/01/2026 – –',
      status: 'Current (Hiệu lực)',
      expired: false,
    },
    {
      code: 'KPI-008',
      name: 'CSAT Index',
      description: 'Điểm hài lòng khách hàng sau giải quyết khiếu nại',
      unit: 'Điểm (Thang 10)',
      aggregation: 'AVG',
      direction: 'Tăng trưởng tốt',
      improving: true,
      required: true,
      period: '01/01/2026 – 31/12/2026',
      status: 'Current (Hiệu lực)',
      expired: false,
    },
    {
      code: 'KPI-002',
      name: 'Error Rate',
      description: 'Tỷ lệ xử lý lỗi nghiệp vụ quy trình tiếp nhận',
      unit: '%',
      aggregation: 'AVG',
      direction: 'Giảm thiểu tốt',
      improving: false,
      required: false,
      period: '01/01/2026 – 30/06/2026',
      status: 'Expired (Hết hạn)',
      expired: true,
    },
  ];

  protected readonly jobPositions: RelatedItem[] = [
    { code: 'JP-001', name: 'Teller (Giao dịch viên)', status: 'Active' },
    { code: 'JP-002', name: 'Senior Teller (Kiểm soát viên giao dịch)', status: 'Active' },
    { code: 'JP-005', name: 'Customer Service Specialist (Chuyên viên CSKH)', status: 'Active' },
    {
      code: 'JP-011',
      name: 'Branch Operations Officer (Cán bộ vận hành chi nhánh)',
      status: 'Active',
    },
  ];

  protected readonly campaigns: RelatedItem[] = [
    { code: 'CMP-001', name: 'Q4 Sales & Service Quality Campaign', status: 'Active' },
    { code: 'CMP-004', name: 'Digital Banking Onboarding 2026', status: 'Active' },
    { code: 'CMP-007', name: 'Priority Customer Care Month', status: 'Active' },
  ];

  protected readonly navigationGroups = [
    {
      title: 'BÀN LÀM VIỆC CÁ NHÂN',
      items: [
        ['▦', 'Tổng quan & Dashboard', ''],
        ['◉', 'Công việc của tôi', '12'],
        ['⌁', 'Chỉ số KPI cá nhân', ''],
        ['♧', 'Đánh giá Hiệu suất', ''],
        ['⟳', 'Yêu cầu Bàn giao', '1'],
        ['⚒', 'Khiếu nại & Phúc khảo', ''],
      ],
    },
    {
      title: 'TỔ CHỨC & PHÒNG BAN',
      items: [
        ['▦', 'Phòng ban / Đơn vị', ''],
        ['♙', 'Vị trí / Chức danh', ''],
        ['▣', 'Hồ sơ Cán bộ', ''],
      ],
    },
    {
      title: 'THÔNG BÁO & LỊCH TRÌNH',
      items: [
        ['⚑', 'Thông báo tác nghiệp', '3'],
        ['▣', 'Lịch làm việc & SLA', ''],
      ],
    },
  ] as const;

  protected get visibleMappings(): KpiMapping[] {
    return this.showHistory() ? this.mappings : this.mappings.filter((item) => !item.expired);
  }

  protected get visibleJobPositions(): RelatedItem[] {
    const query = this.positionSearch().trim().toLocaleLowerCase();
    return this.jobPositions.filter((item) =>
      `${item.code} ${item.name}`.toLocaleLowerCase().includes(query),
    );
  }

  protected get visibleCampaigns(): RelatedItem[] {
    const query = this.campaignSearch().trim().toLocaleLowerCase();
    return this.campaigns.filter((item) =>
      `${item.code} ${item.name}`.toLocaleLowerCase().includes(query),
    );
  }

  protected selectTab(tab: string): void {
    this.activeTab.set(tab);
  }

  protected setPositionSearch(event: Event): void {
    this.positionSearch.set((event.target as HTMLInputElement).value);
  }

  protected setCampaignSearch(event: Event): void {
    this.campaignSearch.set((event.target as HTMLInputElement).value);
  }

  protected toggleEditMode(): void {
    this.isEditing.update((editing) => !editing);
  }

  protected toggleOverview(): void {
    this.showMoreOverview.update((expanded) => !expanded);
  }

  protected toggleHistory(event: Event): void {
    this.showHistory.set((event.target as HTMLInputElement).checked);
  }
}
