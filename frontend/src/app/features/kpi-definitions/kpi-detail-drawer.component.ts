import { A11yModule } from '@angular/cdk/a11y';
import { Component, HostListener, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, distinctUntilChanged, forkJoin, map, of, switchMap, tap } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinition, KpiHistoryEntry, KpiUsage } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { businessClock } from '../work-board/work-board.utils';
import { KpiDecision } from './kpi-decision-modal.component';
import {
  AGGREGATION_KEYS,
  DIRECTION_KEYS,
  actionsFor,
  creditSource,
  isExpiringSoon,
  periodShort,
} from './kpi-definitions.utils';

/** What the viewer may do: the System Admin edits and sends, the Branch Manager decides, anyone else only reads. */
export type KpiViewerMode = 'admin' | 'approver' | 'viewer';

/** SCR-STRAT-02-D1: the details of one KPI, where it is used and how it changed. */
@Component({
  selector: 'app-kpi-detail-drawer',
  standalone: true,
  imports: [A11yModule],
  templateUrl: './kpi-detail-drawer.component.html',
  styleUrls: ['../../shared/ui/ui-controls.scss', './kpi-detail-drawer.component.scss'],
})
export class KpiDetailDrawerComponent extends LocalizedComponent {
  private readonly api = inject(KpiDefinitionService);

  readonly kpi = input.required<KpiDefinition>();
  readonly mode = input<KpiViewerMode>('viewer');
  readonly closed = output<void>();
  readonly decide = output<KpiDecision>();
  readonly edit = output<void>();
  readonly submitForApproval = output<void>();

  protected readonly directionKey = DIRECTION_KEYS;
  protected readonly aggregationKey = AGGREGATION_KEYS;

  protected readonly usage = signal<KpiUsage | null>(null);
  protected readonly history = signal<KpiHistoryEntry[]>([]);
  protected readonly loading = signal(true);

  protected readonly actions = computed(() => actionsFor(this.kpi().status));
  protected readonly expiring = computed(() =>
    isExpiringSoon(this.kpi(), businessClock(new Date()).date),
  );

  /** Who sent the KPI for approval, read from the last "send" step of the history. */
  protected readonly sender = computed(() => {
    const sent = this.history().filter((entry) => entry.action === 'SUBMIT');
    return sent[sent.length - 1]?.actorName ?? null;
  });

  constructor() {
    super();
    toObservable(this.kpi)
      .pipe(
        map((kpi) => kpi.id),
        distinctUntilChanged(),
        tap(() => {
          this.loading.set(true);
          this.usage.set(null);
          this.history.set([]);
        }),
        switchMap((id) =>
          forkJoin({
            usage: this.api.usage(id).pipe(catchError(() => of(null))),
            history: this.api.history(id).pipe(catchError(() => of([] as KpiHistoryEntry[]))),
          }),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ usage, history }) => {
        this.usage.set(usage);
        this.history.set(history);
        this.loading.set(false);
      });
  }

  @HostListener('document:keydown.escape')
  protected close(): void {
    this.closed.emit();
  }

  protected period(period: KpiDefinition['effectiveFrom']): string {
    return period ? periodShort(period) : '';
  }

  protected effectiveText(): string {
    const { effectiveFrom, effectiveTo } = this.kpi();
    if (!effectiveFrom) return this.localization.kpiCatalog.usageNone;
    return `${periodShort(effectiveFrom)} → ${effectiveTo ? periodShort(effectiveTo) : this.localization.kpiCatalog.noEnd}`;
  }

  protected statusLabel(status: KpiDefinition['status']): string {
    const text = this.localization.kpiCatalog;
    const labels: Record<KpiDefinition['status'], string> = {
      DRAFT: text.statusDraft,
      PENDING_APPROVAL: text.statusPending,
      REJECTED: text.statusRejected,
      ACTIVE: text.statusActive,
      INACTIVE: text.statusInactive,
      ARCHIVED: text.statusArchived,
    };
    return labels[status];
  }

  protected recordFrom(item: Pick<KpiDefinition, 'creditRole' | 'dataSource'>): string {
    const text = this.localization.kpiCatalog;
    switch (creditSource(item)) {
      case 'REFER_ID':
        return text.recordFromRefer;
      case 'RATING_EMPLOYEE':
        return text.recordFromRating;
      default:
        return text.recordFromTransaction;
    }
  }

  protected dateTime(value: string | null): string {
    return this.i18n.formatDate(value, 'HH:mm:ss DD/MM/YYYY');
  }

  protected submittedText(): string {
    const time = this.dateTime(this.kpi().submittedAt);
    const actor = this.sender();
    return actor
      ? this.i18n.translate('kpiCatalog.drawerSubmittedBy', { actor, time })
      : this.i18n.translate('kpiCatalog.drawerSubmittedAt', { time });
  }
}
