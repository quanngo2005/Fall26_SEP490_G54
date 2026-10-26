import { AppDatePipe } from './app-date.pipe';
import { AppNumberPipe } from './app-number.pipe';
import { TranslatePipe } from './translate.pipe';
import { VndCurrencyPipe } from './vnd-currency.pipe';

export * from './app-date.pipe';
export * from './app-number.pipe';
export * from './translate.pipe';
export * from './vnd-currency.pipe';

export const I18N_PIPES = [TranslatePipe, AppDatePipe, VndCurrencyPipe, AppNumberPipe] as const;
