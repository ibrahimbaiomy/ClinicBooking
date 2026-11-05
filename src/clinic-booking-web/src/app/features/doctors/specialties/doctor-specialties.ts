import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { Doctor, DoctorsApi } from '../../../../api/doctors-api';
import { ApiError, parseApiError, supportReference } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { SessionService } from '../../../core/auth/session.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { ToastService } from '../../../core/ui/toast.service';
import { byUiName, DoctorReferenceData, managesAnyClinicOf, namesFor, NameView, ReferenceOption } from '../doctor-view';
import { DoctorsSession } from '../doctors-session';

/** Mirror of DoctorRules.MaxSpecialties (D61). */
export const MAX_SPECIALTIES = 10;

const CONFLICT_KEY = 'error.concurrency.conflict';

type LoadState = 'loading' | 'ready' | 'notFound' | 'notAllowed' | 'error';

interface OptionView {
  id: string;
  name: NameView;
}

/**
 * `/doctors/:id/specialties` (D64): the checkbox list (at least one, at most ten), saved through the doctor
 * PUT with its row version and the current names unchanged; the D53 conflict flow. After a save: the
 * remembered list with a toast. Allowed with doctors.manage in any of the doctor's clinics.
 */
@Component({
  selector: 'cb-doctor-specialties',
  imports: [RouterLink, TranslocoPipe],
  templateUrl: './doctor-specialties.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DoctorSpecialtiesPage {
  private readonly api = inject(DoctorsApi);
  private readonly reference = inject(DoctorReferenceData);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(DoctorsSession);
  private readonly session = inject(SessionService);
  private readonly languages = inject(LanguageService);
  private readonly toasts = inject(ToastService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly id = this.route.snapshot.paramMap.get('id') ?? '';
  protected readonly listQuery = this.memory.lastQuery;

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorKey = signal<string | null>(null);
  protected readonly record = signal<Doctor | null>(null);

  protected readonly chosen = signal<readonly string[]>([]);
  protected readonly touched = signal(false);
  protected readonly saving = signal(false);
  /** Error keys for the list (client rule or server field error). */
  protected readonly fieldErrors = signal<string[]>([]);
  protected readonly formErrorKey = signal<string | null>(null);
  protected readonly correlationId = signal<string | null>(null);
  protected readonly conflict = signal(false);
  protected readonly reloading = signal(false);
  protected readonly earlier = signal<readonly string[] | null>(null);

  private readonly specialtyList = signal<ReferenceOption[]>([]);
  protected readonly specialtiesIncomplete = signal(false);
  protected readonly specialtiesFailed = signal(false);

  protected readonly names = computed(() => {
    const record = this.record();
    return record === null ? null : namesFor(record.nameAr, record.nameEn, this.languages.language());
  });

  /** The loaded specialties, plus the doctor's own (they may lie beyond the first 100), by UI-language name. */
  protected readonly options = computed<OptionView[]>(() => {
    const language = this.languages.language();
    const byId = new Map(this.specialtyList().map((option) => [option.id, option]));
    for (const own of this.record()?.specialties ?? []) {
      byId.set(String(own.id), { id: String(own.id), nameAr: own.nameAr, nameEn: own.nameEn });
    }
    return byUiName([...byId.values()], language).map((option) => ({
      id: option.id,
      name: namesFor(option.nameAr, option.nameEn, language).primary,
    }));
  });

  /** The earlier choice after a conflict reload, with names instead of ids. */
  protected readonly earlierOptions = computed(() => {
    const ids = new Set(this.earlier() ?? []);
    return this.options().filter((option) => ids.has(option.id));
  });

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly banner = viewChild<ElementRef<HTMLElement>>('banner');

  constructor() {
    afterNextRender(() => this.heading()?.nativeElement.focus());
    this.reference
      .specialtyList()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.specialtyList.set(list.options);
          this.specialtiesIncomplete.set(list.incomplete);
        },
        error: () => this.specialtiesFailed.set(true),
      });

    if (/^\d+$/.test(this.id)) {
      this.load();
    } else {
      this.loadState.set('notFound');
    }
  }

  protected load(): void {
    this.loadState.set('loading');
    this.api
      .get(this.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (record) => {
          this.show(record);
          this.loadState.set(managesAnyClinicOf(this.session, record.clinics) ? 'ready' : 'notAllowed');
        },
        error: (error: unknown) => {
          const failure = parseApiError(error);
          this.loadErrorKey.set(this.messages.forError(failure));
          this.loadState.set(failure.status === 404 ? 'notFound' : 'error');
        },
      });
  }

  protected isChecked(id: string): boolean {
    return this.chosen().includes(id);
  }

  protected toggle(id: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.chosen.update((ids) => (checked ? [...ids.filter((x) => x !== id), id] : ids.filter((x) => x !== id)));
    this.touched.set(true);
    this.fieldErrors.set(this.clientErrors());
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.formErrorKey.set(null);
    this.correlationId.set(null);
    this.touched.set(true);
    const problems = this.clientErrors();
    this.fieldErrors.set(problems);
    if (problems.length > 0) {
      this.focusFirstBox();
      return;
    }

    const record = this.record();
    this.saving.set(true);
    try {
      await firstValueFrom(
        this.api.update(this.id, {
          // The names have their own page: sent back unchanged (the PUT is a full replace, D61).
          nameAr: record?.nameAr ?? '',
          nameEn: record?.nameEn ?? '',
          specialtyIds: this.chosen().map(Number),
          rowVersion: record?.rowVersion ?? null,
        }),
      );
    } catch (error: unknown) {
      this.failed(parseApiError(error));
      return;
    } finally {
      this.saving.set(false);
    }

    this.toasts.showToast('doctors.flash.specialties_updated');
    await this.router.navigate(['/doctors'], { queryParams: this.memory.lastQuery() });
  }

  protected messageKey(key: string): string {
    return this.messages.keyFor(key);
  }

  protected reload(): void {
    this.reloading.set(true);
    const mine = this.chosen();
    this.api
      .get(this.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (record) => {
          this.earlier.set(mine);
          this.show(record);
          this.conflict.set(false);
          this.reloading.set(false);
        },
        error: (error: unknown) => {
          const failure = parseApiError(error);
          this.reloading.set(false);
          if (failure.status === 404) {
            this.loadState.set('notFound');
          } else {
            this.formErrorKey.set(this.messages.forError(failure));
          }
        },
      });
  }

  protected dismissEarlier(): void {
    this.earlier.set(null);
  }

  private clientErrors(): string[] {
    const count = this.chosen().length;
    if (count === 0) return ['error.doctor.specialties_required'];
    return count > MAX_SPECIALTIES ? ['error.doctor.specialties_too_many'] : [];
  }

  /** Field keys and specialty keys on the list, 409 conflict banner, 404 "not found", else form-level. */
  private failed(error: ApiError): void {
    const onField = error.fieldErrors['specialtyIds'] ?? [];
    const keys = onField.length > 0 ? onField : /^error\.doctor\.(specialties_|specialty_)/.test(error.key) ? [error.key] : [];

    if (error.key === CONFLICT_KEY) {
      this.conflict.set(true);
      afterNextRender(() => this.banner()?.nativeElement.focus(), { injector: this.injector });
    } else if (error.status === 404) {
      this.loadErrorKey.set(this.messages.forError(error));
      this.loadState.set('notFound');
    } else if (keys.length > 0) {
      this.fieldErrors.set(keys);
      afterNextRender(() => this.focusFirstBox(), { injector: this.injector });
    } else {
      this.formErrorKey.set(this.messages.forError(error));
      this.correlationId.set(supportReference(error));
    }
  }

  private focusFirstBox(): void {
    this.host.nativeElement.querySelector<HTMLInputElement>('input[id^="doctor-specialty-"]')?.focus();
  }

  private show(record: Doctor): void {
    this.record.set(record);
    this.chosen.set(record.specialties.map((s) => String(s.id)));
    this.touched.set(false);
    this.fieldErrors.set([]);
  }
}
