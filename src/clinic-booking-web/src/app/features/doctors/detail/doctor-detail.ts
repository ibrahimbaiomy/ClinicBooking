import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { form, FormField, requiredError, submit, TreeValidationResult, validate, ValidationError } from '@angular/forms/signals';
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
import { ConfirmDialog } from '../../../shared/ui/confirm-dialog';
import { byUiName, namesFor, NameView, uiName } from '../doctor-view';
import { DoctorsSession } from '../doctors-session';
import { cairoTomorrow, SLOT_MINUTES } from '../slot';

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';

const MANAGE = ClinicPermissions.DoctorsManage;
const OVERLAP_KEY = 'error.doctor.period_overlaps_other_clinic';
const NOT_FUTURE_KEY = 'error.doctor.effective_from_not_future';

interface AssignmentView {
  clinicId: string;
  primary: NameView;
  secondary: NameView;
  /** For labels such as "Deactivate at {{ name }}". */
  label: string;
  isActive: boolean;
  /** UX only: the API decides (D57). */
  manageable: boolean;
}

/** A message for one assignment row: a key, and the other clinic's name for an overlap (D61). */
interface RowMessage {
  key: string;
  clinic: string | null;
}

interface ClinicOption {
  id: string;
  label: string;
}

interface SlotFields {
  slotMinutes: string;
  effectiveFrom: string;
}

/**
 * One doctor (D61, D62): the summary, the clinic assignments (working hours, activate, deactivate, add),
 * the slot duration and its change, and delete. Every control follows `canIn` for the clinic concerned
 * (UX only); every change shows the server's answer.
 */
