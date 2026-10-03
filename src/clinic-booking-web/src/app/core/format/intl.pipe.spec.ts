import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTestTransloco } from '../../../testing/transloco-testing';
import { LanguageService } from '../i18n/language.service';
import { IntlPipe } from './intl.pipe';

@Component({
  selector: 'cb-intl-host',
  imports: [IntlPipe],
  template: `<time>{{ date() | intl: 'date' }}</time><b>{{ amount() | intl: 'number' }}</b>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class Host {
  readonly date = signal<string | null>('2026-10-04');
  readonly amount = signal<number | null>(1234.5);
}

describe('IntlPipe', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideTestTransloco()] });
  });

  it('formats in the active language and follows a language change, without zone.js', async () => {
    const fixture = TestBed.createComponent(Host);
    const languages = TestBed.inject(LanguageService);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('time')?.textContent).toContain('أكتوبر');

    languages.set('en');
    await fixture.whenStable();

    expect(element.querySelector('time')?.textContent).toBe('4 October 2026');
    expect(element.querySelector('b')?.textContent).toBe('1,234.5');
  });

  it('renders nothing for null', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.date.set(null);
    fixture.componentInstance.amount.set(null);
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('time')?.textContent).toBe('');
    expect(element.querySelector('b')?.textContent).toBe('');
  });
});
