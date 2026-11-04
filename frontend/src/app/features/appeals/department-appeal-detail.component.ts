import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { catchError, EMPTY, finalize, map, Subject, switchMap, tap } from 'rxjs';
import { AppealDetail, AppealEvidence } from '../../core/appeal.models';
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

type DecisionChoice = 'ACCEPT' | 'ADJUST' | 'REJECT';

@Component({
  selector: 'app-department-appeal-detail',
  imports: [FormsModule, RouterModule, SidebarComponent, HeaderComponent, AppDatePipe],
  templateUrl: './department-appeal-detail.component.html',
  styleUrls: ['./appeals-shared.scss', './department-appeal-detail.component.scss'],
})
export class DepartmentAppealDetailComponent extends LocalizedComponent {
  private readonly api = inject(AppealService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly load = new Subject<string>();

  protected readonly decisions: readonly DecisionChoice[] = ['ACCEPT', 'ADJUST', 'REJECT'];

  protected readonly id = signal<string | null>(null);
  protected readonly appeal = signal<AppealDetail | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly notFound = signal(false);
  protected readonly previewFailed = signal(false);

  protected readonly showDecisionModal = signal(false);
  protected readonly decisionType = signal<DecisionChoice | null>(null);
  protected readonly decisionNote = signal('');
  protected readonly showValidation = signal(false);
  protected readonly submitting = signal(false);
  protected readonly unsupported = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly code = computed(() => {
    const id = this.id();
    return id ? appealCode(id) : '';
  });
  protected readonly canDecide = computed(() => {
    const appeal = this.appeal();
    return appeal !== null && OPEN_STATUSES.includes(appeal.status) && !this.submitting();
  });
  protected readonly noteValid = computed(() => this.decisionNote().trim().length > 0);

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
    this.showValidation.set(false);
    this.unsupported.set(false);
    this.errorKey.set(null);
    this.showDecisionModal.set(true);
  }

  protected closeDecision(): void {
    if (!this.submitting()) this.showDecisionModal.set(false);
  }

  protected selectDecision(decision: DecisionChoice): void {
    this.decisionType.set(decision);
    this.unsupported.set(false);
    this.errorKey.set(null);
  }

  protected confirmDecision(): void {
    this.showValidation.set(true);
    const appeal = this.appeal();
    const decision = this.decisionType();
    if (!appeal || !decision || !this.noteValid() || this.submitting()) return;

    this.unsupported.set(false);
    this.errorKey.set(null);
    // A score appeal can only keep the score: the other decisions need the points the screen does not collect.
    if (appeal.type === 'SCORE' && decision !== 'REJECT') {
      this.unsupported.set(true);
      return;
    }

    this.submitting.set(true);
    this.api
      .decide(appeal.id, {
        decision: appeal.type === 'SCORE' ? 'UPHOLD' : decision,
        note: this.decisionNote().trim(),
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

  protected decisionTitle(): string {
    return this.i18n.translate('appeals.decisionTitle', { kind: this.kindLabel() });
  }

  protected decisionLabel(decision: DecisionChoice): string {
    const text = this.localization.appeals;
    switch (decision) {
      case 'ACCEPT':
        return text.decisionAccept;
      case 'ADJUST':
        return text.decisionAdjust;
      case 'REJECT':
        return text.decisionReject;
    }
  }

  protected decisionHint(decision: DecisionChoice): string {
    const text = this.localization.appeals;
    switch (decision) {
      case 'ACCEPT':
        return text.decisionAcceptHint;
      case 'ADJUST':
        return text.decisionAdjustHint;
      case 'REJECT':
        return text.decisionRejectHint;
    }
  }

  protected size(bytes: number | null): string {
    return displayBytes(bytes, this.i18n.currentLanguage(), this.localization.appeals.notAvailable);
  }

  protected errorText(): string {
    const key = this.errorKey();
    return key ? this.i18n.translate(key) : '';
  }
}
