import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { catchError, EMPTY, finalize, forkJoin, map, of, Subject, switchMap, tap } from 'rxjs';
import { apiErrorCode, apiErrorStatus } from '../../core/api-utils';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  ControlStandard,
  DecisionType,
  VerificationDecisionPayload,
  VerificationDecisionResult,
  VerificationDossier,
  VerificationHistoryItem,
} from '../../core/verification.models';
import { VerificationService } from '../../core/verification.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { displayBytes, displayNumber, initials } from '../../shared/ui/display';

const MIN_FEEDBACK_LENGTH = 10;
const DECIDABLE = ['SUBMITTED', 'UNDER_REVIEW'];

@Component({
  selector: 'app-review-verify',
  standalone: true,
  imports: [FormsModule, HeaderComponent, SidebarComponent],
  templateUrl: './review-verify.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/modal.scss',
    './review-verify.component.scss',
  ],
})
export class ReviewVerifyComponent extends LocalizedComponent {
  private readonly api = inject(VerificationService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  protected readonly submissionId = signal<string | null>(null);
  protected readonly dossier = signal<VerificationDossier | null>(null);
  protected readonly history = signal<VerificationHistoryItem[]>([]);
  protected readonly standards = signal<ControlStandard[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly notFound = signal(false);

  protected readonly decision = signal<DecisionType | null>(null);
  protected readonly verifiedValue = signal<number | null>(null);
  protected readonly feedback = signal('');
  protected readonly deadline = signal('');
  protected readonly confirmOpen = signal(false);
  protected readonly submitting = signal(false);
  protected readonly result = signal<VerificationDecisionResult | null>(null);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly showValidation = signal(false);

  protected readonly canDecide = computed(() => {
    const status = this.dossier()?.status;
    return !!status && DECIDABLE.includes(status);
  });
  protected readonly feedbackRequired = computed(
    () => this.decision() === 'REVISION_REQUIRED' || this.decision() === 'REJECTED',
  );
  protected readonly feedbackValid = computed(
    () => !this.feedbackRequired() || this.feedback().trim().length >= MIN_FEEDBACK_LENGTH,
  );
  protected readonly deadlineValid = computed(() => {
    if (this.decision() !== 'REVISION_REQUIRED') return true;
    const time = new Date(this.deadline()).getTime();
    return Number.isFinite(time) && time > Date.now();
  });
  protected readonly valueValid = computed(() => {
    if (this.decision() !== 'APPROVED') return true;
    const value = this.verifiedValue();
    return value !== null && value >= 0;
  });
  protected readonly formValid = computed(
    () =>
      this.decision() !== null && this.feedbackValid() && this.deadlineValid() && this.valueValid(),
  );

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          const id = this.submissionId();
          if (!id) {
            this.loading.set(false);
            return EMPTY;
          }
          this.loading.set(true);
          this.failed.set(false);
          this.notFound.set(false);
          return forkJoin({
            dossier: this.api.dossier(id),
            // History is supplementary; the dossier must still render when it is unavailable.
            history: this.api.history(id).pipe(catchError(() => of([]))),
          }).pipe(
            tap(({ dossier, history }) => this.apply(dossier, history)),
            catchError((error: unknown) => {
              this.notFound.set(apiErrorStatus(error) === 404);
              this.failed.set(true);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    this.route.paramMap
      .pipe(
        map((params) => params.get('submissionId')),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((id) => {
        this.submissionId.set(id);
        this.dossier.set(null);
        this.history.set([]);
        this.result.set(null);
        this.reload.next();
      });
  }

  protected refresh(): void {
    this.reload.next();
  }

  protected toggleStandard(id: string): void {
    this.standards.update((list) =>
      list.map((standard) =>
        standard.id === id ? { ...standard, passed: !standard.passed } : standard,
      ),
    );
  }

  protected selectDecision(decision: DecisionType): void {
    this.decision.set(decision);
    this.errorKey.set(null);
  }

  protected setVerifiedValue(raw: string | number | null): void {
    const value = raw === '' || raw === null ? null : Number(raw);
    this.verifiedValue.set(value !== null && Number.isFinite(value) ? value : null);
  }

  protected openConfirm(): void {
    this.showValidation.set(true);
    if (!this.formValid()) return;
    this.errorKey.set(null);
    this.confirmOpen.set(true);
  }

  protected closeConfirm(): void {
    if (!this.submitting()) this.confirmOpen.set(false);
  }

  protected confirm(): void {
    const id = this.submissionId();
    const decision = this.decision();
    if (!id || !decision || !this.formValid() || this.submitting()) return;
    const payload: VerificationDecisionPayload = {
      decision,
      verifiedValue: decision === 'APPROVED' ? this.verifiedValue() : null,
      feedback: this.feedback().trim() || null,
      resubmitDeadline:
        decision === 'REVISION_REQUIRED' ? new Date(this.deadline()).toISOString() : null,
      checklistResults: this.standards().map((standard) => ({
        standardId: standard.id,
        isPassed: standard.passed,
      })),
    };
    this.submitting.set(true);
    this.api
      .submitDecision(id, payload)
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.confirmOpen.set(false);
          this.result.set(result);
          this.showValidation.set(false);
          this.reload.next();
        },
        error: (error: unknown) => {
          this.confirmOpen.set(false);
          this.errorKey.set(this.errorKeyFor(error));
        },
      });
  }

  protected dismissResult(): void {
    this.result.set(null);
  }

  protected errorText(): string {
    const key = this.errorKey();
    return key ? this.i18n.translate(key) : '';
  }

  protected display(value: number | null | undefined): string {
    return displayNumber(value, this.i18n.currentLanguage(), this.localization.review.notAvailable);
  }

  protected size(bytes: number | null): string {
    return displayBytes(bytes, this.i18n.currentLanguage(), this.localization.review.notAvailable);
  }

  protected initials(name: string): string {
    return initials(name);
  }

  protected statusLabel(status: string): string {
    const text = this.localization.review;
    switch (status) {
      case 'DRAFT':
        return text.statusDraft;
      case 'SUBMITTED':
        return text.statusSubmitted;
      case 'UNDER_REVIEW':
        return text.statusUnderReview;
      case 'APPROVED':
        return text.statusApproved;
      case 'REJECTED':
        return text.statusRejected;
      case 'WITHDRAWN':
        return text.statusWithdrawn;
      default:
        return text.notAvailable;
    }
  }

  protected decisionLabel(decision: string): string {
    const text = this.localization.review;
    switch (decision) {
      case 'APPROVED':
        return text.approvedSummary;
      case 'REVISION_REQUIRED':
        return text.revisionSummary;
      case 'REJECTED':
        return text.rejectedSummary;
      case 'PENDING':
        return text.pendingSummary;
      default:
        return text.notAvailable;
    }
  }

  private apply(dossier: VerificationDossier, history: VerificationHistoryItem[]): void {
    this.dossier.set(dossier);
    this.history.set(history);
    this.standards.set(dossier.controlStandards.map((standard) => ({ ...standard })));
    if (this.verifiedValue() === null) {
      this.verifiedValue.set(dossier.kpiItem?.declaredValue ?? null);
    }
  }

  /** Branches on the stable HTTP status / errorCode, never on the message text. */
  private errorKeyFor(error: unknown): string {
    switch (apiErrorCode(error)) {
      case 'MSG10':
        return 'review.makerCheckerError';
      case 'MSG23':
        return 'review.lockedError';
      case 'VERIFICATION_INVALID_STATE':
        return 'review.invalidStateError';
      case 'VALIDATION_ERROR':
        return 'review.validationError';
      case 'NOT_FOUND':
        return 'review.notFound';
      default:
        return apiErrorStatus(error) === 403 ? 'review.forbiddenError' : 'review.decisionFailed';
    }
  }
}
