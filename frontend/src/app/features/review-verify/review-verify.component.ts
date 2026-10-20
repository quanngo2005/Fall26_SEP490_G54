import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  APPROVED_MOCK_DOSSIER,
  DEFAULT_MOCK_DOSSIER,
  MAKER_CHECKER_VIOLATION_DOSSIER,
} from './review-verify.data';
import {
  AuditDecisionPayload,
  AuditDecisionResponse,
  ControlStandard,
  DecisionType,
  DossierInspection,
  DossierViewState,
  EvidenceFile,
} from './review-verify.model';
import { ReviewVerifyService } from './review-verify.service';

@Component({
  selector: 'app-review-verify',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './review-verify.component.html',
})
export class ReviewVerifyComponent extends LocalizedComponent implements OnInit {
  private readonly reviewService = inject(ReviewVerifyService);

  // View state and data signals
  readonly viewState = signal<DossierViewState>('success');
  readonly dossier = signal<DossierInspection | null>(DEFAULT_MOCK_DOSSIER);
  readonly currentUserId = signal<string>('EMP-MGR-001'); // Manager Tran Van Quan Ly

  // Inspection viewer state signals
  readonly activeFileId = signal<string>('8b849202-b2d2-4309-8438-2f1bfb8ec012');
  readonly docZoom = signal<number>(100);
  readonly docCurrentPage = signal<number>(1);

  // Decision form signals
  readonly controlStandards = signal<ControlStandard[]>([...DEFAULT_MOCK_DOSSIER.controlStandards]);
  readonly verifiedValue = signal<number>(15);
  readonly selectedDecision = signal<DecisionType>('REVISION_REQUIRED');
  readonly feedback = signal<string>(
    'Chiều ngày 05/10 scan thiếu chữ ký trang 3 của 2 hồ sơ KH (CIF #8812 và #8819).\nYêu cầu nhân viên scan bổ sung đầy đủ chữ ký để thẩm định lại!',
  );
  readonly resubmitDeadline = signal<string>('2026-10-08T17:30');

  // UI interaction signals
  readonly showConfirmModal = signal<boolean>(false);
  readonly isSubmitting = signal<boolean>(false);
  readonly decisionResult = signal<AuditDecisionResponse | null>(null);
  readonly validationError = signal<string | null>(null);
  readonly activeNav = signal<string>('Thẩm định kết quả');
  readonly simulationScenario = signal<string>('default');

  // Computed state rules
  readonly isMakerCheckerViolated = computed(() => {
    const currentDossier = this.dossier();
    if (!currentDossier) {
      return false;
    }
    return currentDossier.submitter.employeeId === this.currentUserId();
  });

  readonly isAllStandardsPassed = computed(() => {
    return this.controlStandards().every((std) => std.passed);
  });

  readonly isFeedbackValid = computed(() => {
    const dec = this.selectedDecision();
    if (dec === 'APPROVED') {
      return true;
    }
    return this.feedback().trim().length >= 10;
  });

  readonly isDeadlineValid = computed(() => {
    if (this.selectedDecision() !== 'REVISION_REQUIRED') {
      return true;
    }
    return this.resubmitDeadline().trim().length > 0;
  });

  readonly canSubmitDecision = computed(() => {
    if (this.isMakerCheckerViolated()) {
      return false;
    }
    if (this.viewState() !== 'success') {
      return false;
    }
    if (this.isSubmitting()) {
      return false;
    }
    return this.isFeedbackValid() && this.isDeadlineValid() && this.verifiedValue() >= 0;
  });

  readonly activeEvidenceFile = computed(() => {
    const currentDossier = this.dossier();
    if (!currentDossier) {
      return null;
    }
    return (
      currentDossier.evidenceFiles.find((f) => f.fileId === this.activeFileId()) ??
      currentDossier.evidenceFiles[0]
    );
  });

  ngOnInit(): void {
    this.loadDossier();
  }

  loadDossier(submissionId?: string): void {
    this.viewState.set('loading');
    this.reviewService.getDossier(submissionId).subscribe({
      next: (data) => {
        this.dossier.set(data);
        this.controlStandards.set([...data.controlStandards]);
        this.verifiedValue.set(data.officialVerifiedValue);
        this.docCurrentPage.set(data.documentPreview.currentPage);
        this.docZoom.set(data.documentPreview.zoomPercent);
        if (data.evidenceFiles.length > 0) {
          this.activeFileId.set(data.evidenceFiles[1]?.fileId ?? data.evidenceFiles[0].fileId);
        }
        this.viewState.set('success');
      },
      error: () => {
        this.viewState.set('error');
      },
    });
  }

  toggleStandard(standardId: string): void {
    this.controlStandards.update((list) =>
      list.map((std) => (std.id === standardId ? { ...std, passed: !std.passed } : std)),
    );
  }

  selectDecision(decision: DecisionType): void {
    if (this.isMakerCheckerViolated()) {
      return;
    }
    this.selectedDecision.set(decision);
    this.validationError.set(null);
  }

