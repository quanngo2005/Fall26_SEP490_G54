import { Component, HostListener, input, output } from '@angular/core';

export type SubmissionAttachment = {
  name: string;
  description: string;
  size: string;
  type: 'XLSX' | 'PDF';
};

export type SubmissionAuditEntry = {
  title: string;
  reviewer: string;
  timestamp: string;
  message: string;
};

export type SubmissionAttachmentRequest = {
  attachment: SubmissionAttachment;
  action: 'view' | 'download';
};

export type SubmissionDetail = {
  submissionId: string;
  taskId: string;
  taskName: string;
  employee: string;
  role: string;
  customerGroup: string;
  submittedAt: string;
  attempt: number;
  reviewerMode: string;
  status: string;
  conclusion: string;
  statusKind: 'revision' | 'approved' | 'pending' | 'rejected';
  kpiCode: string;
  kpiName: string;
  quantity: string;
  scale: string;
  unit: string;
  explanation: string;
  attachments: SubmissionAttachment[];
  auditEntries: SubmissionAuditEntry[];
};

@Component({
  selector: 'app-submission-detail-modal',
  templateUrl: './submission-detail-modal.component.html',
})
export class SubmissionDetailModalComponent {
  readonly open = input(false);
  readonly submission = input<SubmissionDetail>(defaultSubmission);

  readonly closed = output<void>();
  readonly attachmentRequested = output<SubmissionAttachmentRequest>();
  readonly resubmitRequested = output<SubmissionDetail>();
  readonly archiveDownloadRequested = output<SubmissionDetail>();

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open()) this.closed.emit();
  }

  closeOnBackdrop(event: MouseEvent): void {
    if (event.target === event.currentTarget) this.closed.emit();
  }

  requestResubmission(): void {
    this.resubmitRequested.emit(this.submission());
  }

  requestArchiveDownload(): void {
    this.archiveDownloadRequested.emit(this.submission());
  }
}

export const defaultSubmission: SubmissionDetail = {
  submissionId: '#SUB-WRK-2026-089-V1',
  taskId: 'WRK-2026-089',
  taskName: 'Mở thẻ tín dụng KH cá nhân',
  employee: 'Nguyễn Tiến Thành (Teller)',
  role: 'P. Khách hàng cá nhân',
  customerGroup: 'Head Office Tower',
  submittedAt: '05/10/2026 16:36:15',
  attempt: 1,
  reviewerMode: 'Read-only Snapshot',
  status: 'Đã thẩm định bởi Trưởng phòng',
  conclusion: 'Yêu cầu sửa đổi bổ sung hồ sơ',
  statusKind: 'revision',
  kpiCode: 'KPI-CARD-01',
  kpiName: 'Số lượng thẻ tín dụng phát hành thành công',
  quantity: '15',
  scale: 'N/A',
  unit: 'Thẻ',
  explanation:
    'Đã hoàn tất tiếp cận khách hàng, kiểm tra điều kiện CIC và phát hành thành công 15 thẻ tín dụng JCB/Visa. Các hồ sơ đã được scan và đối chiếu đầy đủ lên hệ thống Core Banking tuần đầu tháng 10.',
  attachments: [
    {
      name: 'Bao_cao_danh_sach_15_the_da_phat_hanh.xlsx',
      description: 'Export CoreBanking đối chiếu CIF khách hàng',
      size: '1.4 MB',
      type: 'XLSX',
    },
    {
      name: 'Ho_so_scan_chu_ky_khach_hang_dot1.pdf',
      description: 'Hợp đồng tín dụng có chữ ký khách hàng',
      size: '5.2 MB',
      type: 'PDF',
    },
  ],
  auditEntries: [
    {
      title: 'Vòng thẩm định 1: Yêu cầu sửa đổi (Revision Required)',
      reviewer: 'Trần Văn Quản Lý (Trưởng phòng KHCN)',
      timestamp: '05/10/2026 17:15:20',
      message:
        'Chiếu ngày 05/10 scan thiếu chữ ký trang 3 của 2 hồ sơ KH (số CIF #8812 và #8819). Yêu cầu nhân viên scan bổ sung đầy đủ chữ ký để thẩm định lại!',
    },
  ],
};
