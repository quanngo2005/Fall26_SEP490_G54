import { inject, Pipe, PipeTransform } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';

@Pipe({
  name: 'vndCurrency',
  standalone: true,
  pure: false,
})
export class VndCurrencyPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number | string | null | undefined, suffix: string = 'VND'): string {
    return this.i18n.formatCurrency(value, suffix);
  }
}
