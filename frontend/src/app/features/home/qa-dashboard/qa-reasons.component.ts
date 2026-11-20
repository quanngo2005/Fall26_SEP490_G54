import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { QaDashboardReasonCount } from '../../../core/qa-dashboard.models';
import { fill, IMPORT_REASON } from './qa-dashboard-display';

/** "Vì sao mã giao dịch chưa được tính": reasons ranked by count as horizontal bars, each opening the reconciliations. */
@Component({
  selector: 'app-qa-reasons',
  imports: [RouterLink],
  template: `
    <section class="qa-panel" [attr.aria-label]="text().title">
  <header class="qa-panel-head">
    <h2>{{text().title}}</h2>
    @if (total() > 0) {
      <span class="qa-panel-meta">{{summary()}}</span>
    }
  </header>
  @if (rows().length > 0) {
    <ul class="reasons">
      @for (row of rows(); track row.code) {
        <li>
          <a
            class="reason"
            routerLink="/reconciliations"
            [queryParams]="row.params"
            [attr.aria-label]="row.aria"
          >
            <span class="label" [title]="row.label">{{row.label}}</span>
            <span class="track"
              ><span class="bar" [class.import]="row.isImport" [style.width.%]="row.width"></span
            ></span>
            <span class="count">{{row.count}}</span>
          </a>
        </li>
      }
    </ul>
    <footer class="qa-panel-foot">
      <span class="legend">
        <span><i class="dot import"></i>{{legendImport()}}</span>
        <span><i class="dot"></i>{{legendMismatch()}}</span>
      </span>
      <a class="qa-link" routerLink="/reconciliations" [queryParams]="linkParams()">
        {{text().viewReconciliation}}
      </a>
    </footer>
  } @else {
    <p class="qa-empty">{{text().empty}}</p>
  }
</section>
  `,
  styleUrl: './qa-panel.scss',
  styles: `
    .reasons {
      list-style: none;
      margin: 0;
      padding: 4px 0 8px;
    }
    .reason {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(64px, 1fr) 28px;
      align-items: center;
      gap: 12px;
      min-height: 36px;
      padding: 0 16px;
      color: #334155;
      font-size: 13px;
      text-decoration: none;
      &:hover {
        background: #f8fafc;
      }
      &:focus-visible {
        outline: 2px solid #2563eb;
        outline-offset: -2px;
      }
    }
    .label {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .track {
      height: 10px;
      border-radius: 5px;
      background: #f1f5f9;
    }
    .bar {
      display: block;
      height: 100%;
      min-width: 10px;
      border-radius: 5px;
      background: #f87171;
      &.import {
        background: #fb923c;
      }
    }
    .count {
      color: #0f172a;
      font-weight: 600;
      text-align: right;
      font-variant-numeric: tabular-nums;
    }
    .reasons {
      container-type: inline-size;
    }
    @container (max-width: 420px) {
      .reason {
        grid-template-columns: minmax(0, 1fr) 28px;
        gap: 4px 12px;
        padding: 6px 16px;
      }
      .label {
        white-space: normal;
      }
      .track {
        grid-column: 1 / -1;
        grid-row: 2;
      }
    }
    .legend {
      display: flex;
      flex-wrap: wrap;
      gap: 4px 16px;
      color: #475569;
    }
    .dot {
      display: inline-block;
      width: 10px;
      height: 10px;
      margin-right: 6px;
      border-radius: 2px;
      background: #f87171;
      vertical-align: -1px;
      &.import {
        background: #fb923c;
      }
    }
  `,
})
export class QaReasonsComponent extends LocalizedComponent {
  readonly reasons = input.required<QaDashboardReasonCount[]>();
  readonly periodName = input.required<string>();
  /** Dashboard filters, carried over to the reconciliation screen. */
  readonly linkParams = input<Record<string, string>>({});

  protected readonly text = computed(() => this.localization.qaDashboard.reasons);

  protected readonly rows = computed(() => {
    const reasons = this.reasons()
      .filter((r) => r.count > 0)
      .sort((a, b) => b.count - a.count);
    const max = Math.max(1, ...reasons.map((r) => r.count));
    return reasons.map((r) => {
      const label = this.label(r);
      return {
        code: r.reasonCode,
        label,
        count: r.count,
        isImport: r.reasonCode === IMPORT_REASON,
        width: (r.count / max) * 100,
        params: { ...this.linkParams(), reason: r.reasonCode },
        aria: fill(this.text().rowAria, { label, count: r.count }),
      };
    });
  });

  protected readonly total = computed(() => this.rows().reduce((sum, r) => sum + r.count, 0));
  private readonly importTotal = computed(() =>
    this.rows().reduce((sum, r) => sum + (r.isImport ? r.count : 0), 0),
  );

  protected readonly summary = computed(() =>
    fill(this.text().summary, { count: this.total(), period: this.periodName() }),
  );
  protected readonly legendImport = computed(() =>
    fill(this.text().legendImport, { count: this.importTotal() }),
  );
  protected readonly legendMismatch = computed(() =>
    fill(this.text().legendMismatch, { count: this.total() - this.importTotal() }),
  );

  /** Dashboard wording first, then the reconciliation screen's, then what the API sent. */
  private label(reason: QaDashboardReasonCount): string {
    const own = this.text().labels as Record<string, string>;
    const shared = this.localization.reconciliation.reasons as Record<string, string>;
    return (
      own[reason.reasonCode] ?? shared[reason.reasonCode] ?? (reason.reason || reason.reasonCode)
    );
  }
}
