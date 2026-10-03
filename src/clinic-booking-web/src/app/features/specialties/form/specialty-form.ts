import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
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
import { SpecialtiesApi, Specialty } from '../../../../api/specialties-api';
import { ApiError, parseApiError } from '../../../core/auth/api-error';
import { ErrorMessageService } from '../../../core/auth/error-message.service';
import { IntlPipe } from '../../../core/format/intl.pipe';
import { SpecialtiesSession } from '../specialties-session';

/** Mirror of Specialty.NameMaxLength on the API. */
export const NAME_MAX_LENGTH = 100;

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';
type Field = 'nameAr' | 'nameEn';

interface Names {
  nameAr: string;
  nameEn: string;
}

const CONFLICT_KEY = 'error.concurrency.conflict';
const NAME_KEY = /^error\.specialty\.name_(ar|en)_/;

/** The client-side rules, with the back-end keys (required, at most 100 after trimming). */
function nameProblem(raw: string, language: 'ar' | 'en') {
  const trimmed = raw.trim();
  if (trimmed === '') {
    return requiredError({ message: `error.specialty.name_${language}_required` });
  }
  if (trimmed.length > NAME_MAX_LENGTH) {
    return maxLengthError(NAME_MAX_LENGTH, { message: `error.specialty.name_${language}_too_long` });
  }
  return undefined;
}

/** Create and edit share one page: `/specialties/new` and `/specialties/:id/edit`. */
@Component({
  selector: 'cb-specialty-form',
  imports: [FormField, RouterLink, TranslocoPipe, IntlPipe],
  templateUrl: './specialty-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SpecialtyForm {
  private readonly api = inject(SpecialtiesApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(ErrorMessageService);
  private readonly memory = inject(SpecialtiesSession);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  private readonly id = this.route.snapshot.paramMap.get('id');
  protected readonly isNew = this.id === null;
  protected readonly listQuery = this.memory.lastQuery;

  protected readonly model = signal<Names>({ nameAr: '', nameEn: '' });
  protected readonly specialtyForm = form(this.model, (path) => {
    validate(path.nameAr, ({ value }) => nameProblem(value(), 'ar'));
    validate(path.nameEn, ({ value }) => nameProblem(value(), 'en'));
  });

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorKey = signal<string | null>(null);
  protected readonly record = signal<Specialty | null>(null);

  protected readonly formErrorKey = signal<string | null>(null);
  protected readonly correlationId = signal<string | null>(null);

  /** Someone else changed the record: Save stays disabled until the user reloads (D53). */
  protected readonly conflict = signal(false);
  protected readonly reloading = signal(false);
  /** What the user had typed when the conflict happened, shown after Reload so it can be re-applied. */
  protected readonly earlier = signal<Names | null>(null);

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

    await submit(this.specialtyForm, {
      action: () => this.save(),
      onInvalid: () => {
        const field = this.specialtyForm.nameAr().invalid() ? this.specialtyForm.nameAr : this.specialtyForm.nameEn;
        field().focusBoundControl();
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

  private show(record: Specialty): void {
    this.record.set(record);
    this.model.set({ nameAr: record.nameAr, nameEn: record.nameEn });
  }

  private async save(): Promise<TreeValidationResult> {
    const { nameAr, nameEn } = this.model();
    const names = { nameAr: nameAr.trim(), nameEn: nameEn.trim() };

    try {
      if (this.isNew) {
        await firstValueFrom(this.api.create(names));
      } else {
        const rowVersion = this.record()?.rowVersion ?? null;
        await firstValueFrom(this.api.update(this.id as string, { ...names, rowVersion }));
      }
    } catch (error: unknown) {
      return this.failed(parseApiError(error));
    }

    this.memory.setFlash(this.isNew ? 'specialties.flash.saved_created' : 'specialties.flash.saved_updated');
    await this.router.navigate(['/specialties'], { queryParams: this.memory.lastQuery() });
    return undefined;
  }

  /** Maps a failed save onto the right field, the conflict banner, or a form-level message. */
  private failed(error: ApiError): TreeValidationResult {
    const errors: ValidationError.WithFieldTree[] = [];
    const place = (field: Field, message: string) =>
      errors.push({ fieldTree: this.specialtyForm[field], kind: 'server', message });

    // 400: errors keyed by camelCase field name, each message a key.
    for (const [name, keys] of Object.entries(error.fieldErrors)) {
      if (name === 'nameAr' || name === 'nameEn') {
        keys.forEach((key) => place(name, key));
      }
    }

    // 409 name taken (and the same keys on a 400) tell us the field by their name.
    const named = NAME_KEY.exec(error.key);
    if (named !== null && errors.length === 0) {
      place(named[1] === 'ar' ? 'nameAr' : 'nameEn', error.key);
    }

    if (error.key === CONFLICT_KEY) {
      this.conflict.set(true);
      afterNextRender(() => this.banner()?.nativeElement.focus(), { injector: this.injector });
    } else if (error.status === 404) {
      this.loadErrorKey.set(this.messages.forError(error));
      this.loadState.set('notFound');
    } else if (errors.length === 0) {
      this.formErrorKey.set(this.messages.forError(error));
      this.correlationId.set(error.correlationId);
    }

    return errors;
  }
}
