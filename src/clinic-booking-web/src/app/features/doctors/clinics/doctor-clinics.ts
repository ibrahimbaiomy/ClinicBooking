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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { Action } from '../../../shared/ui/action';
import { firstValueFrom } from 'rxjs';
import { Doctor, DoctorsApi } from '../../../../api/doctors-api';
import { ApiError, parseApiError } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { ClinicPermissions } from '../../../core/auth/permissions';
import { SessionService } from '../../../core/auth/session.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { ToastService } from '../../../core/ui/toast.service';
import { ConfirmDialog } from '../../../shared/ui/confirm-dialog';
import { byUiName, namesFor, NameView, uiName } from '../doctor-view';
import { DoctorsSession } from '../doctors-session';

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';

const MANAGE = ClinicPermissions.DoctorsManage;
const OVERLAP_KEY = 'error.doctor.period_overlaps_other_clinic';

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

/**
 * A doctor's clinics (D61, D62, D64): the assignments with their state in words, activate, deactivate
 * (confirmed), a refused reactivation in its row, add a clinic, and the working-hours link per clinic.
 * Anyone signed in may open it; each control follows `canIn` for the clinic it concerns, so a user
 * without doctors.manage there sees the row read-only. Changes stay on the page with a toast.
 */
@Component({
  selector: 'cb-doctor-clinics',
  imports: [Action, RouterLink, TranslocoPipe, ConfirmDialog],
  templateUrl: './doctor-clinics.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DoctorClinicsPage {
  private readonly api = inject(DoctorsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(DoctorsSession);
  private readonly session = inject(SessionService);
  private readonly languages = inject(LanguageService);
  private readonly toasts = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly id = this.route.snapshot.paramMap.get('id') ?? '';
  protected readonly listQuery = this.memory.lastQuery;

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorKey = signal<string | null>(null);
  protected readonly doctor = signal<Doctor | null>(null);

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  protected readonly names = computed(() => {
    const doctor = this.doctor();
    return doctor === null ? null : namesFor(doctor.nameAr, doctor.nameEn, this.languages.language());
  });

  /** A clinic can be added by anyone who manages doctors somewhere (the picker offers only those clinics). */
  protected readonly canAddClinic = computed(() => this.session.canInAny(MANAGE));

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

  // ---- activate and deactivate

  protected async activate(row: AssignmentView): Promise<void> {
    this.clearRowMessage(row.clinicId);
    this.busyRow.set(row.clinicId);
    try {
      this.doctor.set(await firstValueFrom(this.api.activateClinic(this.id, row.clinicId)));
      this.toasts.showToast('doctors.flash.clinic_activated');
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
      this.doctor.set(doctor);
      this.toasts.showToast('doctors.flash.clinic_deactivated');
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
      this.doctor.set(doctor);
      this.toasts.showToast('doctors.flash.clinic_added');
    } catch (error: unknown) {
      this.addErrorKey.set(this.messages.forError(parseApiError(error)));
    } finally {
      this.adding.set(false);
    }
  }

  // ---- shared

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
}
