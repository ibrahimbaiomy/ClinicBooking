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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom, forkJoin } from 'rxjs';
import { Doctor, DoctorsApi, WorkingHours } from '../../../../api/doctors-api';
import { ApiError, parseApiError, supportReference } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { ClinicPermissions } from '../../../core/auth/permissions';
import { SessionService } from '../../../core/auth/session.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { namesFor, uiName } from '../doctor-view';
import { DAY_KEYS, DayView, parsePeriodField, periodAt, PeriodRef, toRequest, toWeek } from './week';

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';

const CONFLICT_KEY = 'error.concurrency.conflict';
const OTHER_CLINIC_KEY = 'error.doctor.period_overlaps_other_clinic';
const REQUIRED_KEY = 'doctors.hours.required';

/** Messages of one displayed period: per input, and for a rule (422) about the whole period. */
interface PeriodErrors {
  start: string[];
  end: string[];
  /** A translation key with its parameters (the other clinic's name for an overlap, D61). */
  rule: { key: string; clinic: string | null } | null;
}

const NO_ERRORS: PeriodErrors = { start: [], end: [], rule: null };

/**
 * The week of one (doctor, clinic) (D61, D62): Saturday to Friday, zero or more periods a day. Saving is a
 * full replace with the assignment's row version. Editing only with doctors.manage in that clinic (UX only;
 * the API decides); otherwise read-only. The list is dynamic, so it is kept in plain signals rather than a
 * Signal Forms tree (D62); every rule beyond "a time is entered" is the server's.
 */
