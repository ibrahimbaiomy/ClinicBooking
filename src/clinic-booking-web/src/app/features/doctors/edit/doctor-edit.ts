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
import { Action } from '../../../shared/ui/action';
import { firstValueFrom } from 'rxjs';
import { Doctor, DoctorsApi } from '../../../../api/doctors-api';
import { ApiError, parseApiError, supportReference } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { SessionService } from '../../../core/auth/session.service';
import { IntlPipe } from '../../../core/format/intl.pipe';
import { ToastService } from '../../../core/ui/toast.service';
import { managesAnyClinicOf } from '../doctor-view';
import { DoctorsSession } from '../doctors-session';
import { cairoTomorrow, SLOT_MINUTES } from '../slot';

/** Mirror of Doctor.NameMaxLength (D61). */
export const NAME_MAX_LENGTH = 100;

const CONFLICT_KEY = 'error.concurrency.conflict';
const NAME_KEY = /^error\.doctor\.name_(ar|en)_/;
const NOT_FUTURE_KEY = 'error.doctor.effective_from_not_future';

type LoadState = 'loading' | 'ready' | 'notFound' | 'notAllowed' | 'error';

interface NameFields {
  nameAr: string;
  nameEn: string;
}

interface SlotFields {
  slotMinutes: string;
  effectiveFrom: string;
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
 * `/doctors/:id/edit` (D64): the Arabic and English names, saved through the doctor PUT with its row
 * version and the doctor's current specialties unchanged (the D53 conflict flow), and a separate
 * slot-duration section (current length, the pending change, and a change form from tomorrow in Cairo
 * that replaces the pending one). The two saves are independent requests with their own errors; each
 * returns to the remembered list with a toast. Allowed with doctors.manage in any of the doctor's clinics.
 */
@Component({
  selector: 'cb-doctor-edit',
  imports: [Action, FormField, RouterLink, TranslocoPipe, IntlPipe],
  templateUrl: './doctor-edit.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DoctorEditPage {
  private readonly api = inject(DoctorsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(DoctorsSession);
  private readonly session = inject(SessionService);
  private readonly toasts = inject(ToastService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly id = this.route.snapshot.paramMap.get('id') ?? '';
  protected readonly listQuery = this.memory.lastQuery;
  protected readonly slotOptions = SLOT_MINUTES;

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorKey = signal<string | null>(null);
  protected readonly record = signal<Doctor | null>(null);

  // ---- names
  protected readonly model = signal<NameFields>({ nameAr: '', nameEn: '' });
  protected readonly namesForm = form(this.model, (path) => {
    validate(path.nameAr, ({ value }) => nameProblem(value(), 'ar'));
    validate(path.nameEn, ({ value }) => nameProblem(value(), 'en'));
  });
  protected readonly formErrorKey = signal<string | null>(null);
  protected readonly correlationId = signal<string | null>(null);
  protected readonly conflict = signal(false);
  protected readonly reloading = signal(false);
  protected readonly earlier = signal<NameFields | null>(null);

  // ---- slot duration
  protected readonly minDate = signal(cairoTomorrow());
  protected readonly slotModel = signal<SlotFields>({ slotMinutes: '15', effectiveFrom: '' });
  protected readonly slotForm = form(this.slotModel, (path) => {
    validate(path.effectiveFrom, ({ value }) => {
      const date = value();
      if (date === '') return requiredError({ message: 'error.doctor.effective_from_required' });
      return date < this.minDate() ? { kind: 'min', message: NOT_FUTURE_KEY } : undefined;
    });
  });
  protected readonly slotErrorKey = signal<string | null>(null);

  /** Today's length and the pending change, as numbers (int32 may arrive as a string). */
  protected readonly slot = computed(() => {
    const record = this.record();
    const pending = record?.pendingSlotChange ?? null;
    return {
      current: Number(record?.slotMinutes ?? 0),
      pending: pending === null ? null : { slotMinutes: Number(pending.slotMinutes), effectiveFrom: pending.effectiveFrom },
    };
  });

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly banner = viewChild<ElementRef<HTMLElement>>('banner');

  constructor() {
    afterNextRender(() => this.heading()?.nativeElement.focus());

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
          this.slotModel.set({ slotMinutes: String(record.slotMinutes), effectiveFrom: '' });
          this.loadState.set(managesAnyClinicOf(this.session, record.clinics) ? 'ready' : 'notAllowed');
        },
        error: (error: unknown) => {
          const failure = parseApiError(error);
          this.loadErrorKey.set(this.messages.forError(failure));
          this.loadState.set(failure.status === 404 ? 'notFound' : 'error');
        },
      });
  }

  protected messageKey(message: string | undefined): string {
    return this.messages.keyFor(message ?? '');
  }

  // ---- names

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.formErrorKey.set(null);
    this.correlationId.set(null);
    await submit(this.namesForm, {
      action: () => this.saveNames(),
      onInvalid: () => this.focusFirstInvalidName(),
    });
  }

