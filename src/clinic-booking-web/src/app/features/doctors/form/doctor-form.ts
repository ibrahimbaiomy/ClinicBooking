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
import {
  form,
  FormField,
  maxLengthError,
  requiredError,
  submit,
  TreeValidationResult,
  validate,
  ValidationError,
} from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { Doctor, DoctorsApi } from '../../../../api/doctors-api';
import { ApiError, parseApiError } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { ClinicPermissions } from '../../../core/auth/permissions';
import { SessionService } from '../../../core/auth/session.service';
import { IntlPipe } from '../../../core/format/intl.pipe';
import { LanguageService } from '../../../core/i18n/language.service';
import { byUiName, DoctorReferenceData, namesFor, NameView, ReferenceOption } from '../doctor-view';
import { DoctorsSession } from '../doctors-session';
import { SLOT_MINUTES } from '../slot';

/** Mirrors of the API's limits (Doctor.NameMaxLength, DoctorRules.MaxSpecialties and MaxClinics, D61). */
export const NAME_MAX_LENGTH = 100;
export const MAX_SPECIALTIES = 10;
export const MAX_CLINICS = 20;
export const DEFAULT_SLOT_MINUTES = 15;

const MANAGE = ClinicPermissions.DoctorsManage;
const CONFLICT_KEY = 'error.concurrency.conflict';
const NAME_KEY = /^error\.doctor\.name_(ar|en)_/;
const SPECIALTY_KEY = /^error\.doctor\.(specialties_|specialty_)/;
const CLINIC_KEY = /^error\.doctor\.clinics_/;
const SLOT_KEY = /^error\.doctor\.slot_minutes_/;

type LoadState = 'loading' | 'ready' | 'notFound' | 'notAllowed' | 'error';
type Field = 'nameAr' | 'nameEn' | 'specialtyIds' | 'clinicIds' | 'slotMinutes';

interface DoctorFields {
  nameAr: string;
  nameEn: string;
  specialtyIds: string[];
  clinicIds: string[];
  slotMinutes: string;
}

interface OptionView {
  id: string;
  name: NameView;
}

function nameProblem(raw: string, language: 'ar' | 'en') {
  const trimmed = raw.trim();
  if (trimmed === '') return requiredError({ message: `error.doctor.name_${language}_required` });
  if (trimmed.length > NAME_MAX_LENGTH) {
    return maxLengthError(NAME_MAX_LENGTH, { message: `error.doctor.name_${language}_too_long` });
  }
  return undefined;
}

/**
 * Create (`/doctors/new`: names, specialties, clinics, slot length) and edit (`/doctors/:id/edit`: names and
 * specialties, a full replace with the row version, D61). Clinics offered on create are only those where the
 * user manages doctors, read from the session after a refresh (D62).
 */
