export type SupportedLocale = 'vi' | 'en';

export interface LocaleFormatConfig {
  date: string;
  dateTime: string;
  currency: string;
  currencySymbol: string;
  thousandSeparator: string;
  decimalSeparator: string;
}

import type localizationData from './localization.json';

export type Localization = typeof localizationData.translations.vi;

export interface LocalizationSchema {
  defaultLocale: SupportedLocale;
  supportedLocales: SupportedLocale[];
  formats: Record<SupportedLocale, LocaleFormatConfig>;
  translations: Record<SupportedLocale, Localization>;
}
