import { Injectable, signal } from '@angular/core';
import { Params } from '@angular/router';

/**
 * Two small pieces of memory for the Specialties screens (D53): the list's last query string, so
 * Save and Cancel return to the same search, page and sort; and a one-time status message that
 * the list shows after a save. Neither is stored anywhere: a reload starts clean.
 */
@Injectable({ providedIn: 'root' })
export class SpecialtiesSession {
  readonly lastQuery = signal<Params>({});

  private flash: string | null = null;

  setFlash(key: string): void {
    this.flash = key;
  }

  takeFlash(): string | null {
    const key = this.flash;
    this.flash = null;
    return key;
  }
}
