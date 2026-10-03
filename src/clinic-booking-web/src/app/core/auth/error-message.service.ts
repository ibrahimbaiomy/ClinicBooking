import { inject, Injectable } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { ApiError, UNEXPECTED_ERROR_KEY } from './api-error';

/**
 * Turns an ApiError into a translation key that is certain to exist, so a user never sees a raw
 * key or server text (D26). Templates then show it with the transloco pipe, which follows the
 * language switcher.
 */
@Injectable({ providedIn: 'root' })
export class ErrorMessageService {
  private readonly transloco = inject(TranslocoService);

  /** The key itself when the active language has it, otherwise error.unexpected. */
  keyFor(key: string): string {
    // i18n-keys: error.unexpected
    return this.transloco.translate(key) === key ? UNEXPECTED_ERROR_KEY : key;
  }

  forError(error: ApiError): string {
    return this.keyFor(error.key);
  }
}
