import { Component, computed, input, output } from '@angular/core';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { AppNumberPipe } from '../../pipes';
import { SuggestionBasis, SuggestionRemainderMethod } from '../target-shared.models';

/** "Suggested N cells in M KPIs · basis X · rounding Y" banner with Change basis / Undo / Apply actions. */
@Component({
  selector: 'app-suggestion-preview-banner',
  standalone: true,
  imports: [AppNumberPipe],
  styleUrls: ['../../ui/ui-controls.scss', './suggestion-preview-banner.component.scss'],
  templateUrl: './suggestion-preview-banner.component.html',
})
export class SuggestionPreviewBannerComponent extends LocalizedComponent {
  readonly cellCount = input.required<number>();
  readonly kpiCount = input.required<number>();
  readonly basis = input.required<SuggestionBasis>();
  readonly remainderMethod = input<SuggestionRemainderMethod>('LARGEST_REMAINDER');
  /** KPIs suggested without last year's data, and the tier whose fallback rule produced them. */
  readonly fallbackCount = input(0);
  readonly fallbackTier = input<1 | 2 | 3>(1);

  readonly changeBasis = output<void>();
  readonly undo = output<void>();
  readonly apply = output<void>();

  protected readonly text = computed(() => this.localization.targetShared.previewBanner);

  protected readonly fallbackNote = computed(() => {
    if (this.fallbackCount() <= 0) {
      return null;
    }
    const text = this.text();
    const rule = { 1: text.fallbackTier1, 2: text.fallbackTier2, 3: text.fallbackTier3 }[
      this.fallbackTier()
    ];
    return rule.replace('{count}', `${this.fallbackCount()}`);
  });

  protected readonly basisLabel = computed(() => {
    const popoverText = this.localization.targetShared.suggestPopover;
    return this.basis() === 'ACTUAL_LAST_YEAR'
      ? popoverText.basisActualLastYear
      : popoverText.basisTargetLastYear;
  });

  protected readonly roundingLabel = computed(() => {
    const popoverText = this.localization.targetShared.suggestPopover;
    return this.remainderMethod() === 'BUFFER_UNIT'
      ? popoverText.remainderBuffer
      : popoverText.remainderLargest;
  });
}
