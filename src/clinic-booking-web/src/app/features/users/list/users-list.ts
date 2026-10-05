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
  viewChild,
} from '@angular/core';
import { rxResource, takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { catchError, debounceTime, map, of, Subject } from 'rxjs';
import { UserPage, UsersApi } from '../../../../api/users-api';
import { ApiError, NETWORK_ERROR_KEY, parseApiError, UNEXPECTED_ERROR_KEY } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { IntlPipe } from '../../../core/format/intl.pipe';
import { Pager } from '../../../shared/ui/pager';
import { UsersSession } from '../users-session';
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
  STATUS_FILTERS,
  StatusFilter,
  toApiQuery,
  toQueryParams,
} from './list-query';

export const SEARCH_DEBOUNCE_MS = 300;

type ListResult = { page: UserPage } | { error: ApiError };
type View = 'loading' | 'error' | 'none' | 'no-results' | 'rows';

interface RowView {
  id: string;
  userName: string;
  isActive: boolean;
  mustChangePassword: boolean;
  createdAt: string;
}

/** Users (D57, D59): search by user name, filter by status, sort, page. The URL is the state (D53). */
@Component({
  selector: 'cb-users-list',
  imports: [NgTemplateOutlet, RouterLink, TranslocoPipe, IntlPipe, Pager],
  templateUrl: './users-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersList {
  private readonly api = inject(UsersApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(UsersSession);
  private readonly injector = inject(Injector);

  protected readonly sortFields = SORT_FIELDS;
  protected readonly statusFilters = STATUS_FILTERS;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly maxSearchLength = MAX_SEARCH_LENGTH;

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

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

  private readonly pageResult = computed(() => {
    const result = this.displayed();
    return result !== undefined && 'page' in result ? result.page : null;
  });

  protected readonly total = computed(() => Number(this.pageResult()?.totalCount ?? 0));
  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.state().pageSize)));
  protected readonly from = computed(() => (this.state().page - 1) * this.state().pageSize + 1);
  protected readonly to = computed(() => this.from() + (this.pageResult()?.items.length ?? 0) - 1);

  protected readonly rows = computed<RowView[]>(() =>
    (this.pageResult()?.items ?? []).map((item) => ({
      id: String(item.id),
      userName: item.userName,
      isActive: item.isActive,
      mustChangePassword: item.mustChangePassword,
      createdAt: item.createdAt,
    })),
  );

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
    const filtered = this.state().search !== '' || this.state().status !== 'all';
    return !filtered && this.total() === 0 ? 'none' : 'no-results';
  });

  constructor() {
    afterNextRender(() => this.focusHeading());

    // Debounced typing replaces the URL entry (no history spam); Enter searches at once.
    this.typed
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), takeUntilDestroyed())
      .subscribe((text) => this.search(text, true));
    this.submitted.pipe(takeUntilDestroyed()).subscribe((text) => this.search(text, false));

    effect(() => this.memory.lastQuery.set(toQueryParams(this.state())));

    // A page past the end (a hand-edited URL) steps back to the last page.
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

  /** Input is ignored while an IME composes and sent when composition ends. */
  protected onCompositionEnd(event: Event): void {
    this.typed.next((event.target as HTMLInputElement).value);
  }

  protected onSearchSubmit(event: Event): void {
    event.preventDefault();
    this.submitted.next(this.searchText());
  }

  protected clearFilters(): void {
    this.searchText.set('');
    this.apply({ search: '', status: 'all', page: 1 });
  }

  protected onStatus(event: Event): void {
    this.apply({ status: (event.target as HTMLSelectElement).value as StatusFilter, page: 1 });
  }

  protected onSort(event: Event): void {
    this.apply({ sortBy: (event.target as HTMLSelectElement).value as SortField, page: 1 });
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

  /** Re-issues the same query; the page itself is not rebuilt. */
  protected retry(): void {
    this.resource.reload();
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
