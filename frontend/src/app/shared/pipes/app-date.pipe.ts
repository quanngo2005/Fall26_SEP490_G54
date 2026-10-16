import { inject, Pipe, PipeTransform } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';

@Pipe({
  name: 'appDate',
  standalone: true,
  pure: false,
})
export class AppDatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(
    value: Date | string | number | null | undefined,
    pattern: string = 'DD/MM/YYYY',
  ): string {
    return this.i18n.formatDate(value, pattern);
  }
}
