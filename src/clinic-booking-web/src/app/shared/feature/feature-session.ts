import { signal } from '@angular/core';
import { Params } from '@angular/router';

/**
 * A feature's memory (D53, D62): the list's last query string, so Save and Cancel return to the same
 * search, page and sort. Not stored anywhere: a reload starts clean. Each feature has its own injectable
 * subclass, so features never share it. Success messages are toasts now (`ToastService`, D64).
 */
export abstract class FeatureSession {
  readonly lastQuery = signal<Params>({});
}
