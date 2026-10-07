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
import { Clinic, ClinicsApi } from '../../../../api/clinics-api';
import { ApiError, parseApiError, supportReference } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { IntlPipe } from '../../../core/format/intl.pipe';
import { formatPhone } from '../../../core/format/phone';
import { ClinicsSession } from '../clinics-session';

/** Mirror of Clinic.NameMaxLength and Clinic.AddressMaxLength on the API. */
export const NAME_MAX_LENGTH = 100;
export const ADDRESS_MAX_LENGTH = 300;

/**
 * The only client-side phone rule: the API rejects raw input longer than this before it parses anything
 * (PhoneNumber.MaxInputLength). Everything else about a phone number is the server's decision
 * (error.clinic.phone_invalid, D56).
 */
export const PHONE_MAX_INPUT_LENGTH = 32;

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';
type Field = 'nameAr' | 'nameEn' | 'address' | 'phone';

interface ClinicFields {
  nameAr: string;
  nameEn: string;
  address: string;
  /** What the user sees and types: the local display form when editing (formatPhone), never E.164 by force. */
  phone: string;
}

const CONFLICT_KEY = 'error.concurrency.conflict';
const NAME_KEY = /^error\.clinic\.name_(ar|en)_/;
const ADDRESS_TOO_LONG_KEY = 'error.clinic.address_too_long';
const PHONE_INVALID_KEY = 'error.clinic.phone_invalid';
const FIELDS: readonly Field[] = ['nameAr', 'nameEn', 'address', 'phone'];

/** The client-side name rules, with the back-end keys (required, at most 100 after trimming). */
function nameProblem(raw: string, language: 'ar' | 'en') {
  const trimmed = raw.trim();
  if (trimmed === '') {
    return requiredError({ message: `error.clinic.name_${language}_required` });
  }
  if (trimmed.length > NAME_MAX_LENGTH) {
    return maxLengthError(NAME_MAX_LENGTH, { message: `error.clinic.name_${language}_too_long` });
  }
  return undefined;
}

const blankToNull = (text: string): string | null => (text.trim() === '' ? null : text);

