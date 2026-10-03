import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Translation, TranslocoLoader } from '@jsverse/transloco';

/**
 * Loads `/i18n/<lang>.json`. A scope loads `/i18n/<scope>/<lang>.json` through the same loader,
 * because Transloco asks for the language as "<scope>/<lang>" (D26).
 */
@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(language: string) {
    return this.http.get<Translation>(`/i18n/${language}.json`);
  }
}
