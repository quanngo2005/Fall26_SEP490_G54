import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, HostListener, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, EMPTY, finalize, map, Subject, switchMap, tap } from 'rxjs';
import { AppealDecision, AppealDetail, AppealEvidence } from '../../core/appeal.models';
import { AppealService } from '../../core/appeal.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe } from '../../shared/pipes';
import { displayBytes } from '../../shared/ui/display';
import {
  appealCode,
  appealErrorKey,
  appealSubject,
  OPEN_STATUSES,
  openBlob,
  typeKey,
} from './appeal-display';

/** The decisions the API accepts for each kind of appeal (docs/api/APPEAL_CONTRACT.md). */
const RESULT_DECISIONS: readonly AppealDecision[] = ['ACCEPT', 'ADJUST', 'REJECT'];
const SCORE_DECISIONS: readonly AppealDecision[] = ['ADJUST', 'UPHOLD'];

@Component({
  selector: 'app-department-appeal-detail',
  imports: [FormsModule, SidebarComponent, HeaderComponent, AppDatePipe],
  templateUrl: './department-appeal-detail.component.html',
  styleUrls: [
    './appeals-shared.scss',
    './department-appeal-detail.component.scss',
    './appeal-evidence.scss',
    './appeal-decision-modal.scss',
  ],
})
export class DepartmentAppealDetailComponent extends LocalizedComponent {
  private readonly api = inject(AppealService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly load = new Subject<string>();

  protected readonly id = signal<string | null>(null);
  protected readonly appeal = signal<AppealDetail | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly notFound = signal(false);
  protected readonly previewFailed = signal(false);

  protected readonly showDecisionModal = signal(false);
  protected readonly decisionType = signal<AppealDecision | null>(null);
  protected readonly decisionNote = signal('');
  protected readonly decisionPoints = signal<number | null>(null);
  protected readonly showValidation = signal(false);
  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly code = computed(() => {
    const id = this.id();
    return id ? appealCode(id) : '';
  });
  protected readonly decisions = computed<readonly AppealDecision[]>(() =>
    this.appeal()?.type === 'SCORE' ? SCORE_DECISIONS : RESULT_DECISIONS,
  );
  protected readonly canDecide = computed(() => {
    const appeal = this.appeal();
    return appeal !== null && OPEN_STATUSES.includes(appeal.status) && !this.submitting();
  });
  protected readonly noteValid = computed(() => this.decisionNote().trim().length > 0);
  /** A score appeal adjusted with points needs a positive amount; every other decision sends none. */
  protected readonly pointsNeeded = computed(
    () => this.appeal()?.type === 'SCORE' && this.decisionType() === 'ADJUST',
  );
  protected readonly pointsValid = computed(() => {
    const points = this.decisionPoints();
    return !this.pointsNeeded() || (points !== null && points > 0);
  });

  constructor() {
    super();
    this.load
      .pipe(
        switchMap((id) => {
          this.appeal.set(null);
          this.failed.set(false);
          this.notFound.set(false);
          this.loading.set(true);
          return this.api.detail(id).pipe(
            tap((appeal) => this.appeal.set(appeal)),
            catchError((error: unknown) => {
              this.failed.set(true);
              this.notFound.set(error instanceof HttpErrorResponse && error.status === 404);
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
        map((params) => params.get('id')),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((id) => {
        this.id.set(id);
        if (id) this.load.next(id);
      });
  }

  protected retry(): void {
    const id = this.id();
    if (id) this.load.next(id);
  }

  protected goBack(): void {
    void this.router.navigate(['/appeals/department']);
  }

  protected openDecision(): void {
    this.decisionType.set(null);
    this.decisionNote.set('');
    this.decisionPoints.set(null);
    this.showValidation.set(false);
    this.errorKey.set(null);
    this.showDecisionModal.set(true);
  }

  protected closeDecision(): void {
    if (!this.submitting()) this.showDecisionModal.set(false);
  }

  protected selectDecision(decision: AppealDecision): void {
    this.decisionType.set(decision);
    this.errorKey.set(null);
  }

  protected confirmDecision(): void {
    this.showValidation.set(true);
    const appeal = this.appeal();
    const decision = this.decisionType();
    if (!appeal || !decision || !this.noteValid() || !this.pointsValid() || this.submitting()) {
      return;
    }

    this.errorKey.set(null);
    this.submitting.set(true);
    this.api
      .decide(appeal.id, {
        decision,
        note: this.decisionNote().trim(),
        ...(this.pointsNeeded() ? { points: this.decisionPoints() } : {}),
      })
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.appeal.set(result.appeal);
          this.showDecisionModal.set(false);
          this.notice.set(this.i18n.translate('appeals.decided', { code: this.code() }));
        },
        error: (error: unknown) => this.errorKey.set(appealErrorKey(error)),
      });
  }

  protected preview(file: AppealEvidence): void {
    this.previewFailed.set(false);
    this.api
      .evidence(file.previewUrl)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => openBlob(blob),
        error: () => this.previewFailed.set(true),
      });
  }

  protected kindLabel(): string {
    const appeal = this.appeal();
    return appeal ? this.localization.appeals[typeKey(appeal.type)] : '';
  }

  protected headline(): string {
    const appeal = this.appeal();
    return this.i18n.translate('appeals.detailHeadline', {
      kind: this.kindLabel(),
      employee: appeal?.employeeName ?? '',
    });
  }

  protected subject(): string {
    const appeal = this.appeal();
    return (appeal && appealSubject(appeal)) ?? this.localization.appeals.notAvailable;
  }

  protected scoreText(): string {
    const score = this.appeal()?.evaluationScore;
    return score == null ? this.localization.appeals.notAvailable : this.i18n.formatNumber(score);
  }

  protected referrerText(): string {
    const appeal = this.appeal();
    if (!appeal?.referrerEmployeeCode) return this.localization.appeals.notAvailable;
    return appeal.referrerEmployeeName
      ? `${appeal.referrerEmployeeCode} · ${appeal.referrerEmployeeName}`
      : appeal.referrerEmployeeCode;
  }

  protected decisionTitle(): string {
    return this.i18n.translate('appeals.decisionTitle', { kind: this.kindLabel() });
  }

  protected decisionLabel(decision: AppealDecision): string {
    const text = this.localization.appeals;
    const score = this.appeal()?.type === 'SCORE';
    switch (decision) {
      case 'ACCEPT':
        return text.decisionAccept;
      case 'ADJUST':
        return score ? text.decisionAdjustScore : text.decisionAdjust;
      case 'REJECT':
        return text.decisionReject;
      case 'UPHOLD':
        return text.decisionUphold;
    }
  }

  protected decisionHint(decision: AppealDecision): string {
    const text = this.localization.appeals;
    const score = this.appeal()?.type === 'SCORE';
    switch (decision) {
      case 'ACCEPT':
        return text.decisionAcceptHint;
      case 'ADJUST':
        return score ? text.decisionAdjustScoreHint : text.decisionAdjustHint;
      case 'REJECT':
        return text.decisionRejectHint;
      case 'UPHOLD':
        return text.decisionUpholdHint;
    }
  }

  protected size(bytes: number | null): string {
    return displayBytes(bytes, this.i18n.currentLanguage(), this.localization.appeals.notAvailable);
  }

  protected errorText(): string {
    const key = this.errorKey();
    return key ? this.i18n.translate(key) : '';
  }

  @HostListener('document:keydown', ['$event'])
  protected handleKeyboard(event: KeyboardEvent): void {
    if (!this.showDecisionModal()) return;

    if (event.key === 'Escape') {
      event.preventDefault();
      this.closeDecision();
    } else if (event.key === 'Enter' && event.ctrlKey) {
      event.preventDefault();
      this.confirmDecision();
    }
  }
}
