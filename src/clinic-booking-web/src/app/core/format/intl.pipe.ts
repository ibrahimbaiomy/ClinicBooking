import { inject, Pipe, PipeTransform } from '@angular/core';
import { LanguageService } from '../i18n/language.service';
import { formatIntl, IntlKind, IntlOptions } from './intl';

/**
 * The only way to display dates and numbers (D27): `{{ value | intl: 'date' }}`. It follows the
 * active language, uses Latin digits and the Gregorian calendar, and shows instants in Cairo time.
 * Never Angular's DatePipe.
 */
@Pipe({
  name: 'intl',
  // Impure on purpose: the output depends on the active language, which is not a pipe argument.
  pure: false,
})
export class IntlPipe implements PipeTransform {
  private readonly languages = inject(LanguageService);

  transform(value: unknown, kind: IntlKind, options?: IntlOptions): string {
    return formatIntl(value, kind, this.languages.language(), options);
  }
}
