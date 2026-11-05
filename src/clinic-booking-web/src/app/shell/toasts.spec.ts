import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import ar from '../../../public/i18n/ar.json';
import clinicsAr from '../../../public/i18n/clinics/ar.json';
import { provideTestTransloco } from '../../testing/transloco-testing';
import { APP_SETTINGS, MESSAGE_DURATION_MS } from '../app-settings';
import { ToastService } from '../core/ui/toast.service';
import { Toasts } from './toasts';

describe('Toasts (D64)', () => {
  let service: ToastService;
  let host: HTMLElement;

  async function render(duration?: number) {
    TestBed.configureTestingModule({
      providers: [provideTestTransloco(), ...(duration === undefined ? [] : [{ provide: MESSAGE_DURATION_MS, useValue: duration }])],
    });
    const transloco = TestBed.inject(TranslocoService);
    await firstValueFrom(transloco.load('ar'));
    await firstValueFrom(transloco.load('clinics/ar'));
    const fixture = TestBed.createComponent(Toasts);
    host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);
    service = TestBed.inject(ToastService);
    return fixture;
  }

  const region = () => host.querySelector<HTMLElement>('[role=status]')!;
  const toasts = () => [...host.querySelectorAll<HTMLElement>('.cb-toast')];
  const closeButton = (toast: HTMLElement) => toast.querySelector<HTMLButtonElement>('button')!;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
  });

  afterEach(() => {
    host?.remove();
    vi.useRealTimers();
  });

  it('defaults to 5 seconds, set in the one settings file', () => {
    expect(APP_SETTINGS.messageDurationMs).toBe(5000);
    TestBed.configureTestingModule({});
    expect(TestBed.inject(MESSAGE_DURATION_MS)).toBe(5000);
  });

  it('shows a translated message, including a feature scope key, in a polite status region that is always there', async () => {
    const fixture = await render();
    expect(region().getAttribute('aria-live')).toBe('polite');
    expect(toasts()).toHaveLength(0);

    service.showToast('clinics.flash.deleted');
    await fixture.whenStable();

    expect(toasts()).toHaveLength(1);
    expect(toasts()[0].textContent).toContain(clinicsAr.flash.deleted);
    expect(closeButton(toasts()[0]).textContent?.trim()).toBe(ar.toast.close);
  });

  it('never takes the focus', async () => {
    const fixture = await render();
    const outside = document.createElement('button');
    document.body.appendChild(outside);
    outside.focus();

    service.showToast('clinics.flash.deleted');
    await fixture.whenStable();

    expect(document.activeElement).toBe(outside);
    outside.remove();
  });

  it('disappears after the configured duration', async () => {
    const fixture = await render(3000);
    service.showToast('clinics.flash.deleted');
    await fixture.whenStable();

    vi.advanceTimersByTime(2999);
    await fixture.whenStable();
    expect(toasts()).toHaveLength(1);

    vi.advanceTimersByTime(1);
    await fixture.whenStable();
    expect(toasts()).toHaveLength(0);
  });

  it('closes with its Close button', async () => {
    const fixture = await render();
    service.showToast('clinics.flash.deleted');
    service.showToast('clinics.flash.already_deleted');
    await fixture.whenStable();

    closeButton(toasts()[0]).click();
    await fixture.whenStable();

    expect(service.toasts().map((toast) => toast.key)).toEqual(['clinics.flash.already_deleted']);
  });

  it('pauses while hovered and continues with the time that was left', async () => {
    const fixture = await render(5000);
    service.showToast('clinics.flash.deleted');
    await fixture.whenStable();

    vi.advanceTimersByTime(2000);
    toasts()[0].dispatchEvent(new MouseEvent('mouseenter'));
    vi.advanceTimersByTime(60_000);
    await fixture.whenStable();
    expect(toasts()).toHaveLength(1);

    toasts()[0].dispatchEvent(new MouseEvent('mouseleave'));
    vi.advanceTimersByTime(2999);
    await fixture.whenStable();
    expect(toasts()).toHaveLength(1);
    vi.advanceTimersByTime(1);
    await fixture.whenStable();
    expect(toasts()).toHaveLength(0);
  });

  it('pauses while its Close button has the focus, even when the pointer leaves', async () => {
    const fixture = await render(5000);
    service.showToast('clinics.flash.deleted');
    await fixture.whenStable();

    toasts()[0].dispatchEvent(new MouseEvent('mouseenter'));
    closeButton(toasts()[0]).focus();
    toasts()[0].dispatchEvent(new MouseEvent('mouseleave'));
    vi.advanceTimersByTime(60_000);
    await fixture.whenStable();
    expect(toasts()).toHaveLength(1);

    closeButton(toasts()[0]).blur();
    vi.advanceTimersByTime(5000);
    await fixture.whenStable();
    expect(toasts()).toHaveLength(0);
  });

  it('uses logical positioning only, so it mirrors in RTL', async () => {
    await render();
    expect(region().className).toContain('end-4');
    expect(region().className).not.toMatch(/\b(left|right)-/);
  });
});
