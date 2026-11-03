import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EvaluationService } from '../../../core/evaluation.service';
import {
  BranchApprovalOverview,
  CalibrationEmployeeRow,
  CalibrationOverview,
  DepartmentApprovalSummary,
} from '../../../core/evaluation.models';
import { I18nService } from '../../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../../shared/layout';
import { LayoutIconComponent } from '../../../shared/layout/layout-icon.component';
import { PreCloseCheckModalComponent } from '../pre-close-check-modal/pre-close-check-modal.component';

export interface PositionTabItem {
  id: string | null;
  title: string;
  count: number;
  hiddenCount: number;
}

@Component({
  selector: 'app-department-ranking-approval',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    PreCloseCheckModalComponent,
  ],
  templateUrl: './department-ranking-approval.component.html',
  styleUrl: './department-ranking-approval.component.scss',
})
export class DepartmentRankingApprovalComponent implements OnInit {
  private readonly evaluationService = inject(EvaluationService);
  private readonly i18n = inject(I18nService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  periodId = signal<string>('');
  selectedDepartmentId = signal<string | null>(null);
  selectedPositionId = signal<string | null>(null);

  branchOverview = signal<BranchApprovalOverview | null>(null);
  calibrationData = signal<CalibrationOverview | null>(null);

  loadingOverview = signal<boolean>(false);
  loadingCalibration = signal<boolean>(false);
  errorMessage = signal<string | null>(null);
  toastMessage = signal<string | null>(null);
  toastType = signal<'success' | 'error'>('success');
  private toastTimer?: ReturnType<typeof setTimeout>;

  // Reject modal
  isRejectModalOpen = signal<boolean>(false);
  rejectReason = signal<string>('');
  rejectError = signal<string | null>(null);
  rejectSubmitting = signal<boolean>(false);

  // Approve modal
  isApproveModalOpen = signal<boolean>(false);
  approveNote = signal<string>('');
  approveSubmitting = signal<boolean>(false);

  // Pre-close check modal
  isPreCloseModalOpen = signal<boolean>(false);

  // Finalize / Publish modal
  isLockModalOpen = signal<boolean>(false);
  lockSignatureToken = signal<string>('');
  lockApprovalComments = signal<string>('');
  lockError = signal<string | null>(null);
  lockSubmitting = signal<boolean>(false);

  protected readonly t = computed(() => {
    return this.i18n.localization.evaluations.departmentApproval;
  });

  headerTitle = computed<string>(() => {
    return (
      this.branchOverview()?.period?.name || this.t()['pageTitle'] || 'Phê duyệt xếp hạng chi nhánh'
    );
  });

  protected readonly tLock = computed(() => {
    return this.i18n.localization.evaluations.branchPeriods;
  });

  selectedDepartment = computed<DepartmentApprovalSummary | null>(() => {
    const deptId = this.selectedDepartmentId();
    const depts = this.branchOverview()?.departments ?? [];
    return depts.find((d) => d.departmentId === deptId) ?? null;
  });

  positionTabs = computed<PositionTabItem[]>(() => {
    const roster = this.calibrationData()?.rankedRoster ?? [];
    const hidden = this.calibrationData()?.disputedPendingCount ?? 0;

    const map = new Map<string, { title: string; count: number }>();
    for (const emp of roster) {
      if (emp.jobPositionId) {
        const cur = map.get(emp.jobPositionId);
        if (cur) {
          cur.count++;
        } else {
          map.set(emp.jobPositionId, {
            title: emp.jobPositionTitle || 'Khác',
            count: 1,
          });
        }
      }
    }

    const tabs: PositionTabItem[] = [];
    for (const [id, val] of map.entries()) {
      tabs.push({
        id,
        title: val.title,
        count: val.count,
        hiddenCount: hidden,
      });
    }

    return tabs;
  });

  filteredRoster = computed<CalibrationEmployeeRow[]>(() => {
    const roster = this.calibrationData()?.rankedRoster ?? [];
    const posId = this.selectedPositionId();
    if (!posId) return roster;
    return roster.filter((r) => r.jobPositionId === posId);
  });

  canApproveCurrentDept = computed<boolean>(() => {
    const dept = this.selectedDepartment();
    return dept != null && dept.status === 'IN_REVIEW';
  });

  canRejectCurrentDept = computed<boolean>(() => {
    const dept = this.selectedDepartment();
    return dept != null && dept.status === 'IN_REVIEW';
  });

  canPublish = computed<boolean>(() => {
    const ov = this.branchOverview();
    return ov != null && ov.canFinalize;
  });

  publishHintText = computed<string>(() => {
    const depts = this.branchOverview()?.departments ?? [];
    const approved = depts.filter((d) => d.status === 'APPROVED' || d.status === 'FINAL').length;
    const total = depts.length;
    return (this.t()['publishHint'] || 'Mở khi {approved}/{total} phòng đã duyệt và kiểm tra đạt.')
      .replace('{approved}', String(approved))
      .replace('{total}', String(total));
  });

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const pid = params.get('periodId');
      if (pid) {
        this.periodId.set(pid);
        this.loadBranchOverview();
      }
    });

    this.destroyRef.onDestroy(() => {
      if (this.toastTimer) clearTimeout(this.toastTimer);
    });
  }

  loadBranchOverview(): void {
    const pid = this.periodId();
    if (!pid) return;

    this.loadingOverview.set(true);
    this.errorMessage.set(null);

    this.evaluationService.getBranchApprovals(pid).subscribe({
      next: (overview) => {
        this.branchOverview.set(overview);
        this.loadingOverview.set(false);

        // Auto select first department if none selected
        if (!this.selectedDepartmentId() && overview.departments.length > 0) {
          this.selectDepartment(overview.departments[0].departmentId);
        } else if (this.selectedDepartmentId()) {
          this.loadDepartmentCalibration(this.selectedDepartmentId()!);
        }
      },
      error: () => {
        this.errorMessage.set(this.t()['errorLoad'] || 'Không thể tải dữ liệu xếp hạng phòng ban.');
        this.loadingOverview.set(false);
      },
    });
  }

  selectDepartment(deptId: string): void {
    this.selectedDepartmentId.set(deptId);
    this.selectedPositionId.set(null);
    this.loadDepartmentCalibration(deptId);
  }

  selectPosition(posId: string | null): void {
    this.selectedPositionId.set(posId);
  }

  loadDepartmentCalibration(deptId: string): void {
    const pid = this.periodId();
    if (!pid || !deptId) return;

    this.loadingCalibration.set(true);
    this.evaluationService.getCalibration(pid, deptId).subscribe({
      next: (calib) => {
        this.calibrationData.set(calib);
        // Default to first position tab if available
        const tabs = this.positionTabs();
        if (tabs.length > 0 && !this.selectedPositionId()) {
          this.selectedPositionId.set(tabs[0].id);
        }
        this.loadingCalibration.set(false);
      },
      error: () => {
        this.calibrationData.set(null);
        this.loadingCalibration.set(false);
      },
    });
  }

  openApproveModal(): void {
    this.approveNote.set('');
    this.isApproveModalOpen.set(true);
  }

  closeApproveModal(): void {
    this.isApproveModalOpen.set(false);
    this.approveNote.set('');
  }

  submitApproveDepartment(): void {
    const deptId = this.selectedDepartmentId();
    const pid = this.periodId();
    if (!deptId || !pid) return;

    this.approveSubmitting.set(true);
    this.evaluationService
      .approveDepartment(deptId, { periodId: pid, note: this.approveNote().trim() || null })
      .subscribe({
        next: (res) => {
          this.approveSubmitting.set(false);
          this.closeApproveModal();
          this.showToast(this.t()['approveSuccess'] || res.message, 'success');
          this.loadBranchOverview();
        },
        error: (err: { error?: { message?: string } }) => {
          this.approveSubmitting.set(false);
          this.showToast(err?.error?.message || 'Lỗi phê duyệt phòng ban.', 'error');
        },
      });
  }

  openRejectModal(): void {
    this.rejectReason.set('');
    this.rejectError.set(null);
    this.isRejectModalOpen.set(true);
  }

  closeRejectModal(): void {
    this.isRejectModalOpen.set(false);
    this.rejectReason.set('');
    this.rejectError.set(null);
  }

  submitRejectDepartment(): void {
    const reason = this.rejectReason().trim();
    if (reason.length < 10) {
      this.rejectError.set(
        this.t()['rejectReasonMinLength'] || 'Lý do trả về phải có ít nhất 10 ký tự.',
      );
      return;
    }

    const deptId = this.selectedDepartmentId();
    const pid = this.periodId();
    if (!deptId || !pid) return;

    this.rejectSubmitting.set(true);
    this.rejectError.set(null);

    this.evaluationService.rejectDepartment(deptId, { periodId: pid, reason }).subscribe({
      next: (res) => {
        this.rejectSubmitting.set(false);
        this.closeRejectModal();
        this.showToast(this.t()['rejectSuccess'] || res.message, 'success');
        this.loadBranchOverview();
      },
      error: (err: { error?: { message?: string } }) => {
        this.rejectSubmitting.set(false);
        this.rejectError.set(err?.error?.message || 'Lỗi khi yêu cầu hiệu chuẩn lại.');
      },
    });
  }

  openPreCloseModal(): void {
    this.isPreCloseModalOpen.set(true);
  }

  closePreCloseModal(): void {
    this.isPreCloseModalOpen.set(false);
  }

  onPreCloseContinuePublish(): void {
    this.closePreCloseModal();
    this.openLockModal();
  }

  openLockModal(): void {
    this.lockSignatureToken.set('');
    this.lockApprovalComments.set('');
    this.lockError.set(null);
    this.isLockModalOpen.set(true);
  }

  closeLockModal(): void {
    this.isLockModalOpen.set(false);
    this.lockError.set(null);
  }

  submitLockPeriod(): void {
    const pid = this.periodId();
    if (!pid) return;

    this.lockSubmitting.set(true);
    this.lockError.set(null);

    this.evaluationService
      .finalize({
        periodId: pid,
        signatureToken: this.lockSignatureToken().trim() || null,
        approvalComments: this.lockApprovalComments().trim() || null,
      })
      .subscribe({
        next: (res) => {
          this.lockSubmitting.set(false);
          this.closeLockModal();
          this.showToast(
            this.tLock().lockSuccess || res.message || 'Khóa kỳ đánh giá thành công.',
            'success',
          );
          this.loadBranchOverview();
        },
        error: (err: { error?: { message?: string } }) => {
          this.lockSubmitting.set(false);
          this.lockError.set(err?.error?.message || 'Lỗi khi khóa kỳ đánh giá.');
        },
      });
  }

  navigateToTraceability(employeeId: string): void {
    this.router.navigate(['/performance-traceability'], {
      queryParams: { employeeId, periodId: this.periodId() },
    });
  }

  formatDate(dateStr?: string | null): string {
    if (!dateStr) return '';
    try {
      const d = new Date(dateStr);
      return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`;
    } catch {
      return '';
    }
  }

  formatScore(score?: number | null): string {
    if (score == null) return '-';
    return score.toLocaleString('vi-VN', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  }

  private showToast(message: string, type: 'success' | 'error'): void {
    this.toastMessage.set(message);
    this.toastType.set(type);
    if (this.toastTimer) clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toastMessage.set(null), 4000);
  }
}
