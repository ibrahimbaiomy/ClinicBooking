import { InjectionToken } from '@angular/core';

/**
 * Front-end settings, changed by editing this file (D64). There is no settings screen.
 */
export const APP_SETTINGS = {
  /** How long a success toast and the "Saved" indicator stay before they disappear, in milliseconds. */
  messageDurationMs: 5000,
} as const;

/** The message duration as an injectable value (tests shorten or pin it). */
export const MESSAGE_DURATION_MS = new InjectionToken<number>('MESSAGE_DURATION_MS', {
  providedIn: 'root',
  factory: () => APP_SETTINGS.messageDurationMs,
});