  setVerifiedValue(event: Event): void {
    const val = Number((event.target as HTMLInputElement).value);
    if (!Number.isNaN(val) && val >= 0) {
      this.verifiedValue.set(val);
    }
  }

  setFeedback(event: Event): void {
    this.feedback.set((event.target as HTMLTextAreaElement).value);
  }

  setResubmitDeadline(event: Event): void {
    this.resubmitDeadline.set((event.target as HTMLInputElement).value);
  }

  zoomIn(): void {
    this.docZoom.update((z) => Math.min(200, z + 15));
  }

  zoomOut(): void {
    this.docZoom.update((z) => Math.max(50, z - 15));
  }

  prevPage(): void {
    this.docCurrentPage.update((p) => Math.max(1, p - 1));
  }

  nextPage(): void {
    const total = this.dossier()?.documentPreview.totalPages ?? 3;
    this.docCurrentPage.update((p) => Math.min(total, p + 1));
  }

  selectFile(file: EvidenceFile): void {
    this.activeFileId.set(file.fileId);
  }

  openConfirmModal(): void {
    if (this.isMakerCheckerViolated()) {
      this.validationError.set(this.localization.review.makerCheckerError);
      return;
    }
    if (!this.isFeedbackValid()) {
      this.validationError.set(this.localization.review.feedbackError);
      return;
    }
    if (!this.isDeadlineValid()) {
      this.validationError.set(this.localization.review.deadlineError);
      return;
    }
    this.validationError.set(null);
    this.showConfirmModal.set(true);
  }

  closeConfirmModal(): void {
    this.showConfirmModal.set(false);
  }

  confirmDecision(): void {
    const currentDossier = this.dossier();
    if (!currentDossier || !this.canSubmitDecision()) {
      return;
    }

    this.isSubmitting.set(true);
    const payload: AuditDecisionPayload = {
      decision: this.selectedDecision(),
      verifiedValue: this.verifiedValue(),
      checklistResults: this.controlStandards().map((std) => ({
        standardId: std.id,
        isPassed: std.passed,
      })),
      feedback: this.feedback(),
      resubmitDeadline:
        this.selectedDecision() === 'REVISION_REQUIRED' ? this.resubmitDeadline() : undefined,
    };

    this.reviewService.submitDecision(currentDossier.submissionId, payload).subscribe({
      next: (response) => {
        this.isSubmitting.set(false);
        this.showConfirmModal.set(false);
        this.decisionResult.set(response);
      },
      error: () => {
        this.isSubmitting.set(false);
        this.showConfirmModal.set(false);
      },
    });
  }

  resetDecisionState(): void {
    this.decisionResult.set(null);
    this.validationError.set(null);
  }

  selectNav(item: string): void {
    this.activeNav.set(item);
  }

  switchScenario(scenario: string): void {
    this.simulationScenario.set(scenario);
    this.decisionResult.set(null);
    this.validationError.set(null);

    switch (scenario) {
      case 'default':
        this.viewState.set('success');
        this.currentUserId.set('EMP-MGR-001');
        this.dossier.set(DEFAULT_MOCK_DOSSIER);
        this.controlStandards.set([...DEFAULT_MOCK_DOSSIER.controlStandards]);
        this.selectedDecision.set('REVISION_REQUIRED');
        this.verifiedValue.set(DEFAULT_MOCK_DOSSIER.officialVerifiedValue);
        this.feedback.set(
          'Chiều ngày 05/10 scan thiếu chữ ký trang 3 của 2 hồ sơ KH (CIF #8812 và #8819).\nYêu cầu nhân viên scan bổ sung đầy đủ chữ ký để thẩm định lại!',
        );
        this.resubmitDeadline.set('2026-10-08T17:30');
        break;

      case 'approved':
        this.viewState.set('success');
        this.currentUserId.set('EMP-MGR-001');
        this.dossier.set(APPROVED_MOCK_DOSSIER);
        this.controlStandards.set([...APPROVED_MOCK_DOSSIER.controlStandards]);
        this.selectedDecision.set('APPROVED');
        this.verifiedValue.set(APPROVED_MOCK_DOSSIER.officialVerifiedValue);
        this.feedback.set('Hồ sơ hợp lệ, số lượng đối soát khớp với sao kê Core Banking.');
        break;

      case 'maker-checker':
        this.viewState.set('success');
        this.currentUserId.set('EMP-MGR-001');
        this.dossier.set(MAKER_CHECKER_VIOLATION_DOSSIER);
        this.controlStandards.set([...DEFAULT_MOCK_DOSSIER.controlStandards]);
        this.selectedDecision.set('APPROVED');
        break;

      case 'loading':
        this.viewState.set('loading');
        break;

      case 'empty':
        this.viewState.set('empty');
        this.dossier.set(null);
        break;

      case 'error':
        this.viewState.set('error');
        this.dossier.set(null);
        break;
    }
  }
}
