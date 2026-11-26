import { Component, computed, input, output, signal } from '@angular/core';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { AppNumberPipe } from '../../pipes';
import { TargetMoneyPipe } from '../pipes/target-money.pipe';
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
  imports: [AppNumberPipe, TargetMoneyPipe],
  templateUrl: './allocation-cell.component.html',
  styleUrl: './allocation-cell.component.scss',
})
export class AllocationCellComponent extends LocalizedComponent {
  readonly value = input<number | null>(null);
  readonly unit = input('');
  readonly state = input<AllocationCellState>('DEFAULT');
  readonly editable = input(false);
  /** While typing, show the amount as triệu / tỷ. Set by the host for VND KPIs only. */
  readonly scaleHint = input(false);
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

  /** Text currently typed in the input; `null` while the input is not focused. */
  private readonly draft = signal<string | null>(null);

  /** Typed VND amount as triệu / tỷ (`1.500.000.000` → `1,5 tỷ`); `null` when there is nothing to show. */
  protected readonly typedAmount = computed(() => {
    const draft = this.draft();
    if (draft === null || !this.scaleHint()) {
      return null;
    }
    const parsed = this.parse(draft);
    return Number.isFinite(parsed) && Math.abs(parsed) >= 1_000_000 ? parsed : null;
  });

  protected readonly unitLabels = computed(() => ({
    million: this.text().unitMillion,
    billion: this.text().unitBillion,
  }));

  protected onInput(input: HTMLInputElement): void {
    this.draft.set(input.value);
  }

  protected onFocus(input: HTMLInputElement): void {
    this.draft.set(input.value);
  }

  protected onBlur(input: HTMLInputElement): void {
    this.draft.set(null);
    if (!this.editable()) {
      return;
    }
    const parsed = this.parse(input.value);
    if (Number.isFinite(parsed)) {
      this.valueChange.emit(parsed);
    }
    input.value = this.display();
  }

  /** vi format: `.` groups thousands, `,` is the decimal separator. */
  private parse(text: string): number {
    return Number(text.replace(/\./g, '').replace(',', '.'));
  }

  protected onKeydown(event: KeyboardEvent, input: HTMLInputElement): void {
    if (event.key === 'Enter') {
      input.blur();
    }
  }
}
