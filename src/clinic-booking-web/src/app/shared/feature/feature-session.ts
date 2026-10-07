import { signal } from '@angular/core';
import { Params } from '@angular/router';

/**
 * Two small pieces of memory for one feature's screens (D53, D62): the list's last query string, so Save
 * and Cancel return to the same search, page and sort; and a one-time status message that the next page
 * shows. Neither is stored anywhere: a reload starts clean. Each feature has its own injectable subclass,
 * so features never share this memory.
 */
export abstract class FeatureSession {
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