/** Create and edit share one page: `/clinics/new` and `/clinics/:id/edit`. */
@Component({
  selector: 'cb-clinic-form',
  imports: [FormField, RouterLink, TranslocoPipe, IntlPipe],
  templateUrl: './clinic-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClinicForm {
  private readonly api = inject(ClinicsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(ClinicsSession);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  private readonly id = this.route.snapshot.paramMap.get('id');
  protected readonly isNew = this.id === null;
  protected readonly listQuery = this.memory.lastQuery;
  protected readonly addressMax = ADDRESS_MAX_LENGTH;

  protected readonly model = signal<ClinicFields>({ nameAr: '', nameEn: '', address: '', phone: '' });
  protected readonly clinicForm = form(this.model, (path) => {
    validate(path.nameAr, ({ value }) => nameProblem(value(), 'ar'));
    validate(path.nameEn, ({ value }) => nameProblem(value(), 'en'));
    validate(path.address, ({ value }) =>
      value().trim().length > ADDRESS_MAX_LENGTH
        ? maxLengthError(ADDRESS_MAX_LENGTH, { message: ADDRESS_TOO_LONG_KEY })
        : undefined,
    );
    validate(path.phone, ({ value }) =>
      value().length > PHONE_MAX_INPUT_LENGTH
        ? maxLengthError(PHONE_MAX_INPUT_LENGTH, { message: PHONE_INVALID_KEY })
        : undefined,
    );
  });

  /** The counter agrees with the rule: it counts the trimmed text. */
  protected readonly addressLength = computed(() => this.model().address.trim().length);

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorKey = signal<string | null>(null);
  protected readonly record = signal<Clinic | null>(null);

  protected readonly formErrorKey = signal<string | null>(null);
  protected readonly correlationId = signal<string | null>(null);

  /** Someone else changed the record: Save stays disabled until the user reloads (D53). */
  protected readonly conflict = signal(false);
  protected readonly reloading = signal(false);
  /** What the user had typed when the conflict happened, shown after Reload so it can be re-applied. */
  protected readonly earlier = signal<ClinicFields | null>(null);

  /**
   * The untouched-phone guard (D56): the stored E.164 value as loaded, and the text shown for it. If the
   * phone field still holds exactly that text on save, the stored value is sent back as it is, so a user
   * who does not touch the field cannot change it, whatever the server rules become.
   */
  private storedPhone: string | null = null;
  private shownPhone = '';

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly banner = viewChild<ElementRef<HTMLElement>>('banner');

  constructor() {
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
    this.formErrorKey.set(null);
    this.correlationId.set(null);

    await submit(this.clinicForm, {
      action: () => this.save(),
      onInvalid: () => {
        const first = FIELDS.find((field) => this.clinicForm[field]().invalid()) ?? 'nameAr';
        this.clinicForm[first]().focusBoundControl();
      },
    });
  }

  /** The user has seen the banner and chose to reload: fetch the latest and keep their entries visible. */
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

  /** A server message that is not a known key is replaced by error.unexpected (never raw text). */
  protected messageKey(message: string | undefined): string {
    return this.messages.keyFor(message ?? '');
  }

  private show(record: Clinic): void {
    this.record.set(record);
    this.storedPhone = record.phone;
    this.shownPhone = formatPhone(record.phone);
    this.model.set({
      nameAr: record.nameAr,
      nameEn: record.nameEn,
      address: record.address ?? '',
      phone: this.shownPhone,
    });
  }

  /** What goes to the API. Blank address and phone are sent as null: the PUT is a full replace (D55). */
  private payload(): { nameAr: string; nameEn: string; address: string | null; phone: string | null } {
    const { nameAr, nameEn, address, phone } = this.model();
    return {
      nameAr: nameAr.trim(),
      nameEn: nameEn.trim(),
      address: blankToNull(address.trim()),
      // Untouched: the stored value, exactly. Touched (even to the same digits in another form): exactly what was typed.
      phone: !this.isNew && phone === this.shownPhone ? this.storedPhone : blankToNull(phone),
    };
  }

  private async save(): Promise<TreeValidationResult> {
    const body = this.payload();

    try {
      if (this.isNew) {
        await firstValueFrom(this.api.create(body));
      } else {
        const rowVersion = this.record()?.rowVersion ?? null;
        await firstValueFrom(this.api.update(this.id as string, { ...body, rowVersion }));
      }
    } catch (error: unknown) {
      return this.failed(parseApiError(error));
    }

    this.memory.setFlash(this.isNew ? 'clinics.flash.saved_created' : 'clinics.flash.saved_updated');
    await this.router.navigate(['/clinics'], { queryParams: this.memory.lastQuery() });
    return undefined;
  }

  /** Maps a failed save onto the right field, the conflict banner, or a form-level message. */
  private failed(error: ApiError): TreeValidationResult {
    const errors: ValidationError.WithFieldTree[] = [];
    const place = (field: Field, message: string) =>
      errors.push({ fieldTree: this.clinicForm[field], kind: 'server', message });

    // 400: errors keyed by camelCase field name, each message a key.
    for (const [name, keys] of Object.entries(error.fieldErrors)) {
      const field = FIELDS.find((candidate) => candidate === name);
      if (field !== undefined) {
        keys.forEach((key) => place(field, key));
      }
    }

    // A 409 name taken, or an error key that names its field, tells us the field by the key itself.
    if (errors.length === 0) {
      const named = NAME_KEY.exec(error.key);
      if (named !== null) {
        place(named[1] === 'ar' ? 'nameAr' : 'nameEn', error.key);
      } else if (error.key === PHONE_INVALID_KEY) {
        place('phone', error.key);
      } else if (error.key === ADDRESS_TOO_LONG_KEY) {
        place('address', error.key);
      }
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
