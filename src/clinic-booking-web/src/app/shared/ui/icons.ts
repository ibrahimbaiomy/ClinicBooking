import { InjectionToken } from '@angular/core';
import checkCircleFill from 'bootstrap-icons/icons/check-circle-fill.svg';
import checkLg from 'bootstrap-icons/icons/check-lg.svg';
import pencil from 'bootstrap-icons/icons/pencil.svg';
import plusLg from 'bootstrap-icons/icons/plus-lg.svg';
import trash from 'bootstrap-icons/icons/trash.svg';
import xLg from 'bootstrap-icons/icons/x-lg.svg';

/**
 * The Bootstrap Icons the app uses (D65), self-hosted: each `.svg` is imported as text at build time, so only
 * these ship (no CDN, no font). Add an icon here with one import. `directional` icons point a way (an arrow,
 * a chevron) and are mirrored in RTL (D25); plus, pencil, trash, check and x are not.
 */
export interface IconDefinition {
  svg: string;
  directional: boolean;
}

export const ICONS = {
  'check-circle-fill': { svg: checkCircleFill, directional: false },
  check: { svg: checkLg, directional: false },
  pencil: { svg: pencil, directional: false },
  plus: { svg: plusLg, directional: false },
  trash: { svg: trash, directional: false },
  x: { svg: xLg, directional: false },
} as const satisfies Record<string, IconDefinition>;

export type IconName = keyof typeof ICONS;

/** The registry as an injectable value, so a test can add a directional icon without shipping one. */
export const ICON_REGISTRY = new InjectionToken<Readonly<Record<string, IconDefinition>>>('ICON_REGISTRY', {
  providedIn: 'root',
  factory: () => ICONS,
});
