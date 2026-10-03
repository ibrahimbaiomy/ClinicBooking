import { DOCUMENT } from '@angular/common';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import {
  DEFAULT_LANGUAGE,
  directionOf,
  isLanguage,
  Language,
  LANGUAGE_STORAGE_KEY,
} from './language';

/**
 * The active language: a signal that is persisted in localStorage and mirrored to the document's
 * `lang` and `dir` attributes and to Transloco (D26).
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);
  private readonly current = signal<Language>(this.readStored());

  readonly language = this.current.asReadonly();
  readonly direction = computed(() => directionOf(this.current()));

  constructor() {
    // Applied at once so the first render already has the right lang and dir...
    this.apply(this.current());
    // ...and again whenever the signal changes.
    effect(() => this.apply(this.current()));
  }

  /** Loads the active language file; used before the first render so no raw keys flash. */
  load(): Promise<unknown> {
    return firstValueFrom(this.transloco.load(this.current()));
  }

  set(language: Language): void {
    if (!isLanguage(language)) {
      return;
    }

    this.current.set(language);
    this.store(language);
  }

  toggle(): void {
    this.set(this.current() === 'ar' ? 'en' : 'ar');
  }

  private apply(language: Language): void {
    const root = this.document.documentElement;
    root.lang = language;
    root.dir = directionOf(language);
    this.transloco.setActiveLang(language);
  }

  private readStored(): Language {
    try {
      const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY);
      return isLanguage(stored) ? stored : DEFAULT_LANGUAGE;
    } catch {
      return DEFAULT_LANGUAGE;
    }
  }

  private store(language: Language): void {
    try {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    } catch {
      // Storage may be unavailable (private mode, blocked): the choice then lasts for the session only.
    }
  }
}
