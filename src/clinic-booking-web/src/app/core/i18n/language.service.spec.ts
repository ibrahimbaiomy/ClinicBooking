import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideTestTransloco } from '../../../testing/transloco-testing';
import { LANGUAGE_STORAGE_KEY } from './language';
import { LanguageService } from './language.service';

function create(): LanguageService {
  TestBed.configureTestingModule({ providers: [provideTestTransloco()] });
  return TestBed.inject(LanguageService);
}

describe('LanguageService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.lang = '';
    document.documentElement.dir = '';
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('defaults to Arabic with right-to-left direction', () => {
    const service = create();

    expect(service.language()).toBe('ar');
    expect(service.direction()).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
  });

  it('starts in the saved language', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en');

    const service = create();

    expect(service.language()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
    expect(document.documentElement.dir).toBe('ltr');
  });

  it.each(['fr', '', 'AR', 'null', ' ar', 'en-GB', '{}'])(
    'ignores an invalid saved value (%j) and uses Arabic',
    (stored) => {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, stored);

      const service = create();

      expect(service.language()).toBe('ar');
      expect(document.documentElement.dir).toBe('rtl');
    },
  );

  it('does not crash when reading localStorage throws, and uses Arabic', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError');
    });

    const service = create();

    expect(service.language()).toBe('ar');
  });

  it('does not crash when writing localStorage throws, and still switches for the session', () => {
    const service = create();
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('quota', 'QuotaExceededError');
    });

    expect(() => service.set('en')).not.toThrow();
    expect(service.language()).toBe('en');
  });

  it('saves the choice', () => {
    const service = create();

    service.set('en');

    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en');
  });

  it('updates lang and dir on the document when the language signal changes', () => {
    const service = create();

    service.set('en');
    TestBed.tick(); // runs the effect that follows the signal

    expect(document.documentElement.lang).toBe('en');
    expect(document.documentElement.dir).toBe('ltr');
    expect(service.direction()).toBe('ltr');

    service.set('ar');
    TestBed.tick();

    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
  });

  it('keeps Transloco on the same language', () => {
    const service = create();
    const transloco = TestBed.inject(TranslocoService);

    service.set('en');
    TestBed.tick();

    expect(transloco.getActiveLang()).toBe('en');
  });

  it('toggles between Arabic and English', () => {
    const service = create();

    service.toggle();
    expect(service.language()).toBe('en');

    service.toggle();
    expect(service.language()).toBe('ar');
  });

  it('ignores a language it does not know', () => {
    const service = create();

    service.set('fr' as never);

    expect(service.language()).toBe('ar');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull();
  });
});
