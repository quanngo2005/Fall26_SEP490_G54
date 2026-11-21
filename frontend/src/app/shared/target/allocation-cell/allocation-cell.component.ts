import { Component, computed, input, output } from '@angular/core';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { AppNumberPipe } from '../../pipes';
import { AllocationCellState } from '../target-shared.models';

/**
 * One allocation value cell (year/quarter target, employee KPI value, …) with the 4 visual states shared
 * across the target allocation v3 screens: suggested, manual, mismatch, locked/not-applicable. Pure
 * presentational — the host decides the state and owns the edit; this component only renders and, when
 * `editable`, emits the committed number on blur/Enter.
 */
@Component({
  selector: 'app-allocation-cell',
  standalone: true,
  imports: [AppNumberPipe],
  templateUrl: './allocation-cell.component.html',
  styleUrl: './allocation-cell.component.scss',
})
export class AllocationCellComponent extends LocalizedComponent {
  readonly value = input<number | null>(null);
  readonly unit = input('');
  readonly state = input<AllocationCellState>('DEFAULT');
  readonly editable = input(false);
  /** Shown as a native tooltip when `state` is `LOCKED`. Falls back to a generic reason. */
  readonly lockedReason = input<string | null>(null);

  readonly valueChange = output<number>();

  protected readonly text = computed(() => this.localization.targetShared.allocationCell);

  protected readonly stateClass = computed(() => `cell is-${this.state().toLowerCase()}`);

  protected readonly tooltip = computed(() =>
    this.state() === 'LOCKED' ? this.lockedReason() || this.text().lockedDefaultReason : null,
  );

  /** Grouped with `.` like the read-only view, so the editable input reads the same (`12.985.855.962`). */
  protected readonly display = computed(() => {
    const value = this.value();
    return value === null ? '' : this.i18n.formatNumber(value);
  });

  protected onBlur(input: HTMLInputElement): void {
    if (!this.editable()) {
      return;
    }
    // vi format: `.` groups thousands, `,` is the decimal separator.
    const parsed = Number(input.value.replace(/\./g, '').replace(',', '.'));
    if (Number.isFinite(parsed)) {
      this.valueChange.emit(parsed);
    }
    input.value = this.display();
  }

  protected onKeydown(event: KeyboardEvent, input: HTMLInputElement): void {
    if (event.key === 'Enter') {
      input.blur();
    }
  }
}
