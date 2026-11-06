import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@jsverse/transloco';
import { Action } from '../../../shared/ui/action';
import { catchError, debounceTime, map, of, Subject } from 'rxjs';
import { ClinicPage, ClinicsApi } from '../../../../api/clinics-api';
import { LanguageService } from '../../../core/i18n/language.service';
import { PermissionChecklist, PermissionToggle } from './permission-checklist';
import { clinicTarget, itemKey, PermissionSaveQueue, PermissionTarget } from './permission-save-queue';

/** The clinics list is paged (at most 100 a page, D55): the picker shows the first 20 and asks to refine. */
export const PICKER_PAGE_SIZE = 20;
export const PICKER_DEBOUNCE_MS = 300;
const MAX_SEARCH_LENGTH = 100;

interface NameView {
  text: string;
  lang: 'ar' | 'en';
  dir: 'rtl' | 'ltr';
}

/** A clinic shown on this page: from the server, or picked here and not saved yet. */
interface ClinicEntry {
  id: string;
  nameAr: string;
  nameEn: string;
  /** Picked through the picker and never saved: it can still be cancelled. */
  picked: boolean;
}

interface CardView extends ClinicEntry {
  target: PermissionTarget;
  primary: NameView;
  secondary: NameView;
  selected: ReadonlySet<string>;
  pending: ReadonlySet<string>;
  saved: ReadonlySet<string>;
  errors: Record<string, string>;
}

type PickerResult = { page: ClinicPage } | { failed: true };

/**
 * The per-clinic permissions of one user (D57, D59), saved on each change (D64): no Save and no
 * "Remove all". A clinic added through the picker is saved by its first checked permission. A card stays
 * on the page until the user leaves it, even when its last permission was removed (the server then has no
 * row for that clinic), so nothing moves under the pointer. The picker searches the Clinics list (the
 * first 20 matches, with a hint to refine), so no page size limit can hide a clinic.
 */