@Component({
  selector: 'cb-working-hours',
  imports: [RouterLink, TranslocoPipe],
  templateUrl: './working-hours.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkingHoursPage {
  private readonly api = inject(DoctorsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly session = inject(SessionService);
  private readonly languages = inject(LanguageService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly doctorId = this.route.snapshot.paramMap.get('id') ?? '';
  protected readonly clinicId = this.route.snapshot.paramMap.get('clinicId') ?? '';
  protected readonly dayKeys = DAY_KEYS;

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorKey = signal<string | null>(null);
  protected readonly doctor = signal<Doctor | null>(null);
  protected readonly hours = signal<WorkingHours | null>(null);
  protected readonly week = signal<DayView[]>([]);
  private nextKey = 1;

  protected readonly errors = signal<Record<number, PeriodErrors>>({});
  protected readonly formErrorKey = signal<string | null>(null);
  protected readonly formErrorClinic = signal<string | null>(null);
  protected readonly correlationId = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly flash = signal<string | null>(null);

  protected readonly conflict = signal(false);
  protected readonly reloading = signal(false);
  /** The week as the user had it when the conflict happened, shown after Reload (D53). */
  protected readonly earlier = signal<DayView[] | null>(null);

  /** UX only (D57): doctors.manage in this clinic. */
  protected readonly canEdit = computed(() => this.session.canIn(ClinicPermissions.DoctorsManage, this.clinicId));

  protected readonly names = computed(() => {
    const doctor = this.doctor();
    if (doctor === null) return null;
    const language = this.languages.language();
    const clinic = doctor.clinics.find((c) => String(c.clinicId) === this.clinicId);
    return {
      doctor: namesFor(doctor.nameAr, doctor.nameEn, language),
      clinic: clinic === undefined ? null : namesFor(clinic.nameAr, clinic.nameEn, language),
    };
  });

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly banner = viewChild<ElementRef<HTMLElement>>('banner');

  constructor() {
    afterNextRender(() => this.heading()?.nativeElement.focus());

    if (/^\d+$/.test(this.doctorId) && /^\d+$/.test(this.clinicId)) {
      this.load();
    } else {
      this.loadState.set('notFound');
    }
  }

  protected load(): void {
    this.loadState.set('loading');
    forkJoin([this.api.get(this.doctorId), this.api.getWorkingHours(this.doctorId, this.clinicId)])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ([doctor, hours]) => {
          this.doctor.set(doctor);
          this.show(hours);
          this.loadState.set('ready');
        },
        error: (error: unknown) => {
          const failure = parseApiError(error);
          this.loadErrorKey.set(this.messages.forError(failure));
          this.loadState.set(failure.status === 404 ? 'notFound' : 'error');
        },
      });
  }

  protected errorsOf(key: number): PeriodErrors {
    return this.errors()[key] ?? NO_ERRORS;
  }

  protected addPeriod(dayOfWeek: number): void {
    const key = this.nextKey++;
    this.week.update((week) =>
      week.map((day) => (day.dayOfWeek === dayOfWeek ? { ...day, periods: [...day.periods, { key, start: '', end: '' }] } : day)),
    );
    this.focus(`#period-${key}-start`);
  }

  protected removePeriod(dayOfWeek: number, key: number): void {
    this.week.update((week) =>
      week.map((day) => (day.dayOfWeek === dayOfWeek ? { ...day, periods: day.periods.filter((p) => p.key !== key) } : day)),
    );
    this.clearErrors(key);
    this.focus(`#add-${dayOfWeek}`);
  }

  protected setTime(dayOfWeek: number, key: number, field: 'start' | 'end', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.week.update((week) =>
      week.map((day) =>
        day.dayOfWeek === dayOfWeek
          ? { ...day, periods: day.periods.map((p) => (p.key === key ? { ...p, [field]: value } : p)) }
          : day,
      ),
    );
    // A change clears what the server said about this period.
    this.clearErrors(key);
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    this.formErrorKey.set(null);
    this.formErrorClinic.set(null);
    this.correlationId.set(null);
    this.flash.set(null);

    // The only client rule: every time is entered. The rest is the server's (D62).
    const missing: Record<number, PeriodErrors> = {};
    for (const period of this.week().flatMap((day) => day.periods)) {
      if (period.start === '' || period.end === '') {
        missing[period.key] = {
          start: period.start === '' ? [REQUIRED_KEY] : [],
          end: period.end === '' ? [REQUIRED_KEY] : [],
          rule: null,
        };
      }
    }
    this.errors.set(missing);
    if (Object.keys(missing).length > 0) {
      this.formErrorKey.set('doctors.hours.invalid');
      this.focusFirstError();
      return;
    }

    const request = toRequest(this.week());
    this.saving.set(true);
    try {
      const saved = await firstValueFrom(
        this.api.replaceWorkingHours(this.doctorId, this.clinicId, {
          periods: request.periods,
          rowVersion: this.hours()?.rowVersion ?? null,
        }),
      );
      this.show(saved);
      this.flash.set('doctors.flash.hours_saved');
    } catch (error: unknown) {
      this.failed(parseApiError(error), request.refs);
    } finally {
      this.saving.set(false);
    }
  }

  protected reload(): void {
    this.reloading.set(true);
    const mine = this.week();
    this.api
      .getWorkingHours(this.doctorId, this.clinicId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (hours) => {
          this.earlier.set(mine);
          this.show(hours);
          this.conflict.set(false);
          this.reloading.set(false);
        },
        error: (error: unknown) => {
          const failure = parseApiError(error);
          this.reloading.set(false);
          if (failure.status === 404) {
            this.loadErrorKey.set(this.messages.forError(failure));
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

  protected messageKey(key: string): string {
    return key === REQUIRED_KEY ? key : this.messages.keyFor(key);
  }

  private show(hours: WorkingHours): void {
    this.hours.set(hours);
    const week = toWeek(hours.periods, this.nextKey);
    this.nextKey += hours.periods.length;
    this.week.set(week);
    this.errors.set({});
  }

  /**
   * Field errors (periods[i].start / .end) go on the matching input; a 422 goes next to the period it names
   * (periodIndex) and names the other clinic for an overlap; anything else is form-level (D62).
   */
  private failed(error: ApiError, refs: readonly PeriodRef[]): void {
    const found: Record<number, PeriodErrors> = {};
    const errorsFor = (key: number) => (found[key] ??= { start: [], end: [], rule: null });

    for (const [name, keys] of Object.entries(error.fieldErrors)) {
      const field = parsePeriodField(name);
      const ref = field === null ? null : periodAt(refs, field.index);
      if (field !== null && ref !== null) {
        // A bad weekday cannot come from this screen; it is shown with the start.
        errorsFor(ref.key)[field.field === 'end' ? 'end' : 'start'].push(...keys);
      }
    }

    const clinic = error.key === OTHER_CLINIC_KEY ? this.clinicName(error.conflictingClinicId) : null;
    const rule = { key: clinic === null ? this.messages.forError(error) : 'doctors.hours.other_clinic', clinic };
    const named = error.status === 422 ? periodAt(refs, error.periodIndex) : null;
    if (named !== null) {
      errorsFor(named.key).rule = rule;
    }

    this.errors.set(found);

    if (error.key === CONFLICT_KEY) {
      this.conflict.set(true);
      afterNextRender(() => this.banner()?.nativeElement.focus(), { injector: this.injector });
    } else if (error.status === 404) {
      this.loadErrorKey.set(this.messages.forError(error));
      this.loadState.set('notFound');
    } else if (Object.keys(found).length === 0) {
      this.formErrorKey.set(rule.key);
      this.formErrorClinic.set(rule.clinic);
      this.correlationId.set(supportReference(error));
    } else {
      this.formErrorKey.set('doctors.hours.invalid');
      this.focusFirstError();
    }
  }

  /** The other clinic's name in the UI language, from the doctor's clinics or the session. */
  private clinicName(clinicId: number | null): string | null {
    if (clinicId === null) return null;
    const language = this.languages.language();
    const own = this.doctor()?.clinics.find((c) => Number(c.clinicId) === clinicId);
    if (own !== undefined) return uiName(own, language);
    const grant = this.session.user()?.clinicPermissions.find((g) => Number(g.clinicId) === clinicId);
    return grant === undefined ? null : uiName({ nameAr: grant.clinicNameAr, nameEn: grant.clinicNameEn }, language);
  }

  private clearErrors(key: number): void {
    if (this.errors()[key] !== undefined) {
      this.errors.update((all) => Object.fromEntries(Object.entries(all).filter(([k]) => Number(k) !== key)));
    }
  }

  private focusFirstError(): void {
    afterNextRender(
      () => (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(),
      { injector: this.injector },
    );
  }

  private focus(selector: string): void {
    afterNextRender(() => (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