  protected reload(): void {
    this.reloading.set(true);
    const mine = this.model();
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

  private async saveNames(): Promise<TreeValidationResult> {
    const record = this.record();
    const { nameAr, nameEn } = this.model();
    try {
      await firstValueFrom(
        this.api.update(this.id, {
          nameAr: nameAr.trim(),
          nameEn: nameEn.trim(),
          // The specialties have their own page: sent back unchanged (the PUT is a full replace, D61).
          specialtyIds: (record?.specialties ?? []).map((s) => Number(s.id)),
          rowVersion: record?.rowVersion ?? null,
        }),
      );
    } catch (error: unknown) {
      return this.namesFailed(parseApiError(error));
    }

    this.toasts.showToast('doctors.flash.updated');
    await this.router.navigate(['/doctors'], { queryParams: this.memory.lastQuery() });
    return undefined;
  }

  /** Name keys on their fields, 409 conflict banner, 404 "not found", anything else form-level (D53). */
  private namesFailed(error: ApiError): TreeValidationResult {
    const errors: ValidationError.WithFieldTree[] = [];
    const place = (field: 'nameAr' | 'nameEn', message: string) =>
      errors.push({ fieldTree: this.namesForm[field], kind: 'server', message });

    for (const [name, keys] of Object.entries(error.fieldErrors)) {
      if (name === 'nameAr' || name === 'nameEn') keys.forEach((key) => place(name, key));
    }
    const named = errors.length === 0 ? NAME_KEY.exec(error.key) : null;
    if (named !== null) place(named[1] === 'ar' ? 'nameAr' : 'nameEn', error.key);

    if (error.key === CONFLICT_KEY) {
      this.conflict.set(true);
      afterNextRender(() => this.banner()?.nativeElement.focus(), { injector: this.injector });
    } else if (error.status === 404) {
      this.loadErrorKey.set(this.messages.forError(error));
      this.loadState.set('notFound');
    } else if (errors.length === 0) {
      this.formErrorKey.set(this.messages.forError(error));
      this.correlationId.set(supportReference(error));
    } else {
      afterNextRender(() => this.focusFirstInvalidName(), { injector: this.injector });
    }
    return errors;
  }

  private focusFirstInvalidName(): void {
    (this.namesForm.nameAr().invalid() ? this.namesForm.nameAr() : this.namesForm.nameEn()).focusBoundControl();
  }

  // ---- slot duration

  protected async onSlotSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.slotErrorKey.set(null);
    this.minDate.set(cairoTomorrow());
    await submit(this.slotForm, {
      action: () => this.saveSlot(),
      onInvalid: () => this.slotForm.effectiveFrom().focusBoundControl(),
    });
  }

  private async saveSlot(): Promise<TreeValidationResult> {
    const { slotMinutes, effectiveFrom } = this.slotModel();
    try {
      await firstValueFrom(this.api.changeSlotDuration(this.id, { slotMinutes: Number(slotMinutes), effectiveFrom }));
    } catch (error: unknown) {
      return this.slotFailed(parseApiError(error));
    }

    this.toasts.showToast('doctors.flash.slot_changed');
    await this.router.navigate(['/doctors'], { queryParams: this.memory.lastQuery() });
    return undefined;
  }

  /** 400 field keys and the 422 for the date go on their field; anything else is in the slot section (D62). */
  private slotFailed(error: ApiError): TreeValidationResult {
    const errors: ValidationError.WithFieldTree[] = [];
    const place = (field: 'slotMinutes' | 'effectiveFrom', message: string) =>
      errors.push({ fieldTree: this.slotForm[field], kind: 'server', message });

    for (const [name, keys] of Object.entries(error.fieldErrors)) {
      if (name === 'slotMinutes' || name === 'effectiveFrom') keys.forEach((key) => place(name, key));
    }
    if (errors.length === 0 && error.key === NOT_FUTURE_KEY) {
      place('effectiveFrom', error.key);
    }
    if (errors.length === 0) {
      if (error.status === 404 && error.key === 'error.doctor.not_found') {
        this.loadErrorKey.set(error.key);
        this.loadState.set('notFound');
      } else {
        this.slotErrorKey.set(this.messages.forError(error));
      }
    }
    return errors;
  }

  // ---- shared

  private show(record: Doctor): void {
    this.record.set(record);
    this.model.set({ nameAr: record.nameAr, nameEn: record.nameEn });
  }
}
