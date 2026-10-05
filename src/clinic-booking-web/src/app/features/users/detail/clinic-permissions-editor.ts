import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@jsverse/transloco';
import { catchError, debounceTime, firstValueFrom, map, of, Subject } from 'rxjs';
import { ClinicPage, ClinicsApi } from '../../../../api/clinics-api';
import { UserDetail, UsersApi } from '../../../../api/users-api';
import { parseApiError } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { PermissionChecklist } from './permission-checklist';
import { PermissionsSaved } from './global-permissions-editor';

/** The clinics list is paged (at most 100 a page, D55): the picker shows the first 20 and asks to refine. */
export const PICKER_PAGE_SIZE = 20;
export const PICKER_DEBOUNCE_MS = 300;
const MAX_SEARCH_LENGTH = 100;

interface CardView {
  id: string;
  /** False for a clinic the user has just picked and has not saved yet. */
  stored: boolean;
  nameAr: string;
  nameEn: string;
  primary: NameView;
  secondary: NameView;
}

interface NameView {
  text: string;
  lang: 'ar' | 'en';
  dir: 'rtl' | 'ltr';
}

interface PickedClinic {
  id: string;
  nameAr: string;
  nameEn: string;
}

type PickerResult = { page: ClinicPage } | { failed: true };

/**
 * The per-clinic permissions of one user (D57, D59). Each clinic is its own card with its own Save: a
 * save is a full replace for that clinic and an empty set removes the grants. After a save the detail is
 * reloaded and the server's state is shown. A clinic is added through a search box over the Clinics list
 * (the first 20 matches, with a hint to refine), so no page size limit can hide a clinic.
 */
@Component({
  selector: 'cb-clinic-permissions-editor',
  imports: [TranslocoPipe, PermissionChecklist],
  templateUrl: './clinic-permissions-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClinicPermissionsEditor {
  private readonly users = inject(UsersApi);
  private readonly clinics = inject(ClinicsApi);
  private readonly messages = inject(ErrorMessageService);
  private readonly languages = inject(LanguageService);

  readonly user = input.required<UserDetail>();
  /** The assignable clinic-scoped names, from the API. */
  readonly names = input.required<readonly string[]>();
  readonly saved = output<PermissionsSaved>();

  protected readonly maxSearchLength = MAX_SEARCH_LENGTH;

  /** Unsaved edits, by clinic id: the selection shown is the edit when there is one, else the server's. */
  private readonly edits = signal<Record<string, ReadonlySet<string>>>({});
  /** Clinics picked but not saved yet. */
  private readonly added = signal<PickedClinic[]>([]);

  protected readonly busyId = signal<string | null>(null);
  protected readonly errors = signal<Record<string, string>>({});

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

  /** Clinics from the search that the user does not already have a card for. */
  protected readonly candidates = computed(() => {
    const result = this.pickerResource.value();
    if (result === undefined || !('page' in result)) return [];
    const taken = new Set(this.cards().map((card) => card.id));
    return result.page.items
      .filter((clinic) => !taken.has(String(clinic.id)))
      .map((clinic) => this.card(String(clinic.id), clinic.nameAr, clinic.nameEn, false));
  });

  /** The search returned more clinics than it shows. */
  protected readonly more = computed(() => {
    const result = this.pickerResource.value();
    if (result === undefined || !('page' in result)) return null;
    const total = Number(result.page.totalCount);
    return total > result.page.items.length ? { shown: result.page.items.length, total } : null;
  });

  protected readonly cards = computed<CardView[]>(() => {
    const stored = this.user().clinicPermissions.map((entry) =>
      this.card(String(entry.clinicId), entry.clinicNameAr, entry.clinicNameEn, true),
    );
    const known = new Set(stored.map((card) => card.id));
    const unsaved = this.added()
      .filter((clinic) => !known.has(clinic.id))
      .map((clinic) => this.card(clinic.id, clinic.nameAr, clinic.nameEn, false));
    return [...stored, ...unsaved];
  });

  constructor() {
    this.typed
      .pipe(debounceTime(PICKER_DEBOUNCE_MS), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((text) => this.committedSearch.set(text.trim().slice(0, MAX_SEARCH_LENGTH)));
  }

  // ---- selections
  protected selectionOf(id: string): ReadonlySet<string> {
    const edited = this.edits()[id];
    if (edited !== undefined) return edited;
    const entry = this.user().clinicPermissions.find((candidate) => String(candidate.clinicId) === id);
    return new Set(entry?.permissions ?? []);
  }

  protected toggle(id: string, name: string): void {
    const next = new Set(this.selectionOf(id));
    if (!next.delete(name)) {
      next.add(name);
    }
    this.edits.update((edits) => ({ ...edits, [id]: next }));
  }

  /** Whether Save has something to send: a stored clinic that differs, or a new clinic with a selection. */
  protected canSave(card: CardView): boolean {
    const selected = this.selectionOf(card.id);
    if (!card.stored) return selected.size > 0;
    const entry = this.user().clinicPermissions.find((candidate) => String(candidate.clinicId) === card.id);
    const saved = entry?.permissions ?? [];
    return selected.size !== saved.length || saved.some((name) => !selected.has(name));
  }

  protected async save(card: CardView): Promise<void> {
    await this.send(card, this.names().filter((name) => this.selectionOf(card.id).has(name)));
  }

  /** An empty set: the server removes every grant in this clinic. */
  protected async removeAll(card: CardView): Promise<void> {
    await this.send(card, []);
  }

  protected cancelAdded(card: CardView): void {
    this.added.update((list) => list.filter((clinic) => clinic.id !== card.id));
    this.edits.update((edits) => without(edits, card.id));
  }

  private async send(card: CardView, permissions: string[]): Promise<void> {
    this.busyId.set(card.id);
    this.errors.update((errors) => without(errors, card.id));
    const id = this.user().id;

    try {
      const replaced = await firstValueFrom(this.users.replaceClinicPermissions(id, card.id, permissions));
      // Show the server's state: reload it, and fall back to the answer of the PUT if the reload fails.
      const detail = await firstValueFrom(this.users.get(id)).catch(() => replaced);
      this.added.update((list) => list.filter((clinic) => clinic.id !== card.id));
      this.edits.update((edits) => without(edits, card.id));
      this.saved.emit({ detail, flash: 'users.flash.clinic_saved' });
    } catch (error: unknown) {
      const key = this.messages.forError(parseApiError(error));
      this.errors.update((errors) => ({ ...errors, [card.id]: key }));
    } finally {
      this.busyId.set(null);
    }
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
    const input = event.target as HTMLInputElement;
    this.searchText.set(input.value);
    if (!(event as InputEvent).isComposing) {
      this.typed.next(input.value);
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

  protected pick(card: CardView): void {
    this.added.update((list) => [...list, { id: card.id, nameAr: card.nameAr, nameEn: card.nameEn }]);
    this.picking.set(false);
  }

  private card(id: string, nameAr: string, nameEn: string, stored: boolean): CardView {
    const arabicFirst = this.languages.language() === 'ar';
    const ar: NameView = { text: nameAr, lang: 'ar', dir: 'rtl' };
    const en: NameView = { text: nameEn, lang: 'en', dir: 'ltr' };
    return { id, stored, nameAr, nameEn, primary: arabicFirst ? ar : en, secondary: arabicFirst ? en : ar };
  }
}

function without<T>(record: Record<string, T>, key: string): Record<string, T> {
  const copy = { ...record };
  delete copy[key];
  return copy;
}
