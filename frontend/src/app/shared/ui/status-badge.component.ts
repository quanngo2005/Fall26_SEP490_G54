import { Component, input } from '@angular/core';

export type StatusBadgeTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

/**
 * Canonical status badge. Sentence case (no all-caps), a leading dot, and a shape shared with
 * `.ui-chip`. The "success" tone is deliberately quiet (dot + plain text, no fill) so a column full
 * of healthy rows stays calm and only abnormal states (warning/danger) stand out.
 */
@Component({
  selector: 'app-status-badge',
  template: `<span class="badge" [class]="'badge tone-' + tone()"><ng-content /></span>`,
  styles: [
    `
      :host {
        display: inline-flex;
        vertical-align: middle;
      }

      .badge {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        min-height: 24px;
        box-sizing: border-box;
        border: 1px solid #e2e8f0;
        border-radius: 6px;
        background: #f1f5f9;
        color: #334155;
        font:
          600 13px/18px 'Nunito Sans',
          sans-serif;
        padding: 0 9px;
        white-space: nowrap;
      }

      .badge::before {
        content: '';
        width: 6px;
        height: 6px;
        flex: none;
        border-radius: 50%;
        background: currentcolor;
      }

      .badge.tone-success {
        border-color: transparent;
        background: transparent;
        color: #047857;
        font-weight: 500;
        padding: 0;
      }

      .badge.tone-warning {
        border-color: #fde68a;
        background: #fffbeb;
        color: #b45309;
      }

      .badge.tone-danger {
        border-color: #fecdd3;
        background: #fff1f2;
        color: #be123c;
      }

      .badge.tone-info {
        border-color: #bfdbfe;
        background: #eff6ff;
        color: #1d4ed8;
      }
    `,
  ],
})
export class StatusBadgeComponent {
  readonly tone = input<StatusBadgeTone>('neutral');
}
