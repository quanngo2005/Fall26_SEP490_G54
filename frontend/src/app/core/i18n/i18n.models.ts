export type SupportedLocale = 'vi' | 'en';

export interface LocaleFormatConfig {
  date: string;
  dateTime: string;
  currency: string;
  currencySymbol: string;
  thousandSeparator: string;
  decimalSeparator: string;
}

export interface LocalizationSchema {
  defaultLocale: SupportedLocale;
  supportedLocales: SupportedLocale[];
  formats: Record<SupportedLocale, LocaleFormatConfig>;
  translations: Record<SupportedLocale, Record<string, Record<string, string>>>;
}
