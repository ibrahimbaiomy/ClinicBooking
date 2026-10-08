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
import { Patient, PatientsApi } from '../../../../api/patients-api';
import { ApiError, parseApiError, PhoneMatch, supportReference } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { Permissions } from '../../../core/auth/permissions';
import { SessionService } from '../../../core/auth/session.service';
import { IntlPipe } from '../../../core/format/intl.pipe';
import { formatPhone } from '../../../core/format/phone';
import { PhonePipe } from '../../../core/format/phone.pipe';
import { PatientsSession } from '../patients-session';

/** Mirror of Patient.NameMaxLength on the API. */
export const NAME_MAX_LENGTH = 100;

/** The only client-side phone rule besides "required" (PhoneNumber.MaxInputLength); the rest is the server's. */
export const PHONE_MAX_INPUT_LENGTH = 32;

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';
type Field = 'name' | 'phone';

interface PatientFields {
  name: string;
  /** What the user sees and types: the local display form when editing (formatPhone), never E.164 by force. */
  phone: string;
}

/** The duplicate-phone warning of the last save (D63), tied to the phone text it was about. */
interface DuplicateWarning {
  phone: string;
  count: number;
  matches: PhoneMatch[];
}

const CONFLICT_KEY = 'error.concurrency.conflict';
const PHONE_EXISTS_KEY = 'error.patient.phone_exists';
const NAME_KEY = /^error\.patient\.name_/;
const PHONE_KEY = /^error\.patient\.phone_(required|invalid)$/;
const FIELDS: readonly Field[] = ['name', 'phone'];

/**
 * Create and edit a patient (D63), following the Clinics form (D53, D56): the conflict flow, the untouched-phone
 * guard. A phone other patients have is a warning: a focused panel lists the matches (or only their count for a
 * user who may not read patients) with "Save anyway", which resends with confirmDuplicatePhone.
 */
