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
import { catchError, EMPTY, finalize, map, merge, Observable, Subject, switchMap, tap } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { canTransitionRiskEvents } from '../../core/risk-event.guard';
import {
  RiskEventAttachment,
  RiskEventDetails,
  RiskSeverity,
  RiskStatus,
  RiskTransition,
} from '../../core/risk-event.models';
import { RiskEventService } from '../../core/risk-event.service';
import { AppDatePipe } from '../../shared/pipes';
import { displayBytes } from '../../shared/ui/display';
import { openBlob } from '../appeals/appeal-display';
import { MIN_REVERSAL_REASON, riskErrorKey } from './risk-event-display';

/** What the drawer changed, so the list can refresh and say so. */
export interface RiskEventChange {
  code: string;
  action: RiskTransition | 'reverse';
}

/** The lifecycle moves each status offers (contract §1); the reversal follows `canReverse`. */
const TRANSITIONS: Partial<Record<RiskStatus, readonly RiskTransition[]>> = {
  OPEN: ['confirm', 'dismiss'],
  CONFIRMED: ['resolve'],
};

/** The right-hand drawer of the risk event list (SCR-RSK-01-D1): the full record, its lifecycle and the reversal. */
@Component({
  selector: 'app-risk-event-drawer',
  imports: [FormsModule, AppDatePipe],
  templateUrl: './risk-event-drawer.component.html',
  styleUrls: [
    './risk-events-badges.scss',
    './risk-event-drawer.component.scss',
    './risk-event-dialog.scss',
  ],
})
export class RiskEventDrawerComponent extends LocalizedComponent {
  private readonly api = inject(RiskEventService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  readonly eventId = input.required<string>();
  readonly closed = output<void>();
  /** Emits after the event was confirmed, dismissed, resolved or reversed, so the list can refresh. */
  readonly changed = output<RiskEventChange>();

  protected readonly details = signal<RiskEventDetails | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly notFound = signal(false);
  protected readonly previewFailed = signal(false);

  protected readonly transition = signal<RiskTransition | null>(null);
  protected readonly reversing = signal(false);
  protected readonly reason = signal('');
  protected readonly showValidation = signal(false);
  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  protected readonly reasonValid = computed(
    () => this.reason().trim().length >= MIN_REVERSAL_REASON,
  );
  protected readonly isReversed = computed(() => this.details()?.item.state === 'REVERSED');

  protected readonly transitions = computed(() => {
    if (this.details()?.isOwnEvent) return [];
    const status = this.details()?.item.status;
    const permissions = this.auth.currentUser()?.permissions;
    return (status ? (TRANSITIONS[status] ?? []) : []).filter((transition) =>
      canTransitionRiskEvents(permissions, transition),
    );
  });

  /** OPEN → CONFIRMED → RESOLVED, or OPEN → DISMISSED; a reversed event shows how far it got before the reversal. */
  protected readonly statusSteps = computed(() => {
    const details = this.details();
    if (!details) return [];
    const status = details.item.status;
    const path: RiskStatus[] =
      status === 'DISMISSED' && !details.reversedAt
        ? ['OPEN', 'DISMISSED']
        : ['OPEN', 'CONFIRMED', 'RESOLVED'];
    const reached = path.indexOf(details.reversedAt ? 'CONFIRMED' : status);
    return path.map((step, index) => ({
      status: step,
      reached: index <= reached,
      current: step === status,
    }));
  });

  constructor() {
    super();
    merge(toObservable(this.eventId), this.reload.pipe(map(() => this.eventId())))
      .pipe(
        switchMap((id) => {
          this.details.set(null);
          this.failed.set(false);
          this.notFound.set(false);
          this.previewFailed.set(false);
          this.transition.set(null);
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

  protected openTransition(transition: RiskTransition): void {
    this.errorKey.set(null);
    this.transition.set(transition);
  }

  protected cancelTransition(): void {
    if (!this.submitting()) this.transition.set(null);
  }

  /** Runs the chosen move, then reloads the record: confirm, dismiss and resolve do not return the drawer data. */
  protected confirmTransition(): void {
    const current = this.details();
    const transition = this.transition();
    if (!current || !transition || this.submitting()) return;
    this.errorKey.set(null);
    this.submitting.set(true);
    const request: Observable<unknown> = this.api[transition](current.item.id);
    request
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.transition.set(null);
          this.changed.emit({ code: current.item.code, action: transition });
          this.reload.next();
        },
        error: (error: unknown) => this.errorKey.set(riskErrorKey(error, 'transition')),
      });
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
          this.changed.emit({ code: updated.item.code, action: 'reverse' });
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

  protected transitionTitle(transition: RiskTransition): string {
    return this.i18n.translate(`riskEvents.transition.${transition}.title`, {
      code: this.details()?.item.code ?? '',
    });
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
    if (this.transition()) this.cancelTransition();
    else if (this.reversing()) this.cancelReverse();
    else this.close();
  }
}
