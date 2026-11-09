import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, EMPTY, finalize, map, Subject, switchMap, tap } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  QaEvidence,
  QaKind,
  QaQueueDetail,
  QaSeverity,
  QaTransaction,
  QaVerdict,
} from '../../core/qa-queue.models';
import { QaQueueService } from '../../core/qa-queue.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { displayBytes } from '../../shared/ui/display';
import { openBlob } from '../appeals/appeal-display';
import { isKind, QaQueueContext, qaErrorKey } from './qa-queue-display';

const MIN_REASON_LENGTH = 10;
const SEVERITIES: readonly QaSeverity[] = ['LOW', 'MEDIUM', 'HIGH'];

@Component({
  selector: 'app-qa-queue-detail',
  imports: [FormsModule, SidebarComponent, HeaderComponent],
  templateUrl: './qa-queue-detail.component.html',
  styleUrls: [
    '../appeals/appeals-shared.scss',
    '../appeals/appeals-list.scss',
    '../appeals/department-appeal-detail.component.scss',
    '../appeals/appeal-evidence.scss',
    './qa-queue-badges.scss',
    './qa-queue-detail.component.scss',
  ],
})
export class QaQueueDetailComponent extends LocalizedComponent {
  private readonly api = inject(QaQueueService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly context = inject(QaQueueContext);
  private readonly destroyRef = inject(DestroyRef);
  private readonly load = new Subject<{ kind: QaKind; id: string }>();

  protected readonly severities = SEVERITIES;
  protected readonly target = signal<{ kind: QaKind; id: string } | null>(null);
  protected readonly detail = signal<QaQueueDetail | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly notFound = signal(false);
  protected readonly previewFailed = signal(false);

  protected readonly verdict = signal<QaVerdict>('CONFIRM');
  protected readonly comment = signal('');
  protected readonly severity = signal<QaSeverity>('MEDIUM');
  protected readonly requestRework = signal(true);
  protected readonly showValidation = signal(false);
  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly isPending = computed(() => this.detail()?.item.tab === 'PENDING');
  protected readonly isSample = computed(() => this.detail()?.item.kind === 'SAMPLE');
  protected readonly commentValid = computed(
    () => this.verdict() === 'CONFIRM' || this.comment().trim().length >= MIN_REASON_LENGTH,
  );
  protected readonly canSave = computed(() => this.isPending() && !this.submitting());
  /** The explanation under the reason box follows what the chosen verdict will do for this kind of item. */
  protected readonly verdictNote = computed(() => {
    const text = this.localization.qaQueue;
    if (this.verdict() === 'CONFIRM') return this.isSample() ? '' : text.dismissNote;
    return this.isSample() && !this.requestRework() ? text.rejectNoteFlag : text.rejectNoteRework;
  });
  private readonly position = computed(() => {
    const target = this.target();
    return target
      ? this.context.items().findIndex((i) => i.kind === target.kind && i.id === target.id)
      : -1;
  });
  protected readonly previous = computed(() => this.context.items()[this.position() - 1] ?? null);
  protected readonly next = computed(() => {
    const index = this.position();
    return index < 0 ? null : (this.context.items()[index + 1] ?? null);
  });

  constructor() {
    super();
    this.load
      .pipe(
        switchMap(({ kind, id }) => {
          this.detail.set(null);
          this.failed.set(false);
          this.notFound.set(false);
          this.loading.set(true);
          return this.api.detail(kind, id).pipe(
            tap((detail) => this.detail.set(detail)),
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
        map((params) => ({ kind: params.get('kind'), id: params.get('id') })),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ kind, id }) => {
        const upper = kind?.toUpperCase() ?? null;
        if (!id || !isKind(upper)) {
          this.target.set(null);
          this.failed.set(true);
          this.notFound.set(true);
          return;
        }
        this.target.set({ kind: upper, id });
        this.resetForm();
        this.notice.set(null);
        this.load.next({ kind: upper, id });
      });
  }

  protected retry(): void {
    const target = this.target();
    if (target) this.load.next(target);
  }

  protected goBack(): void {
    void this.router.navigate(['/qa-queue']);
  }

  protected step(entry: { kind: QaKind; id: string } | null): void {
    if (entry) void this.router.navigate(['/qa-queue', entry.kind, entry.id]);
  }

  protected selectVerdict(verdict: QaVerdict): void {
    this.verdict.set(verdict);
    this.errorKey.set(null);
  }

  protected cancel(): void {
    this.resetForm();
    this.goBack();
  }

  protected save(): void {
    this.showValidation.set(true);
    const target = this.target();
    const detail = this.detail();
    if (!target || !detail || !this.canSave() || !this.commentValid()) return;

    const verdict = this.verdict();
    this.errorKey.set(null);
    this.submitting.set(true);
    this.api
      .decide(target.kind, target.id, {
        verdict,
        comment: this.comment(),
        severity: this.severity(),
        requestRework: this.requestRework(),
      })
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.notice.set(this.i18n.translate('qaQueue.decided', { code: detail.item.code }));
          this.resetForm();
          // A sample that failed is now represented by the flag it raised, so reopen the queue instead of the stale item.
          if (target.kind === 'SAMPLE' && verdict === 'REJECT') this.goBack();
          else this.load.next(target);
        },
        error: (error: unknown) => this.errorKey.set(qaErrorKey(error)),
      });
  }

  protected preview(file: QaEvidence): void {
    this.previewFailed.set(false);
    this.api
      .evidence(file.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => openBlob(blob),
        error: () => this.previewFailed.set(true),
      });
  }

  protected kindLabel(): string {
    return this.detail()?.item.type === 'SAMPLE'
      ? this.localization.qaQueue.kindSample
      : this.localization.qaQueue.kindFlag;
  }

  protected statusLabel(): string {
    const detail = this.detail();
    return detail ? this.localization.qaQueue.status[detail.item.status] : '';
  }

  protected transactionStatus(transaction: QaTransaction): string {
    return this.localization.qaQueue.transactionStatus[transaction.status];
  }

  protected versionText(): string {
    const detail = this.detail();
    return detail
      ? this.i18n.translate('qaQueue.evidenceVersion', {
          no: detail.item.submissionNo,
          date: this.i18n.formatDate(detail.submittedAt, 'DD/MM/YYYY HH:mm'),
        })
      : '';
  }

  protected reviewedText(): string {
    const detail = this.detail();
    if (!detail?.reviewedAt) return '';
    return this.i18n.translate('qaQueue.reviewedBy', {
      name: detail.reviewedByName ?? '—',
      date: this.i18n.formatDate(detail.reviewedAt, 'DD/MM/YYYY HH:mm'),
    });
  }

  protected alreadyDecidedText(): string {
    return this.i18n.translate('qaQueue.alreadyDecided', { status: this.statusLabel() });
  }

  protected severityLabel(severity: QaSeverity): string {
    return this.localization.qaQueue.severity[severity];
  }

  protected size(bytes: number | null): string {
    return displayBytes(bytes, this.i18n.currentLanguage(), '—');
  }

  protected errorText(): string {
    const key = this.errorKey();
    return key ? this.i18n.translate(key) : '';
  }

  private resetForm(): void {
    this.verdict.set('CONFIRM');
    this.comment.set('');
    this.severity.set('MEDIUM');
    this.requestRework.set(true);
    this.showValidation.set(false);
    this.errorKey.set(null);
  }
}
