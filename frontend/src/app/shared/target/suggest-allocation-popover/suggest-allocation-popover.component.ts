import { Component, computed, input, output, signal } from '@angular/core';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { AppNumberPipe } from '../../pipes';
import {
  DEFAULT_COUNT_STEP,
  DEFAULT_MONEY_STEP,
  SuggestAllocationRequest,
  SuggestionApplyMode,
  SuggestionBasis,
  SuggestionBufferUnitOption,
  SuggestionEmployeeScope,
  SuggestionPeriodScope,
  SuggestionPreview,
  SuggestionRemainderMethod,
  SuggestionRoundingMode,
  SuggestionScope,
} from '../target-shared.models';

/**
 * 420px popover for the suggest-allocation flow shared by the KPI, department and employee allocation
 * screens: basis -> scope (-> tier-3 employees/period) -> apply-to -> rounding -> preview. Pure
 * presentational; the host supplies `hasHistory`/`preview` and reacts to `created`/`cancelled`.
 */
@Component({
  selector: 'app-suggest-allocation-popover',
  standalone: true,
  imports: [AppNumberPipe],
  styleUrls: ['../../ui/ui-controls.scss', './suggest-allocation-popover.component.scss'],
  templateUrl: './suggest-allocation-popover.component.html',
})
export class SuggestAllocationPopoverComponent extends LocalizedComponent {
  /** D5: a unit with no previous-year history at all disables the whole form. */
  readonly hasHistory = input(true);
  readonly noHistoryReason = input<string | null>(null);
  /** Shows the tier-3-only employee scope and period blocks. */
  readonly isTier3 = input(false);
  readonly bufferUnitOptions = input<readonly SuggestionBufferUnitOption[]>([]);
  readonly preview = input<SuggestionPreview | null>(null);

  readonly cancelled = output<void>();
  readonly created = output<SuggestAllocationRequest>();

  protected readonly text = computed(() => this.localization.targetShared.suggestPopover);

  protected readonly basis = signal<SuggestionBasis>('TARGET_LAST_YEAR');
  protected readonly scope = signal<SuggestionScope>('CURRENT_KPI');
  protected readonly employeeScope = signal<SuggestionEmployeeScope>('ALL');
  protected readonly periodScope = signal<SuggestionPeriodScope>('YEAR_AND_QUARTERS');
  protected readonly applyMode = signal<SuggestionApplyMode>('EMPTY_ONLY');
  protected readonly moneyStep = signal(DEFAULT_MONEY_STEP);
  protected readonly countStep = signal(DEFAULT_COUNT_STEP);
  protected readonly roundingMode = signal<SuggestionRoundingMode>('NEAREST');
  protected readonly remainderMethod = signal<SuggestionRemainderMethod>('LARGEST_REMAINDER');
  protected readonly bufferUnitId = signal<string | null>(null);

  protected readonly isActualBasis = computed(() => this.basis() === 'ACTUAL_LAST_YEAR');

  protected readonly request = computed<SuggestAllocationRequest>(() => ({
    basis: this.basis(),
    scope: this.scope(),
    employeeScope: this.isTier3() ? this.employeeScope() : null,
    periodScope: this.isTier3() ? this.periodScope() : null,
    applyMode: this.applyMode(),
    rounding: {
      moneyStep: this.moneyStep(),
      countStep: this.countStep(),
      mode: this.roundingMode(),
      remainderMethod: this.isActualBasis() ? this.remainderMethod() : 'LARGEST_REMAINDER',
      bufferUnitId: this.remainderMethod() === 'BUFFER_UNIT' ? this.bufferUnitId() : null,
    },
  }));

  protected setBasis(value: SuggestionBasis): void {
    this.basis.set(value);
    if (value === 'TARGET_LAST_YEAR') {
      this.remainderMethod.set('LARGEST_REMAINDER');
    }
  }

  protected setScope(value: SuggestionScope): void {
    this.scope.set(value);
  }

  protected setEmployeeScope(value: SuggestionEmployeeScope): void {
    this.employeeScope.set(value);
  }

  protected setPeriodScope(value: SuggestionPeriodScope): void {
    this.periodScope.set(value);
  }

  protected setApplyMode(value: SuggestionApplyMode): void {
    this.applyMode.set(value);
  }

  protected setRoundingMode(value: SuggestionRoundingMode): void {
    this.roundingMode.set(value);
  }

  protected setRemainderMethod(value: SuggestionRemainderMethod): void {
    this.remainderMethod.set(value);
  }

  protected onMoneyStepInput(value: string): void {
    const parsed = Number(value);
    this.moneyStep.set(Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_MONEY_STEP);
  }

  protected onCountStepInput(value: string): void {
    const parsed = Number(value);
    this.countStep.set(Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_COUNT_STEP);
  }

  protected onBufferUnitChange(value: string): void {
    this.bufferUnitId.set(value || null);
  }

  protected cancel(): void {
    this.cancelled.emit();
  }

  protected create(): void {
    if (!this.hasHistory()) {
      return;
    }
    this.created.emit(this.request());
  }
}