@Component({
  selector: 'cb-doctor-detail',
  imports: [RouterLink, TranslocoPipe, IntlPipe, ConfirmDialog, FormField],
  templateUrl: './doctor-detail.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DoctorDetail {
  private readonly api = inject(DoctorsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(DoctorsSession);
  private readonly session = inject(SessionService);
  private readonly languages = inject(LanguageService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly id = this.route.snapshot.paramMap.get('id') ?? '';
  protected readonly listQuery = this.memory.lastQuery;
  protected readonly slotOptions = SLOT_MINUTES;

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorKey = signal<string | null>(null);
  protected readonly doctor = signal<Doctor | null>(null);
  protected readonly flash = signal<string | null>(null);

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  // ---- what the user may do (UX only, D57, D62)
  /** Editing names, specialties and the slot needs doctors.manage in any of the doctor's clinics. */
  protected readonly canManage = computed(() =>
    (this.doctor()?.clinics ?? []).some((c) => this.session.canIn(MANAGE, c.clinicId)),
  );
  /** Delete needs it in all of them. */
  protected readonly canDelete = computed(() => {
    const clinics = this.doctor()?.clinics ?? [];
    return clinics.length > 0 && clinics.every((c) => this.session.canIn(MANAGE, c.clinicId));
  });
  /** A clinic can be added by anyone who manages doctors somewhere. */
  protected readonly canAddClinic = computed(() => this.session.canInAny(MANAGE));

  // ---- the summary
  protected readonly names = computed(() => {
    const doctor = this.doctor();
    return doctor === null ? null : namesFor(doctor.nameAr, doctor.nameEn, this.languages.language());
  });

  /** Today's slot length and the pending change, as numbers (int32 may arrive as a string). */
  protected readonly slot = computed(() => {
    const doctor = this.doctor();
    const pending = doctor?.pendingSlotChange ?? null;
    return {
      current: Number(doctor?.slotMinutes ?? 0),
      pending: pending === null ? null : { slotMinutes: Number(pending.slotMinutes), effectiveFrom: pending.effectiveFrom },
    };
  });

  protected readonly specialties = computed(() => {
    const language = this.languages.language();
    return byUiName(this.doctor()?.specialties ?? [], language).map((s) => namesFor(s.nameAr, s.nameEn, language).primary);
  });

  protected readonly assignments = computed<AssignmentView[]>(() => {
    const language = this.languages.language();
    return byUiName(this.doctor()?.clinics ?? [], language).map((c) => ({
      clinicId: String(c.clinicId),
      ...namesFor(c.nameAr, c.nameEn, language),
      label: uiName(c, language),
      isActive: c.isActive,
      manageable: this.session.canIn(MANAGE, c.clinicId),
    }));
  });

  // ---- activate (immediate) and deactivate (confirmed), per row
  protected readonly busyRow = signal<string | null>(null);
  protected readonly rowMessages = signal<Record<string, RowMessage>>({});
  protected readonly pendingDeactivate = signal<AssignmentView | null>(null);
  protected readonly deactivateErrorKey = signal<string | null>(null);

  // ---- add a clinic
  protected readonly picking = signal(false);
  protected readonly refreshingOptions = signal(false);
  protected readonly chosenClinic = signal('');
  protected readonly adding = signal(false);
  protected readonly addErrorKey = signal<string | null>(null);

  /** Clinics where the user manages doctors and the doctor has no assignment, active or not (D62). */
  protected readonly addOptions = computed<ClinicOption[]>(() => {
    const assigned = new Set((this.doctor()?.clinics ?? []).map((c) => String(c.clinicId)));
    const language = this.languages.language();
    const grants = (this.session.user()?.clinicPermissions ?? [])
      .filter((grant) => grant.permissions.includes(MANAGE) && !assigned.has(String(grant.clinicId)))
      .map((grant) => ({ id: String(grant.clinicId), nameAr: grant.clinicNameAr, nameEn: grant.clinicNameEn }));
    return byUiName(grants, language).map((grant) => ({ id: grant.id, label: uiName(grant, language) }));
  });

  // ---- slot change
  protected readonly changingSlot = signal(false);
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

  // ---- delete
  protected readonly pendingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly deleteErrorKey = signal<string | null>(null);

  constructor() {
    this.flash.set(this.memory.takeFlash());
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
        next: (doctor) => {
          this.doctor.set(doctor);
          this.loadState.set('ready');
        },
        error: (error: unknown) => {
          const failure = parseApiError(error);
          this.loadErrorKey.set(this.messages.forError(failure));
          this.loadState.set(failure.status === 404 ? 'notFound' : 'error');
        },
      });
  }

  /** A server message that is not a known key is replaced by error.unexpected (never raw text). */
  protected messageKey(message: string | undefined): string {
    return this.messages.keyFor(message ?? '');
  }

  // ---- activate and deactivate

  protected async activate(row: AssignmentView): Promise<void> {
    this.clearRowMessage(row.clinicId);
    this.busyRow.set(row.clinicId);
    try {
      this.updated(await firstValueFrom(this.api.activateClinic(this.id, row.clinicId)), 'doctors.flash.clinic_activated');
    } catch (error: unknown) {
      this.rowFailed(row.clinicId, parseApiError(error));
    } finally {
      this.busyRow.set(null);
    }
  }

  protected askDeactivate(row: AssignmentView): void {
    this.clearRowMessage(row.clinicId);
    this.deactivateErrorKey.set(null);
    this.pendingDeactivate.set(row);
  }

  protected cancelDeactivate(): void {
    if (this.busyRow() === null) {
      this.pendingDeactivate.set(null);
    }
  }

  protected async confirmDeactivate(): Promise<void> {
    const row = this.pendingDeactivate();
    if (row === null) return;

    this.busyRow.set(row.clinicId);
    try {
      const doctor = await firstValueFrom(this.api.deactivateClinic(this.id, row.clinicId));
      this.pendingDeactivate.set(null);
      this.updated(doctor, 'doctors.flash.clinic_deactivated');
    } catch (error: unknown) {
      const failure = parseApiError(error);
      if (failure.status === 404 && failure.key === 'error.doctor.not_found') {
        this.pendingDeactivate.set(null);
        this.gone();
      } else {
        this.deactivateErrorKey.set(this.messages.forError(failure));
      }
    } finally {
      this.busyRow.set(null);
    }
  }

  // ---- add a clinic

  /** Opening the picker refreshes the session, so a grant made after sign-in is offered at once (D62). */
  protected async openPicker(): Promise<void> {
    this.addErrorKey.set(null);
    this.chosenClinic.set('');
    this.picking.set(true);
    this.refreshingOptions.set(true);
    try {
      await this.session.refreshUser();
    } finally {
      this.refreshingOptions.set(false);
    }
  }

  protected closePicker(): void {
    this.picking.set(false);
    this.addErrorKey.set(null);
  }

  protected onChooseClinic(event: Event): void {
    this.chosenClinic.set((event.target as HTMLSelectElement).value);
  }

  protected async addClinic(event: Event): Promise<void> {
    event.preventDefault();
    const clinicId = this.chosenClinic();
    if (clinicId === '') return;

    this.adding.set(true);
    this.addErrorKey.set(null);
    try {
      const doctor = await firstValueFrom(this.api.addClinic(this.id, clinicId));
      this.picking.set(false);
      this.updated(doctor, 'doctors.flash.clinic_added');
    } catch (error: unknown) {
      this.addErrorKey.set(this.messages.forError(parseApiError(error)));
    } finally {
      this.adding.set(false);
    }
  }

  // ---- slot change

  protected openSlotChange(): void {
    this.minDate.set(cairoTomorrow());
    this.slotErrorKey.set(null);
    this.slotModel.set({ slotMinutes: String(this.doctor()?.slotMinutes ?? 15), effectiveFrom: '' });
    this.slotForm().reset();
    this.changingSlot.set(true);
  }

  protected closeSlotChange(): void {
    this.changingSlot.set(false);
  }

  protected async onSlotSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.slotErrorKey.set(null);
    await submit(this.slotForm, {
      action: () => this.saveSlot(),
      onInvalid: () => this.slotForm.effectiveFrom().focusBoundControl(),
    });
  }

  private async saveSlot(): Promise<TreeValidationResult> {
    const { slotMinutes, effectiveFrom } = this.slotModel();
    try {
      const doctor = await firstValueFrom(
        this.api.changeSlotDuration(this.id, { slotMinutes: Number(slotMinutes), effectiveFrom }),
      );
      this.changingSlot.set(false);
      this.updated(doctor, 'doctors.flash.slot_changed');
      return undefined;
    } catch (error: unknown) {
      return this.slotFailed(parseApiError(error));
    }
  }

  /** 400 field keys and the 422 for the date go on their field; anything else is form-level (D62). */
  private slotFailed(error: ApiError): TreeValidationResult {
    const errors: ValidationError.WithFieldTree[] = [];
    const place = (field: 'slotMinutes' | 'effectiveFrom', message: string) =>
      errors.push({ fieldTree: this.slotForm[field], kind: 'server', message });

    for (const [name, keys] of Object.entries(error.fieldErrors)) {
      if (name === 'slotMinutes' || name === 'effectiveFrom') {
        keys.forEach((key) => place(name, key));
      }
    }
    if (errors.length === 0 && error.key === NOT_FUTURE_KEY) {
      place('effectiveFrom', error.key);
    }
    if (errors.length === 0) {
      if (error.status === 404 && error.key === 'error.doctor.not_found') {
        this.gone();
      } else {
        this.slotErrorKey.set(this.messages.forError(error));
      }
    }
    return errors;
  }

  // ---- delete

  protected askDelete(): void {
    this.deleteErrorKey.set(null);
    this.pendingDelete.set(true);
  }

  protected cancelDelete(): void {
    if (!this.deleting()) {
      this.pendingDelete.set(false);
    }
  }

  protected async confirmDelete(): Promise<void> {
    this.deleting.set(true);
    try {
      await firstValueFrom(this.api.delete(this.id));
      await this.leave('doctors.flash.deleted');
    } catch (error: unknown) {
      const failure = parseApiError(error);
      if (failure.status === 404) {
        // Already deleted elsewhere: the outcome is what the user wanted.
        await this.leave('doctors.flash.already_deleted');
      } else {
        // error.doctor.all_clinics_required and every other failure stay in the dialog (D62).
        this.deleteErrorKey.set(this.messages.forError(failure));
      }
    } finally {
      this.deleting.set(false);
    }
  }

  // ---- shared

  private updated(doctor: Doctor, flashKey: string): void {
    this.doctor.set(doctor);
    this.flash.set(flashKey);
  }

  /** The doctor was deleted elsewhere while this page was open. */
  private gone(): void {
    this.loadErrorKey.set('error.doctor.not_found');
    this.loadState.set('notFound');
  }

  private rowFailed(clinicId: string, error: ApiError): void {
    if (error.status === 404 && error.key === 'error.doctor.not_found') {
      this.gone();
      return;
    }
    // A reactivation refused for an overlap names the other clinic when its name is known (D61, D62);
    // otherwise the generic overlap message says it.
    const clinic = error.key === OVERLAP_KEY ? this.clinicName(error.conflictingClinicId) : null;
    const message: RowMessage =
      clinic === null ? { key: this.messages.forError(error), clinic: null } : { key: 'doctors.detail.overlap_named', clinic };
    this.rowMessages.update((all) => ({ ...all, [clinicId]: message }));
  }

  private clearRowMessage(clinicId: string): void {
    this.rowMessages.update((all) => Object.fromEntries(Object.entries(all).filter(([id]) => id !== clinicId)));
  }

  /** The other clinic's name in the UI language: from the doctor's own clinics, else from the session. */
  private clinicName(clinicId: number | null): string | null {
    if (clinicId === null) return null;
    const language = this.languages.language();
    const own = this.doctor()?.clinics.find((c) => Number(c.clinicId) === clinicId);
    if (own !== undefined) return uiName(own, language);
    const grant = this.session.user()?.clinicPermissions.find((g) => Number(g.clinicId) === clinicId);
    return grant === undefined ? null : uiName({ nameAr: grant.clinicNameAr, nameEn: grant.clinicNameEn }, language);
  }

  private async leave(flashKey: string): Promise<void> {
    this.pendingDelete.set(false);
    this.memory.setFlash(flashKey);
    await this.router.navigate(['/doctors'], { queryParams: this.memory.lastQuery() });
  }
}
