import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { rxResource, takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { catchError, debounceTime, map, of, Subject } from 'rxjs';
import { DoctorPage, DoctorsApi } from '../../../../api/doctors-api';
import { ApiError, NETWORK_ERROR_KEY, parseApiError, UNEXPECTED_ERROR_KEY } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { ClinicPermissions } from '../../../core/auth/permissions';
import { SessionService } from '../../../core/auth/session.service';
import { IntlPipe } from '../../../core/format/intl.pipe';
import { LanguageService } from '../../../core/i18n/language.service';
import { Pager } from '../../../shared/ui/pager';
import { DoctorsSession } from '../doctors-session';
import { byUiName, DoctorReferenceData, namesFor, NameView, ReferenceList } from '../doctor-view';
import {
  isSameListState,
  ListState,
  MAX_SEARCH_LENGTH,
  normalizeSearch,
  PAGE_SIZES,
  parseListState,
  SortDirection,
  SortField,
  StatusFilter,
  STATUS_FILTERS,
  toApiQuery,
  toQueryParams,
} from './list-query';

export const SEARCH_DEBOUNCE_MS = 300;

type ListResult = { page: DoctorPage } | { error: ApiError };
type View = 'loading' | 'error' | 'none' | 'no-results' | 'rows';
type ReferenceResult = ReferenceList | { failed: true };

interface OptionView {
  id: string;
  label: string;
}

interface AssignmentView {
  id: string;
  name: NameView;
  isActive: boolean;
}

interface RowView {
  id: string;
  primary: NameView;
  secondary: NameView;
  specialties: NameView[];
  clinics: AssignmentView[];
  slotMinutes: number;
}

/**
 * The doctors list (D62): any signed-in user reads it (D57). Search, specialty, clinic and status
 * filters, paging and sort all live in the URL (D53). The status filter means "active in that clinic",
 * so it is enabled only once a clinic is chosen (error.doctor.is_active_requires_clinic, D61).
 */
@Component({
  selector: 'cb-doctors-list',
  imports: [NgTemplateOutlet, RouterLink, TranslocoPipe, IntlPipe, Pager],
  templateUrl: './doctors-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DoctorsList {
  private readonly api = inject(DoctorsApi);
  private readonly reference = inject(DoctorReferenceData);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(DoctorsSession);
  private readonly languages = inject(LanguageService);
  private readonly session = inject(SessionService);

  protected readonly pageSizes = PAGE_SIZES;
  protected readonly statusFilters = STATUS_FILTERS;
  protected readonly maxSearchLength = MAX_SEARCH_LENGTH;

  /** UX only: the API decides (D57). */
  protected readonly canCreate = computed(() => this.session.canInAny(ClinicPermissions.DoctorsManage));

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  // The URL is the single source of truth (D53): reload and back/forward just work.
  private readonly queryParams = toSignal(this.route.queryParamMap, { requireSync: true });
  protected readonly state = computed(() => parseListState(this.queryParams()), { equal: isSameListState });

  /** A newer query cancels the older request, so a late response is never shown. */
  private readonly resource = rxResource({
    params: () => toApiQuery(this.state()),
    stream: ({ params }) =>
      this.api.list(params).pipe(
        map((page): ListResult => ({ page })),
        catchError((error: unknown) => of<ListResult>({ error: parseApiError(error) })),
      ),
  });

  private readonly displayed = linkedSignal<ListResult | undefined, ListResult | undefined>({
    source: this.resource.value,
    computation: (value, previous) => value ?? previous?.value,
  });

  protected readonly loading = this.resource.isLoading;

  // ---- the filter options: loaded once (100 at most, D62)
  private readonly specialtyResource = rxResource({
    stream: () => this.reference.specialtyList().pipe(catchError(() => of<ReferenceResult>({ failed: true }))),
  });
  private readonly clinicResource = rxResource({
    stream: () => this.reference.clinicList().pipe(catchError(() => of<ReferenceResult>({ failed: true }))),
  });

  protected readonly specialtyOptions = computed(() => this.optionsOf(this.specialtyResource.value()));
  protected readonly clinicOptions = computed(() => this.optionsOf(this.clinicResource.value()));
  protected readonly optionsIncomplete = computed(
    () => this.isIncomplete(this.specialtyResource.value()) || this.isIncomplete(this.clinicResource.value()),
  );
  protected readonly optionsFailed = computed(
    () => this.hasFailed(this.specialtyResource.value()) || this.hasFailed(this.clinicResource.value()),
  );

  /** The status filter is offered only with a clinic (the API refuses IsActive alone). */
  protected readonly statusEnabled = computed(() => this.state().clinic !== '');

  protected readonly searchText = linkedSignal(() => this.state().search);
  private readonly typed = new Subject<string>();
  private readonly submitted = new Subject<string>();

  protected readonly status = signal<string | null>(null);

  private readonly pageResult = computed(() => {
    const result = this.displayed();
    return result !== undefined && 'page' in result ? result.page : null;
  });

  protected readonly total = computed(() => Number(this.pageResult()?.totalCount ?? 0));
  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.state().pageSize)));
  protected readonly from = computed(() => (this.state().page - 1) * this.state().pageSize + 1);
  protected readonly to = computed(() => this.from() + (this.pageResult()?.items.length ?? 0) - 1);

  protected readonly rows = computed<RowView[]>(() => {
    const language = this.languages.language();
    return (this.pageResult()?.items ?? []).map((item) => ({
      id: String(item.id),
      ...namesFor(item.nameAr, item.nameEn, language),
      specialties: byUiName(item.specialties, language).map((s) => namesFor(s.nameAr, s.nameEn, language).primary),
      clinics: byUiName(item.clinics, language).map((c) => ({
        id: String(c.clinicId),
        name: namesFor(c.nameAr, c.nameEn, language).primary,
        isActive: c.isActive,
      })),
      slotMinutes: Number(item.slotMinutes),
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

  protected readonly correlationId = computed(() => {
    const failure = this.failure();
    return failure !== null && (failure.key === UNEXPECTED_ERROR_KEY || failure.key === NETWORK_ERROR_KEY)
      ? failure.correlationId
      : null;
  });

  private readonly filtered = computed(() => {
    const state = this.state();
    return state.search !== '' || state.specialty !== '' || state.clinic !== '';
  });

  protected readonly view = computed<View>(() => {
    if (this.displayed() === undefined) return 'loading';
    if (this.failure() !== null) return 'error';
    if (this.rows().length > 0) return 'rows';
    return !this.filtered() && this.total() === 0 ? 'none' : 'no-results';
  });

  constructor() {
    this.status.set(this.memory.takeFlash());
    afterNextRender(() => this.heading()?.nativeElement.focus());

    this.typed.pipe(debounceTime(SEARCH_DEBOUNCE_MS), takeUntilDestroyed()).subscribe((text) => this.search(text, true));
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

  protected onCompositionEnd(event: Event): void {
    this.typed.next((event.target as HTMLInputElement).value);
  }

  protected onSearchSubmit(event: Event): void {
    event.preventDefault();
    this.submitted.next(this.searchText());
  }

  /** Clears every filter, not only the search: "no results" may come from any of them. */
  protected clearFilters(): void {
    this.searchText.set('');
    this.apply({ search: '', specialty: '', clinic: '', status: 'all', page: 1 });
  }

  protected onSpecialty(event: Event): void {
    this.apply({ specialty: (event.target as HTMLSelectElement).value, page: 1 });
  }

  /** Choosing "every clinic" also resets the status, which has no meaning without one. */
  protected onClinic(event: Event): void {
    const clinic = (event.target as HTMLSelectElement).value;
    this.apply({ clinic, ...(clinic === '' ? { status: 'all' as StatusFilter } : {}), page: 1 });
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

  protected retry(): void {
    this.resource.reload();
  }

  private optionsOf(result: ReferenceResult | undefined): OptionView[] {
    if (result === undefined || 'failed' in result) return [];
    const language = this.languages.language();
    return byUiName(result.options, language).map((option) => ({
      id: option.id,
      label: namesFor(option.nameAr, option.nameEn, language).primary.text,
    }));
  }

  private isIncomplete(result: ReferenceResult | undefined): boolean {
    return result !== undefined && !('failed' in result) && result.incomplete;
  }

  private hasFailed(result: ReferenceResult | undefined): boolean {
    return result !== undefined && 'failed' in result;
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
}
