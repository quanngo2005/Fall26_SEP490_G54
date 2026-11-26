import { Component, computed, input } from '@angular/core';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { AppNumberPipe } from '../../pipes';
import { PaceBarKind, PaceStatus, paceStatusFor } from '../target-shared.models';

/**
 * D16/D17: progress bar with a black expected-pace tick, colored by the pace gap (on track / slow / at
 * risk). A balance KPI (D2) has no tick and shows "level x%" instead of a gap; a ratio KPI shows a simple
 * above/below-threshold check instead of a percentage bar.
 */
@Component({
  selector: 'app-pace-bar',
  standalone: true,
  imports: [AppNumberPipe],
  templateUrl: './pace-bar.component.html',
  styleUrl: './pace-bar.component.scss',
})
export class PaceBarComponent extends LocalizedComponent {
  readonly kind = input<PaceBarKind>('NORMAL');
  /** % of target achieved so far (0-100+). Ignored for `RATIO`. */
  readonly progressPercent = input(0);
  /** Where the black tick sits, as % of target expected by now. Ignored for `BALANCE`/`RATIO`. */
  readonly expectedPacePercent = input(100);
  /** `progressPercent - expectedPacePercent`; drives the on-track/slow/at-risk color (D16). */
  readonly gapPercent = input(0);
  /** Current balance level, shown instead of a gap for a `BALANCE` KPI. */
  readonly balanceLevelPercent = input(0);
  /** Whether the ratio KPI is above its threshold, for `RATIO`. */
  readonly ratioAboveThreshold = input(false);

  protected readonly text = computed(() => this.localization.targetShared.paceBar);

  protected readonly status = computed<PaceStatus>(() => paceStatusFor(this.gapPercent()));

  protected readonly statusLabel = computed(() => {
    const text = this.text();
    switch (this.status()) {
      case 'ON_TRACK':
        return text.onTrack;
      case 'SLOW':
        return text.slow;
      default:
        return text.atRisk;
    }
  });

  protected readonly fillPercent = computed(() =>
    Math.max(0, Math.min(100, this.progressPercent())),
  );

  protected readonly tickPercent = computed(() =>
    Math.max(0, Math.min(100, this.expectedPacePercent())),
  );
}
