import { computed, Injectable, signal } from '@angular/core';
import localizationData from './localization.json';
import { LocalizationSchema, LocaleFormatConfig, SupportedLocale } from './i18n.models';

const STORAGE_KEY = 'bpms_language';

@Injectable({
  providedIn: 'root',
})
export class I18nService {
  private readonly schema: LocalizationSchema = {
    ...localizationData,
    defaultLocale: 'vi',
    supportedLocales: ['vi', 'en'],
  };

  get localization() {
    return this.schema.translations[this.currentLanguage()];
  }

  readonly currentLanguage = signal<SupportedLocale>(this.getInitialLanguage());

  readonly currentFormatConfig = computed<LocaleFormatConfig>(() => {
    const lang = this.currentLanguage();
    return this.schema.formats[lang] ?? this.schema.formats.vi;
  });

  private getInitialLanguage(): SupportedLocale {
    if (typeof window !== 'undefined' && typeof localStorage !== 'undefined') {
      try {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === 'vi' || stored === 'en') {
          return stored;
        }
      } catch {
        // Fallback to default if localStorage is inaccessible
      }
    }
    return this.schema.defaultLocale || 'vi';
  }

  setLanguage(lang: SupportedLocale): void {
    if (lang !== 'vi' && lang !== 'en') {
      return;
    }
    this.currentLanguage.set(lang);
    if (typeof window !== 'undefined' && typeof localStorage !== 'undefined') {
      try {
        localStorage.setItem(STORAGE_KEY, lang);
      } catch {
        // Ignore localStorage write failures
      }
    }
  }

  toggleLanguage(): SupportedLocale {
    const nextLang: SupportedLocale = this.currentLanguage() === 'vi' ? 'en' : 'vi';
    this.setLanguage(nextLang);
    return nextLang;
  }

  translate(key: string, params?: Record<string, string | number>): string {
    if (!key) {
      return '';
    }

    const currentLang = this.currentLanguage();
    const resolved =
      this.resolveTranslationKey(currentLang, key) ?? this.resolveTranslationKey('vi', key) ?? key;

    if (!params) {
      return resolved;
    }

    return Object.entries(params).reduce((acc, [paramKey, paramValue]) => {
      return acc.replace(new RegExp(`\\{${paramKey}\\}`, 'g'), String(paramValue));
    }, resolved);
  }

  private resolveTranslationKey(lang: SupportedLocale, key: string): string | null {
    const langDict = this.schema.translations[lang];
    if (!langDict) {
      return null;
    }

    const segments = key.split('.');
    let current: unknown = langDict;

    for (const segment of segments) {
      if (current && typeof current === 'object' && segment in current) {
        current = (current as Record<string, unknown>)[segment];
      } else {
        return null;
      }
    }

    return typeof current === 'string' ? current : null;
  }

  formatDate(
    value: Date | string | number | null | undefined,
    pattern: string = 'DD/MM/YYYY',
  ): string {
    if (value === null || value === undefined || value === '') {
      return '-';
    }

    const date = this.parseDate(value);
    if (!date || isNaN(date.getTime())) {
      return typeof value === 'string' ? value : '-';
    }

    const day = String(date.getDate()).padStart(2, '0');
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const year = String(date.getFullYear());
    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');
    const seconds = String(date.getSeconds()).padStart(2, '0');

    return pattern
      .replace(/YYYY/g, year)
      .replace(/MM/g, month)
      .replace(/DD/g, day)
      .replace(/HH/g, hours)
      .replace(/mm/g, minutes)
      .replace(/ss/g, seconds);
  }

  private parseDate(value: Date | string | number): Date | null {
    if (value instanceof Date) {
      return value;
    }
    if (typeof value === 'number') {
      return new Date(value);
    }
    if (typeof value === 'string') {
      // Support dd/mm/yyyy input format conversion if needed
      const ddmmyyyyMatch = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(value.trim());
      if (ddmmyyyyMatch) {
        const [, d, m, y] = ddmmyyyyMatch;
        return new Date(Number(y), Number(m) - 1, Number(d));
      }
      return new Date(value);
    }
    return null;
  }

  formatCurrency(value: number | string | null | undefined, suffix: string = 'VND'): string {
    if (value === null || value === undefined || value === '') {
      return `0 ${suffix}`.trim();
    }

    const numericValue =
      typeof value === 'number' ? value : Number(String(value).replace(/,/g, ''));
    if (isNaN(numericValue)) {
      return `${value} ${suffix}`.trim();
    }

    const isNegative = numericValue < 0;
    const absValue = Math.abs(numericValue);

    const parts = absValue.toString().split('.');
    const integerPart = parts[0];
    const decimalPart = parts[1];

    // Format thousands with dot separator
    const formattedInteger = integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, '.');

    let formattedNumber = formattedInteger;
    if (decimalPart) {
      formattedNumber += `,${decimalPart}`;
    }

    if (isNegative) {
      formattedNumber = `-${formattedNumber}`;
    }

    if (!suffix) {
      return formattedNumber;
    }

    return `${formattedNumber} ${suffix}`;
  }

  formatNumber(value: number | string | null | undefined, maxDecimals?: number): string {
    if (value === null || value === undefined || value === '') {
      return '0';
    }

    const numericValue =
      typeof value === 'number' ? value : Number(String(value).replace(/,/g, ''));
    if (isNaN(numericValue)) {
      return String(value);
    }

    const isNegative = numericValue < 0;
    const absValue = Math.abs(numericValue);

    let [integerPart, decimalPart] = absValue.toString().split('.');

    if (maxDecimals !== undefined) {
      const fixed = absValue.toFixed(maxDecimals);
      [integerPart, decimalPart] = fixed.split('.');
    }

    const formattedInteger = integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    let formattedNumber = formattedInteger;
    if (decimalPart && (!maxDecimals || maxDecimals > 0)) {
      formattedNumber += `,${decimalPart}`;
    }

    return isNegative ? `-${formattedNumber}` : formattedNumber;
  }
}
