import { inject, Pipe, PipeTransform } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';

@Pipe({
  name: 'appNumber',
  standalone: true,
  pure: false,
})
export class AppNumberPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number | string | null | undefined, maxDecimals?: number): string {
    return this.i18n.formatNumber(value, maxDecimals);
  }
}
