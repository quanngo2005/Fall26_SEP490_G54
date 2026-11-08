import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  computed,
  DestroyRef,
  HostListener,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, EMPTY, finalize, switchMap, tap } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { RiskEventAttachment, RiskEventDetails, RiskSeverity } from '../../core/risk-event.models';
import { RiskEventService } from '../../core/risk-event.service';
import { AppDatePipe } from '../../shared/pipes';
import { displayBytes } from '../../shared/ui/display';
import { openBlob } from '../appeals/appeal-display';
import { MIN_REVERSAL_REASON, riskErrorKey } from './risk-event-display';

/** The right-hand drawer of the risk event list (SCR-RSK-01-D1): the full record and the reversal. */
@Component({
  selector: 'app-risk-event-drawer',
  imports: [FormsModule, AppDatePipe],
  templateUrl: './risk-event-drawer.component.html',
  styleUrls: ['./risk-events-badges.scss', './risk-event-drawer.component.scss'],
})
export class RiskEventDrawerComponent extends LocalizedComponent {
  private readonly api = inject(RiskEventService);
  private readonly destroyRef = inject(DestroyRef);

  readonly eventId = input.required<string>();
  readonly closed = output<void>();
  /** Emits the code of the event after it was reversed, so the list can refresh. */
  readonly reversed = output<string>();

  protected readonly details = signal<RiskEventDetails | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly notFound = signal(false);
  protected readonly previewFailed = signal(false);

  protected readonly reversing = signal(false);
  protected readonly reason = signal('');
  protected readonly showValidation = signal(false);
  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  protected readonly reasonValid = computed(
    () => this.reason().trim().length >= MIN_REVERSAL_REASON,
  );
  protected readonly isReversed = computed(() => this.details()?.item.state === 'REVERSED');

  constructor() {
    super();
    toObservable(this.eventId)
      .pipe(
        switchMap((id) => {
          this.details.set(null);
          this.failed.set(false);
          this.notFound.set(false);
          this.previewFailed.set(false);
          this.reversing.set(false);
          this.loading.set(true);
          return this.api.details(id).pipe(
            tap((details) => this.details.set(details)),
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
  }

  protected close(): void {
    this.closed.emit();
  }

  protected openReverse(): void {
    this.reason.set('');
    this.showValidation.set(false);
    this.errorKey.set(null);
    this.reversing.set(true);
  }

  protected cancelReverse(): void {
    if (!this.submitting()) this.reversing.set(false);
  }

  protected confirmReverse(): void {
    this.showValidation.set(true);
    const current = this.details();
    if (!current || !this.reasonValid() || this.submitting()) return;
    this.errorKey.set(null);
    this.submitting.set(true);
    this.api
      .reverse(current.item.id, this.reason())
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (updated) => {
          this.details.set(updated);
          this.reversing.set(false);
          this.reversed.emit(updated.item.code);
        },
        error: (error: unknown) => this.errorKey.set(riskErrorKey(error, 'reverse')),
      });
  }

  protected preview(file: RiskEventAttachment): void {
    this.previewFailed.set(false);
    this.api
      .evidence(file.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => openBlob(blob),
        error: () => this.previewFailed.set(true),
      });
  }

  protected titleText(): string {
    const item = this.details()?.item;
    return item
      ? this.i18n.translate('riskEvents.drawer.title', {
          code: item.code,
          type: this.typeLabel(item.eventType),
        })
      : '';
  }

  protected typeLabel(code: string): string {
    return (this.localization.riskEvents.types as Record<string, string>)[code] ?? code;
  }

  protected severityLabel(severity: RiskSeverity): string {
    return this.localization.riskEvents.severity[severity];
  }

  protected recordedByText(): string {
    const name = this.details()?.recordedByName;
    return name ? this.i18n.translate('riskEvents.drawer.recordedBy', { name }) : '';
  }

  protected penaltyText(): string {
    const points = this.details()?.item.penaltyPoints ?? 0;
    return this.i18n.translate('riskEvents.drawer.penaltyValue', {
      points: this.i18n.formatNumber(points),
    });
  }

  protected periodText(): string {
    const details = this.details();
    return details?.periodName ?? details?.periodCode ?? '—';
  }

  protected size(bytes: number | null): string {
    return displayBytes(bytes, this.i18n.currentLanguage(), '—');
  }

  protected errorText(): string {
    const key = this.errorKey();
    return key ? this.i18n.translate(key) : '';
  }

  protected historyRecorded(): string {
    return this.i18n.translate('riskEvents.drawer.historyRecorded', {
      name: this.details()?.recordedByName ?? '—',
    });
  }

  protected historyReversed(): string {
    return this.i18n.translate('riskEvents.drawer.historyReversed', {
      name: this.details()?.reversedByName ?? '—',
    });
  }

  protected historyReason(): string {
    return this.i18n.translate('riskEvents.drawer.historyReason', {
      reason: this.details()?.reversalReason ?? '',
    });
  }

  @HostListener('document:keydown.escape', ['$event'])
  protected onEscape(event: Event): void {
    event.preventDefault();
    if (this.reversing()) this.cancelReverse();
    else this.close();
  }
}
