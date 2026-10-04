import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  Injector,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { rxResource, takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { catchError, debounceTime, firstValueFrom, map, of, Subject } from 'rxjs';
import { ClinicPage, ClinicsApi } from '../../../../api/clinics-api';
import { ApiError, NETWORK_ERROR_KEY, parseApiError, UNEXPECTED_ERROR_KEY } from '../../../core/auth/api-error';
import { CanDirective } from '../../../core/auth/can.directive';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { Permissions } from '../../../core/auth/permissions';
import { IntlPipe } from '../../../core/format/intl.pipe';
import { PhonePipe } from '../../../core/format/phone.pipe';
import { LanguageService } from '../../../core/i18n/language.service';
import { ConfirmDialog } from '../../../shared/ui/confirm-dialog';
import { Pager } from '../../../shared/ui/pager';
import { ClinicsSession } from '../clinics-session';
import {
  isSameListState,
  ListState,
  MAX_SEARCH_LENGTH,
  normalizeSearch,
  PAGE_SIZES,
  parseListState,
  SORT_FIELDS,
  SortDirection,
  SortField,
  toApiQuery,
  toQueryParams,
} from './list-query';

export const SEARCH_DEBOUNCE_MS = 300;

type ListResult = { page: ClinicPage } | { error: ApiError };
type View = 'loading' | 'error' | 'none' | 'no-results' | 'rows';

interface NameView {
  text: string;
  lang: 'ar' | 'en';
  dir: 'rtl' | 'ltr';
}

interface RowView {
  id: string;
  nameAr: string;
  nameEn: string;
  /** The name in the UI language: shown first and prominent. */
  primary: NameView;
  secondary: NameView;
  /** The stored values (E.164 phone); the template formats the phone with the `phone` pipe. */
  address: string | null;
  phone: string | null;
  createdAt: string;
}

const AR = (text: string): NameView => ({ text, lang: 'ar', dir: 'rtl' });
const EN = (text: string): NameView => ({ text, lang: 'en', dir: 'ltr' });

@Component({
  selector: 'cb-clinics-list',
  imports: [NgTemplateOutlet, RouterLink, TranslocoPipe, IntlPipe, PhonePipe, CanDirective, Pager, ConfirmDialog],
  templateUrl: './clinics-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClinicsList {
  private readonly api = inject(ClinicsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(ClinicsSession);
  private readonly languages = inject(LanguageService);
  private readonly injector = inject(Injector);

  protected readonly permissions = Permissions;
  protected readonly sortFields = SORT_FIELDS;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly maxSearchLength = MAX_SEARCH_LENGTH;

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  // The URL is the single source of truth (D53): reload and back/forward just work.
  private readonly queryParams = toSignal(this.route.queryParamMap, { requireSync: true });
  protected readonly state = computed(() => parseListState(this.queryParams()), {
    equal: isSameListState,
  });

  /** A newer query cancels the older request, so a late response is never shown. */
  private readonly resource = rxResource({
    params: () => toApiQuery(this.state()),
    stream: ({ params }) =>
      this.api.list(params).pipe(
        map((page): ListResult => ({ page })),
        catchError((error: unknown) => of<ListResult>({ error: parseApiError(error) })),
      ),
  });

  /** Keeps the previous result on screen while the next one loads. */
  private readonly displayed = linkedSignal<ListResult | undefined, ListResult | undefined>({
    source: this.resource.value,
    computation: (value, previous) => value ?? previous?.value,
  });

  protected readonly loading = this.resource.isLoading;

  protected readonly searchText = linkedSignal(() => this.state().search);
  private readonly typed = new Subject<string>();
  private readonly submitted = new Subject<string>();

  protected readonly status = signal<string | null>(null);
  protected readonly pendingDelete = signal<RowView | null>(null);
  protected readonly deleting = signal(false);
  protected readonly deleteErrorKey = signal<string | null>(null);

  private readonly pageResult = computed(() => {
    const result = this.displayed();
    return result !== undefined && 'page' in result ? result.page : null;
  });

  protected readonly total = computed(() => Number(this.pageResult()?.totalCount ?? 0));
  protected readonly totalPages = computed(() => totalPagesOf(this.total(), this.state().pageSize));
  protected readonly from = computed(() => (this.state().page - 1) * this.state().pageSize + 1);
  protected readonly to = computed(() => this.from() + (this.pageResult()?.items.length ?? 0) - 1);

  protected readonly rows = computed<RowView[]>(() => {
    const arabicFirst = this.languages.language() === 'ar';
    return (this.pageResult()?.items ?? []).map((item) => ({
      id: String(item.id),
      nameAr: item.nameAr,
      nameEn: item.nameEn,
      primary: arabicFirst ? AR(item.nameAr) : EN(item.nameEn),
      secondary: arabicFirst ? EN(item.nameEn) : AR(item.nameAr),
      address: item.address,
      phone: item.phone,
      createdAt: item.createdAt,
    }));
  });

  private readonly failure = computed(() => {
    const result = this.displayed();
    return result !== undefined && 'error' in result ? result.error : null;
  });

  protected readonly errorKey = computed(() => {
    const failure = this.failure();
    return failure === null ? null : this.messages.forError(failure);
  });

  /** Only for failures nobody can explain to the user: a reference to quote. */
  protected readonly correlationId = computed(() => {
    const failure = this.failure();
    return failure !== null && (failure.key === UNEXPECTED_ERROR_KEY || failure.key === NETWORK_ERROR_KEY)
      ? failure.correlationId
      : null;
  });

  protected readonly view = computed<View>(() => {
    if (this.displayed() === undefined) return 'loading';
    if (this.failure() !== null) return 'error';
    if (this.rows().length > 0) return 'rows';
    return this.state().search === '' && this.total() === 0 ? 'none' : 'no-results';
  });

  constructor() {
    this.status.set(this.memory.takeFlash());
    afterNextRender(() => this.focusHeading());

    // Debounced typing replaces the URL entry (no history spam); Enter searches at once.
    this.typed
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), takeUntilDestroyed())
      .subscribe((text) => this.search(text, true));
    this.submitted.pipe(takeUntilDestroyed()).subscribe((text) => this.search(text, false));

    effect(() => this.memory.lastQuery.set(toQueryParams(this.state())));

    // A page past the end (after a delete, or a hand-edited URL) steps back to the last page.
    effect(() => {
      const state = this.state();
      if (this.resource.isLoading() || this.pageResult() === null) return;
      if (state.page > this.totalPages()) {
        this.apply({ page: this.totalPages() }, true);
      }
    });
  }

  protected onSearchInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.searchText.set(input.value);
    if (!(event as InputEvent).isComposing) {
      this.typed.next(input.value);
    }
  }

  /** Input is ignored while an IME composes (Arabic, etc.) and sent when composition ends. */
  protected onCompositionEnd(event: Event): void {
    this.typed.next((event.target as HTMLInputElement).value);
  }

  protected onSearchSubmit(event: Event): void {
    event.preventDefault();
    this.submitted.next(this.searchText());
  }

  protected clearSearch(): void {
    this.searchText.set('');
    this.apply({ search: '', page: 1 });
  }

  protected onSort(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.apply({ sortBy: value as SortField, page: 1 });
  }

  protected toggleDirection(): void {
    const next: SortDirection = this.state().sortDirection === 'asc' ? 'desc' : 'asc';
    this.apply({ sortDirection: next, page: 1 });
  }

  protected onPageSize(event: Event): void {
    this.apply({ pageSize: Number((event.target as HTMLSelectElement).value), page: 1 });
  }

  protected goToPage(page: number): void {
    this.apply({ page });
  }

  /** Re-issues the same query; the page itself is not reloaded. */
  protected retry(): void {
    this.resource.reload();
  }

  protected askDelete(row: RowView): void {
    this.deleteErrorKey.set(null);
    this.pendingDelete.set(row);
  }

  protected cancelDelete(): void {
    if (!this.deleting()) {
      this.pendingDelete.set(null);
    }
  }

  protected async confirmDelete(): Promise<void> {
    const row = this.pendingDelete();
    if (row === null) return;

    this.deleting.set(true);
    try {
      await firstValueFrom(this.api.delete(row.id));
      this.finishDelete('clinics.flash.deleted');
    } catch (error: unknown) {
      const failure = parseApiError(error);
      if (failure.status === 404) {
        // Already deleted elsewhere: the outcome is what the user wanted.
        this.finishDelete('clinics.flash.already_deleted');
      } else {
        // Stays in the dialog; a later error.clinic.in_use needs only its translation (D55).
        this.deleteErrorKey.set(this.messages.forError(failure));
      }
    } finally {
      this.deleting.set(false);
    }
  }

  private finishDelete(statusKey: string): void {
    this.pendingDelete.set(null);
    this.status.set(statusKey);
    this.resource.reload();
    this.focusHeading();
  }

  private search(text: string, replace: boolean): void {
    const search = normalizeSearch(text);
    if (search !== this.state().search) {
      this.apply({ search, page: 1 }, replace);
    }
  }

  private apply(change: Partial<ListState>, replace = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: toQueryParams({ ...this.state(), ...change }),
      replaceUrl: replace,
    });
  }

  private focusHeading(): void {
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }
}

function totalPagesOf(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(total / pageSize));
}