@Component({
  selector: 'cb-patient-form',
  imports: [FormField, RouterLink, TranslocoPipe, IntlPipe, PhonePipe],
  templateUrl: './patient-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PatientForm {
  private readonly api = inject(PatientsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(PatientsSession);
  private readonly session = inject(SessionService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  private readonly id = this.route.snapshot.paramMap.get('id');
  protected readonly isNew = this.id === null;
  protected readonly listQuery = this.memory.lastQuery;
  /** Where Cancel and Save lead: the list, for a user who may read it (D63). */
  protected readonly canRead = computed(() => this.session.can(Permissions.PatientsRead));

  protected readonly model = signal<PatientFields>({ name: '', phone: '' });
  protected readonly patientForm = form(this.model, (path) => {
    validate(path.name, ({ value }) => {
      const trimmed = value().trim();
      if (trimmed === '') return requiredError({ message: 'error.patient.name_required' });
      return trimmed.length > NAME_MAX_LENGTH
        ? maxLengthError(NAME_MAX_LENGTH, { message: 'error.patient.name_too_long' })
        : undefined;
    });
    validate(path.phone, ({ value }) => {
      if (value().trim() === '') return requiredError({ message: 'error.patient.phone_required' });
      return value().length > PHONE_MAX_INPUT_LENGTH
        ? maxLengthError(PHONE_MAX_INPUT_LENGTH, { message: 'error.patient.phone_invalid' })
        : undefined;
    });
  });

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorKey = signal<string | null>(null);
  protected readonly record = signal<Patient | null>(null);

  protected readonly formErrorKey = signal<string | null>(null);
  protected readonly correlationId = signal<string | null>(null);
  protected readonly flash = signal<string | null>(null);

  protected readonly conflict = signal(false);
  protected readonly reloading = signal(false);
  protected readonly earlier = signal<PatientFields | null>(null);

  private readonly warning = signal<DuplicateWarning | null>(null);
  /** The panel is about one phone: typing another one hides it (D63). */
  protected readonly duplicate = computed(() => {
    const warning = this.warning();
    return warning !== null && warning.phone === this.model().phone ? warning : null;
  });
  /** Matches the server did not list (it lists five at most). */
  protected readonly moreMatches = computed(() => {
    const duplicate = this.duplicate();
    return duplicate === null ? 0 : Math.max(0, duplicate.count - duplicate.matches.length);
  });
  protected readonly savingAnyway = signal(false);

  /** The untouched-phone guard (D56): the stored E.164 value and the text shown for it. */
  private storedPhone: string | null = null;
  private shownPhone = '';

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly banner = viewChild<ElementRef<HTMLElement>>('banner');
  private readonly duplicatePanel = viewChild<ElementRef<HTMLElement>>('duplicatePanel');

  constructor() {
    this.flash.set(this.memory.takeFlash());
    afterNextRender(() => this.heading()?.nativeElement.focus());

    if (this.isNew) {
      this.loadState.set('ready');
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
          this.loadState.set('ready');
        },
        error: (error: unknown) => {
          const failure = parseApiError(error);
          this.loadErrorKey.set(this.messages.forError(failure));
          this.loadState.set(failure.status === 404 ? 'notFound' : 'error');
        },
      });
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    await this.send(false);
  }

  /** "Save anyway": the same values, confirmed (D44, D63). */
  protected async saveAnyway(): Promise<void> {
    this.savingAnyway.set(true);
    try {
      await this.send(true);
    } finally {
      this.savingAnyway.set(false);
    }
  }

  protected cancelDuplicate(): void {
    this.warning.set(null);
    afterNextRender(() => this.patientForm.phone().focusBoundControl(), { injector: this.injector });
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

  private async send(confirmDuplicatePhone: boolean): Promise<void> {
    this.formErrorKey.set(null);
    this.correlationId.set(null);
    this.flash.set(null);
    if (!confirmDuplicatePhone) {
      this.warning.set(null);
    }

    await submit(this.patientForm, {
      action: () => this.save(confirmDuplicatePhone),
      onInvalid: () => {
        const first = FIELDS.find((field) => this.patientForm[field]().invalid()) ?? 'name';
        this.patientForm[first]().focusBoundControl();
      },
    });
  }

  private show(record: Patient): void {
    this.record.set(record);
    this.storedPhone = record.phone;
    this.shownPhone = formatPhone(record.phone);
    this.model.set({ name: record.name, phone: this.shownPhone });
  }

  /** Untouched: the stored value, exactly; touched: exactly what was typed (D56). */
  private payload(): { name: string; phone: string } {
    const { name, phone } = this.model();
    return {
      name: name.trim(),
      phone: !this.isNew && phone === this.shownPhone && this.storedPhone !== null ? this.storedPhone : phone,
    };
  }

  private async save(confirmDuplicatePhone: boolean): Promise<TreeValidationResult> {
    const body = { ...this.payload(), confirmDuplicatePhone: confirmDuplicatePhone || null };

    try {
      if (this.isNew) {
        await firstValueFrom(this.api.create(body));
      } else {
        await firstValueFrom(this.api.update(this.id as string, { ...body, rowVersion: this.record()?.rowVersion ?? null }));
      }
    } catch (error: unknown) {
      return this.failed(parseApiError(error));
    }

    this.warning.set(null);
    this.memory.setFlash(this.isNew ? 'patients.flash.saved_created' : 'patients.flash.saved_updated');
    if (this.canRead()) {
      await this.router.navigate(['/patients'], { queryParams: this.memory.lastQuery() });
    } else {
      // Without patients.read there is no list to return to: a fresh form with the message (D63).
      this.flash.set(this.memory.takeFlash());
      this.model.set({ name: '', phone: '' });
      this.patientForm().reset();
      afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
    }
    return undefined;
  }

  private failed(error: ApiError): TreeValidationResult {
    if (error.key === PHONE_EXISTS_KEY) {
      this.warning.set({ phone: this.model().phone, count: error.matchCount ?? error.matches.length, matches: error.matches });
      afterNextRender(() => this.duplicatePanel()?.nativeElement.focus(), { injector: this.injector });
      return undefined;
    }

    const errors: ValidationError.WithFieldTree[] = [];
    const place = (field: Field, message: string) => errors.push({ fieldTree: this.patientForm[field], kind: 'server', message });

    for (const [name, keys] of Object.entries(error.fieldErrors)) {
      const field = FIELDS.find((candidate) => candidate === name);
      if (field !== undefined) {
        keys.forEach((key) => place(field, key));
      }
    }
    if (errors.length === 0) {
      if (NAME_KEY.test(error.key)) place('name', error.key);
      else if (PHONE_KEY.test(error.key)) place('phone', error.key);
    }

    if (error.key === CONFLICT_KEY) {
      this.conflict.set(true);
      afterNextRender(() => this.banner()?.nativeElement.focus(), { injector: this.injector });
    } else if (error.status === 404) {
      this.loadErrorKey.set(this.messages.forError(error));
      this.loadState.set('notFound');
    } else if (errors.length === 0) {
      this.formErrorKey.set(this.messages.forError(error));
      this.correlationId.set(supportReference(error));
    }

    return errors;
  }
}
