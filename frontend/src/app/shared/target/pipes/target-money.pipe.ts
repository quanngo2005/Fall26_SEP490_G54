import { inject, Pipe, PipeTransform } from '@angular/core';
import { I18nService } from '../../../core/i18n/i18n.service';

export interface TargetMoneyUnitLabels {
  readonly million: string;
  readonly billion: string;
}

const DEFAULT_LABELS: Record<'vi' | 'en', TargetMoneyUnitLabels> = {
  vi: { million: 'tr', billion: 'tỷ' },
  en: { million: 'm', billion: 'bn' },
};

const MILLION = 1_000_000;
const BILLION = 1_000_000_000;

/**
 * D15: auto-scales a VND amount to triệu (`tr`) or tỷ (`tỷ`) by magnitude, so large targets stay readable.
 * Values under 1 triệu are shown as a plain grouped number (no unit). `unitLabels` lets a caller override the
 * suffixes (e.g. with localized `targetShared` strings once merged); it otherwise falls back to the current
 * language's default vi/en labels.
 */
@Pipe({
  name: 'targetMoney',
  standalone: true,
  pure: false,
})
export class TargetMoneyPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(
    value: number | null | undefined,
    unitLabels?: TargetMoneyUnitLabels,
    maxDecimals = 2,
  ): string {
    if (value === null || value === undefined || !Number.isFinite(value)) {
      return '0';
    }

    const lang = this.i18n.currentLanguage();
    const labels = unitLabels ?? DEFAULT_LABELS[lang] ?? DEFAULT_LABELS.vi;
    const absolute = Math.abs(value);
    const sign = value < 0 ? '-' : '';

    if (absolute >= BILLION) {
      return `${sign}${this.scaled(absolute / BILLION, maxDecimals)} ${labels.billion}`;
    }
    if (absolute >= MILLION) {
      return `${sign}${this.scaled(absolute / MILLION, maxDecimals)} ${labels.million}`;
    }
    return `${sign}${this.i18n.formatNumber(absolute, 0)}`;
  }

  /** Rounds to `maxDecimals` then drops trailing zero decimals, so `2.50` reads as `2,5`. */
  private scaled(value: number, maxDecimals: number): string {
    const fixed = value.toFixed(maxDecimals);
    const trimmed = fixed.includes('.') ? fixed.replace(/0+$/, '').replace(/\.$/, '') : fixed;
    const decimals = trimmed.includes('.') ? trimmed.split('.')[1].length : 0;
    return this.i18n.formatNumber(value, decimals);
  }
}
