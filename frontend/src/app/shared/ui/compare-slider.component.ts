import { Component, input, output } from '@angular/core';

export type ComparePeriodMode = 'PREVIOUS' | 'SAME_LAST_YEAR';

/**
 * Reusable segmented slider switch for period comparison across all evaluation and performance screens.
 * Options: "Kỳ trước" (PREVIOUS) and "Kỳ này năm trước" (SAME_LAST_YEAR).
 */
@Component({
  selector: 'app-compare-slider',
  standalone: true,
  template: `
    <div
  class="compare-slider-root"
  role="group"
  [attr.aria-label]="ariaLabel()"
  [class.is-disabled]="disabled()"
>
  <button
    type="button"
    class="compare-slider-btn"
    [class.active]="mode() === 'PREVIOUS'"
    [disabled]="disabled() || !hasPrevious()"
    (click)="selectMode('PREVIOUS')"
    [title]="!hasPrevious() ? 'Không có dữ liệu kỳ trước' : previousTooltip() || previousLabel()"
  >
    <span class="btn-text">{{previousLabel()}}</span>
  </button>
  <button
    type="button"
    class="compare-slider-btn"
    [class.active]="mode() === 'SAME_LAST_YEAR'"
    [disabled]="disabled() || !hasSameLastYear()"
    (click)="selectMode('SAME_LAST_YEAR')"
    [title]="
      !hasSameLastYear()
        ? 'Không có dữ liệu cùng kỳ năm trước'
        : sameLastYearTooltip() || sameLastYearLabel()
    "
  >
    <span class="btn-text">{{sameLastYearLabel()}}</span>
  </button>
</div>
  `,
  styles: [
    `
      :host {
        display: inline-block;
        vertical-align: middle;
      }

      .compare-slider-root {
        display: inline-flex;
        align-items: center;
        background: #f1f5f9;
        padding: 3px;
        border-radius: 8px;
        border: 1px solid #cbd5e1;
        user-select: none;
        box-sizing: border-box;

        &.is-disabled {
          opacity: 0.6;
          pointer-events: none;
        }
      }

      .compare-slider-btn {
        padding: 6px 14px;
        border: none;
        background: transparent;
        border-radius: 6px;
        font-size: 13px;
        font-weight: 600;
        color: #64748b;
        cursor: pointer;
        white-space: nowrap;
        transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
        display: inline-flex;
        align-items: center;
        justify-content: center;
        line-height: 1.25;

        &:hover:not(:disabled) {
          color: #0f172a;
        }

        &.active {
          background: #ffffff;
          color: #002b49;
          box-shadow:
            0 1px 3px rgba(15, 23, 42, 0.12),
            0 1px 2px rgba(15, 23, 42, 0.08);
          font-weight: 700;
        }

        &:disabled:not(.active) {
          opacity: 0.45;
          cursor: not-allowed;
        }
      }
    `,
  ],
})
export class CompareSliderComponent {
  readonly mode = input<ComparePeriodMode>('PREVIOUS');
  readonly disabled = input<boolean>(false);
  readonly hasPrevious = input<boolean>(true);
  readonly hasSameLastYear = input<boolean>(true);
  readonly previousLabel = input<string>('Kỳ trước');
  readonly sameLastYearLabel = input<string>('Kỳ này năm trước');
  readonly previousTooltip = input<string>('');
  readonly sameLastYearTooltip = input<string>('');
  readonly ariaLabel = input<string>('So sánh với kỳ');

  readonly modeChange = output<ComparePeriodMode>();

  selectMode(newMode: ComparePeriodMode): void {
    if (this.disabled()) return;
    if (newMode === 'SAME_LAST_YEAR' && !this.hasSameLastYear()) return;
    if (newMode === 'PREVIOUS' && !this.hasPrevious()) return;
    if (this.mode() !== newMode) {
      this.modeChange.emit(newMode);
    }
  }
}