@Component({
  selector: 'cb-clinic-permissions-editor',
  imports: [Action, TranslocoPipe, PermissionChecklist],
  templateUrl: './clinic-permissions-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClinicPermissionsEditor {
  private readonly clinics = inject(ClinicsApi);
  private readonly languages = inject(LanguageService);
  private readonly queue = inject(PermissionSaveQueue);

  /** The assignable clinic-scoped names, from the API. */
  readonly names = input.required<readonly string[]>();

  protected readonly maxSearchLength = MAX_SEARCH_LENGTH;

  /** Every clinic shown since the page opened, in the order it appeared; never shrinks. */
  private readonly shown = signal<ClinicEntry[]>([]);

  protected readonly cards = computed<CardView[]>(() => {
    const arabicFirst = this.languages.language() === 'ar';
    const pending = this.queue.pending();
    const saved = this.queue.saved();
    const errors = this.queue.errors();
    this.queue.server(); // the selections follow the server's answers

    return this.shown().map((entry) => {
      const target = clinicTarget(entry.id);
      const ar: NameView = { text: entry.nameAr, lang: 'ar', dir: 'rtl' };
      const en: NameView = { text: entry.nameEn, lang: 'en', dir: 'ltr' };
      const of = (keys: ReadonlySet<string>) => new Set(this.names().filter((name) => keys.has(itemKey(target, name))));
      return {
        ...entry,
        target,
        primary: arabicFirst ? ar : en,
        secondary: arabicFirst ? en : ar,
        selected: this.queue.selection(target),
        pending: of(pending),
        saved: of(saved),
        errors: Object.fromEntries(
          this.names().flatMap((name) => (errors[itemKey(target, name)] ? [[name, errors[itemKey(target, name)]]] : [])),
        ),
      };
    });
  });

  // ---- the picker
  protected readonly picking = signal(false);
  protected readonly searchText = signal('');
  private readonly committedSearch = signal('');
  private readonly typed = new Subject<string>();

  private readonly pickerResource = rxResource({
    params: () => (this.picking() ? { search: this.committedSearch() } : undefined),
    stream: ({ params }) =>
      this.clinics
        .list({
          ...(params.search === '' ? {} : { Search: params.search }),
          Page: 1,
          PageSize: PICKER_PAGE_SIZE,
          SortBy: 'nameEn',
          SortDirection: 'asc',
        })
        .pipe(
          map((page): PickerResult => ({ page })),
          catchError(() => of<PickerResult>({ failed: true })),
        ),
  });

  protected readonly pickerLoading = this.pickerResource.isLoading;
  protected readonly pickerFailed = computed(() => {
    const result = this.pickerResource.value();
    return result !== undefined && 'failed' in result;
  });

  /** Clinics from the search that are not already on the page. */
  protected readonly candidates = computed(() => {
    const result = this.pickerResource.value();
    if (result === undefined || !('page' in result)) return [];
    const taken = new Set(this.shown().map((entry) => entry.id));
    const arabicFirst = this.languages.language() === 'ar';
    return result.page.items
      .filter((clinic) => !taken.has(String(clinic.id)))
      .map((clinic) => {
        const ar: NameView = { text: clinic.nameAr, lang: 'ar', dir: 'rtl' };
        const en: NameView = { text: clinic.nameEn, lang: 'en', dir: 'ltr' };
        return {
          id: String(clinic.id),
          nameAr: clinic.nameAr,
          nameEn: clinic.nameEn,
          primary: arabicFirst ? ar : en,
          secondary: arabicFirst ? en : ar,
        };
      });
  });

  /** The search returned more clinics than it shows. */
  protected readonly more = computed(() => {
    const result = this.pickerResource.value();
    if (result === undefined || !('page' in result)) return null;
    const total = Number(result.page.totalCount);
    return total > result.page.items.length ? { shown: result.page.items.length, total } : null;
  });

  constructor() {
    this.typed
      .pipe(debounceTime(PICKER_DEBOUNCE_MS), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((text) => this.committedSearch.set(text.trim().slice(0, MAX_SEARCH_LENGTH)));

    // Every clinic the server reports joins the page; a picked clinic that saved is no longer "picked".
    effect(() => {
      const entries = this.queue.server()?.clinicPermissions ?? [];
      untracked(() =>
        this.shown.update((list) => {
          const stored = new Set(entries.map((entry) => String(entry.clinicId)));
          const known = new Set(list.map((entry) => entry.id));
          const updated = list.map((entry) => (entry.picked && stored.has(entry.id) ? { ...entry, picked: false } : entry));
          const added = entries
            .filter((entry) => !known.has(String(entry.clinicId)))
            .map((entry) => ({ id: String(entry.clinicId), nameAr: entry.clinicNameAr, nameEn: entry.clinicNameEn, picked: false }));
          return [...updated, ...added];
        }),
      );
    });
  }

  protected onToggled(card: CardView, change: PermissionToggle): void {
    this.queue.change(card.target, change.name, change.checked, this.names());
  }

  /** A picked clinic with nothing saved yet can be taken off the page. */
  protected canCancel(card: CardView): boolean {
    return card.picked && card.pending.size === 0 && card.selected.size === 0;
  }

  protected cancelAdded(card: CardView): void {
    this.shown.update((list) => list.filter((entry) => entry.id !== card.id));
  }

  // ---- the picker
  protected openPicker(): void {
    this.searchText.set('');
    this.committedSearch.set('');
    this.picking.set(true);
  }

  protected closePicker(): void {
    this.picking.set(false);
  }

  protected onSearchInput(event: Event): void {
    const element = event.target as HTMLInputElement;
    this.searchText.set(element.value);
    if (!(event as InputEvent).isComposing) {
      this.typed.next(element.value);
    }
  }

  protected onCompositionEnd(event: Event): void {
    this.typed.next((event.target as HTMLInputElement).value);
  }

  protected onSearchSubmit(event: Event): void {
    event.preventDefault();
    this.committedSearch.set(this.searchText().trim().slice(0, MAX_SEARCH_LENGTH));
  }

  protected retryPicker(): void {
    this.pickerResource.reload();
  }

  protected pick(candidate: { id: string; nameAr: string; nameEn: string }): void {
    this.shown.update((list) => [...list, { id: candidate.id, nameAr: candidate.nameAr, nameEn: candidate.nameEn, picked: true }]);
    this.picking.set(false);
  }
}