@Component({
  selector: 'cb-doctor-form',
  imports: [FormField, RouterLink, TranslocoPipe, IntlPipe],
  templateUrl: './doctor-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DoctorForm {
  private readonly api = inject(DoctorsApi);
  private readonly reference = inject(DoctorReferenceData);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(DoctorsSession);
  private readonly session = inject(SessionService);
  private readonly languages = inject(LanguageService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  private readonly id = this.route.snapshot.paramMap.get('id');
  protected readonly isNew = this.id === null;
  protected readonly doctorId = this.id ?? '';
  protected readonly slotOptions = SLOT_MINUTES;

  protected readonly model = signal<DoctorFields>({
    nameAr: '',
    nameEn: '',
    specialtyIds: [],
    clinicIds: [],
    slotMinutes: String(DEFAULT_SLOT_MINUTES),
  });

  protected readonly doctorForm = form(this.model, (path) => {
    validate(path.nameAr, ({ value }) => nameProblem(value(), 'ar'));
    validate(path.nameEn, ({ value }) => nameProblem(value(), 'en'));
    validate(path.specialtyIds, ({ value }) => {
      if (value().length === 0) return requiredError({ message: 'error.doctor.specialties_required' });
      return value().length > MAX_SPECIALTIES ? { kind: 'max', message: 'error.doctor.specialties_too_many' } : undefined;
    });
    validate(path.clinicIds, ({ value }) => {
      if (!this.isNew) return undefined;
      if (value().length === 0) return requiredError({ message: 'error.doctor.clinics_required' });
      return value().length > MAX_CLINICS ? { kind: 'max', message: 'error.doctor.clinics_too_many' } : undefined;
    });
  });

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorKey = signal<string | null>(null);
  protected readonly record = signal<Doctor | null>(null);

  protected readonly formErrorKey = signal<string | null>(null);
  protected readonly correlationId = signal<string | null>(null);
  protected readonly conflict = signal(false);
  protected readonly reloading = signal(false);
  protected readonly earlier = signal<DoctorFields | null>(null);

  // ---- options
  private readonly specialtyList = signal<ReferenceOption[]>([]);
  protected readonly specialtiesIncomplete = signal(false);
  protected readonly specialtiesFailed = signal(false);

  /** The loaded specialties, plus the doctor's own (they may lie beyond the first 100), by UI-language name. */
  protected readonly specialtyOptions = computed<OptionView[]>(() => {
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

  /** Create only: clinics where the user manages doctors (D62). */
  protected readonly clinicOptions = computed<OptionView[]>(() => {
    const language = this.languages.language();
    const grants = (this.session.user()?.clinicPermissions ?? [])
      .filter((grant) => grant.permissions.includes(MANAGE))
      .map((grant) => ({ id: String(grant.clinicId), nameAr: grant.clinicNameAr, nameEn: grant.clinicNameEn }));
    return byUiName(grants, language).map((option) => ({
      id: option.id,
      name: namesFor(option.nameAr, option.nameEn, language).primary,
    }));
  });

  /** The earlier entries after a conflict reload, with specialty names instead of ids. */
  protected readonly earlierSpecialties = computed(() => {
    const ids = new Set(this.earlier()?.specialtyIds ?? []);
    return this.specialtyOptions().filter((option) => ids.has(option.id));
  });

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly banner = viewChild<ElementRef<HTMLElement>>('banner');
  private readonly host = inject(ElementRef<HTMLElement>);

  constructor() {
    afterNextRender(() => this.heading()?.nativeElement.focus());
    this.loadSpecialties();

    if (this.isNew) {
      void this.openCreate();
    } else if (this.id !== null && /^\d+$/.test(this.id)) {
      this.load();
    } else {
      this.loadState.set('notFound');
    }
  }

  protected load(): void {
    this.loadState.set('loading');
    this.api
      .get(this.id as string)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (record) => {
          this.show(record);
          this.loadState.set(this.mayEdit(record) ? 'ready' : 'notAllowed');
        },
        error: (error: unknown) => {
          const failure = parseApiError(error);
          this.loadErrorKey.set(this.messages.forError(failure));
          this.loadState.set(failure.status === 404 ? 'notFound' : 'error');
        },
      });
  }

  protected isChecked(field: 'specialtyIds' | 'clinicIds', id: string): boolean {
    return this.model()[field].includes(id);
  }

  protected toggle(field: 'specialtyIds' | 'clinicIds', id: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.model.update((fields) => ({
      ...fields,
      [field]: checked ? [...fields[field].filter((x) => x !== id), id] : fields[field].filter((x) => x !== id),
    }));
    this.doctorForm[field]().markAsTouched();
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.formErrorKey.set(null);
    this.correlationId.set(null);

    await submit(this.doctorForm, {
      action: () => this.save(),
      onInvalid: () => this.focusFirstInvalid(),
    });
  }

  protected reload(): void {
    this.reloading.set(true);
    const mine = this.model();
    this.api
      .get(this.id as string)
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

  protected messageKey(message: string | undefined): string {
    return this.messages.keyFor(message ?? '');
  }

  /** Create: the session is refreshed first, so a grant made after sign-in is offered at once (D62). */
  private async openCreate(): Promise<void> {
    await this.session.refreshUser();
    this.loadState.set('ready');
  }

  private loadSpecialties(): void {
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
  }

  /** UX only: doctors.manage in any of the doctor's clinics (D61). */
  private mayEdit(record: Doctor): boolean {
    return record.clinics.some((clinic) => this.session.canIn(MANAGE, clinic.clinicId));
  }

  private show(record: Doctor): void {
    this.record.set(record);
    this.model.set({
      nameAr: record.nameAr,
      nameEn: record.nameEn,
      specialtyIds: record.specialties.map((s) => String(s.id)),
      clinicIds: [],
      slotMinutes: String(record.slotMinutes),
    });
  }

  private async save(): Promise<TreeValidationResult> {
    const { nameAr, nameEn, specialtyIds, clinicIds, slotMinutes } = this.model();
    const names = { nameAr: nameAr.trim(), nameEn: nameEn.trim(), specialtyIds: specialtyIds.map(Number) };
    let saved: Doctor;

    try {
      saved = this.isNew
        ? await firstValueFrom(this.api.create({ ...names, clinicIds: clinicIds.map(Number), slotMinutes: Number(slotMinutes) }))
        : await firstValueFrom(this.api.update(this.id as string, { ...names, rowVersion: this.record()?.rowVersion ?? null }));
    } catch (error: unknown) {
      return this.failed(parseApiError(error));
    }

    this.memory.setFlash(this.isNew ? 'doctors.flash.created' : 'doctors.flash.updated');
    await this.router.navigate(['/doctors', String(saved.id)]);
    return undefined;
  }

  /** Field keys on their fields (specialty_unavailable on the specialties), 409 conflict banner, else form-level. */
  private failed(error: ApiError): TreeValidationResult {
    const errors: ValidationError.WithFieldTree[] = [];
    const fields: readonly Field[] = ['nameAr', 'nameEn', 'specialtyIds', 'clinicIds', 'slotMinutes'];
    const place = (field: Field, message: string) => errors.push({ fieldTree: this.doctorForm[field], kind: 'server', message });

    for (const [name, keys] of Object.entries(error.fieldErrors)) {
      const field = fields.find((candidate) => candidate === name);
      if (field !== undefined) {
        keys.forEach((key) => place(field, key));
      }
    }

    if (errors.length === 0) {
      const named = NAME_KEY.exec(error.key);
      if (named !== null) place(named[1] === 'ar' ? 'nameAr' : 'nameEn', error.key);
      else if (SPECIALTY_KEY.test(error.key)) place('specialtyIds', error.key);
      else if (CLINIC_KEY.test(error.key)) place('clinicIds', error.key);
      else if (SLOT_KEY.test(error.key)) place('slotMinutes', error.key);
    }

    if (error.key === CONFLICT_KEY) {
      this.conflict.set(true);
      afterNextRender(() => this.banner()?.nativeElement.focus(), { injector: this.injector });
    } else if (error.status === 404 && !this.isNew) {
      this.loadErrorKey.set(this.messages.forError(error));
      this.loadState.set('notFound');
    } else if (errors.length === 0) {
      // 403 (a clinic not managed any more) and everything unexplained: form-level (D62).
      this.formErrorKey.set(this.messages.forError(error));
      this.correlationId.set(error.correlationId);
    } else {
      afterNextRender(() => this.focusFirstInvalid(), { injector: this.injector });
    }

    return errors;
  }

  /** Text inputs through the form; a checkbox group by its first box. */
  private focusFirstInvalid(): void {
    const order: readonly Field[] = ['nameAr', 'nameEn', 'specialtyIds', 'clinicIds', 'slotMinutes'];
    const first = order.find((field) => this.doctorForm[field]().invalid());
    if (first === 'specialtyIds' || first === 'clinicIds') {
      const prefix = first === 'specialtyIds' ? 'doctor-specialty-' : 'doctor-clinic-';
      (this.host.nativeElement as HTMLElement).querySelector<HTMLInputElement>(`input[id^="${prefix}"]`)?.focus();
    } else {
      this.doctorForm[first ?? 'nameAr']().focusBoundControl();
    }
  }
}
