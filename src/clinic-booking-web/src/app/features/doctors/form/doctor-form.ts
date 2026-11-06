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
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { Action } from '../../../shared/ui/action';
import { firstValueFrom } from 'rxjs';
import { DoctorsApi } from '../../../../api/doctors-api';
import { ApiError, parseApiError, supportReference } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { ClinicPermissions } from '../../../core/auth/permissions';
import { SessionService } from '../../../core/auth/session.service';
import { IntlPipe } from '../../../core/format/intl.pipe';
import { LanguageService } from '../../../core/i18n/language.service';
import { ToastService } from '../../../core/ui/toast.service';
import { byUiName, DoctorReferenceData, namesFor, NameView, ReferenceOption } from '../doctor-view';
import { DoctorsSession } from '../doctors-session';
import { SLOT_MINUTES } from '../slot';

/** Mirrors of the API's limits (Doctor.NameMaxLength, DoctorRules.MaxSpecialties and MaxClinics, D61). */
export const NAME_MAX_LENGTH = 100;
export const MAX_SPECIALTIES = 10;
export const MAX_CLINICS = 20;
export const DEFAULT_SLOT_MINUTES = 15;

const MANAGE = ClinicPermissions.DoctorsManage;
const NAME_KEY = /^error\.doctor\.name_(ar|en)_/;
const SPECIALTY_KEY = /^error\.doctor\.(specialties_|specialty_)/;
const CLINIC_KEY = /^error\.doctor\.clinics_/;
const SLOT_KEY = /^error\.doctor\.slot_minutes_/;

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
 * Create a doctor (`/doctors/new`: names, specialties, clinics, slot length; D61, D62). Clinics offered are
 * only those where the user manages doctors, read from the session after a refresh. After a save: the
 * remembered list with a toast (D64; the detail page is gone). Editing has its own pages
 * (`/doctors/:id/edit`, `/doctors/:id/specialties`, `/doctors/:id/clinics`).
 */
@Component({
  selector: 'cb-doctor-form',
  imports: [Action, FormField, RouterLink, TranslocoPipe, IntlPipe],
  templateUrl: './doctor-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DoctorForm {
  private readonly api = inject(DoctorsApi);
  private readonly reference = inject(DoctorReferenceData);
  private readonly router = inject(Router);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(DoctorsSession);
  private readonly toasts = inject(ToastService);
  private readonly session = inject(SessionService);
  private readonly languages = inject(LanguageService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly slotOptions = SLOT_MINUTES;
  protected readonly listQuery = this.memory.lastQuery;

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
      if (value().length === 0) return requiredError({ message: 'error.doctor.clinics_required' });
      return value().length > MAX_CLINICS ? { kind: 'max', message: 'error.doctor.clinics_too_many' } : undefined;
    });
  });

  /** The session is refreshed first, so a grant made after sign-in is offered at once (D62). */
  protected readonly ready = signal(false);
  protected readonly formErrorKey = signal<string | null>(null);
  protected readonly correlationId = signal<string | null>(null);

  // ---- options
  private readonly specialtyList = signal<ReferenceOption[]>([]);
  protected readonly specialtiesIncomplete = signal(false);
  protected readonly specialtiesFailed = signal(false);

  protected readonly specialtyOptions = computed<OptionView[]>(() => {
    const language = this.languages.language();
    return byUiName(this.specialtyList(), language).map((option) => ({
      id: option.id,
      name: namesFor(option.nameAr, option.nameEn, language).primary,
    }));
  });

  /** Clinics where the user manages doctors (D62). */
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

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly host = inject(ElementRef<HTMLElement>);

  constructor() {
    afterNextRender(() => this.heading()?.nativeElement.focus());
    this.loadSpecialties();
    void this.session.refreshUser().finally(() => this.ready.set(true));
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

  protected messageKey(message: string | undefined): string {
    return this.messages.keyFor(message ?? '');
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

  private async save(): Promise<TreeValidationResult> {
    const { nameAr, nameEn, specialtyIds, clinicIds, slotMinutes } = this.model();
    try {
      await firstValueFrom(
        this.api.create({
          nameAr: nameAr.trim(),
          nameEn: nameEn.trim(),
          specialtyIds: specialtyIds.map(Number),
          clinicIds: clinicIds.map(Number),
          slotMinutes: Number(slotMinutes),
        }),
      );
    } catch (error: unknown) {
      return this.failed(parseApiError(error));
    }

    this.toasts.showToast('doctors.flash.created');
    await this.router.navigate(['/doctors'], { queryParams: this.memory.lastQuery() });
    return undefined;
  }

  /** Field keys on their fields (specialty_unavailable on the specialties), else form-level (403 included). */
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

    if (errors.length === 0) {
      // 403 (a clinic not managed any more) and everything unexplained: form-level (D62).
      this.formErrorKey.set(this.messages.forError(error));
      this.correlationId.set(supportReference(error));
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
