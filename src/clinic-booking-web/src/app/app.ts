import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { RouterOutlet } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LanguageSwitcher } from './shell/language-switcher';

@Component({
  selector: 'cb-root',
  imports: [RouterOutlet, TranslocoPipe, LanguageSwitcher],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  constructor() {
    const title = inject(Title);

    // Emits again after every language change, once the new file has loaded.
    inject(TranslocoService)
      .selectTranslate('app.title')
      .pipe(takeUntilDestroyed())
      .subscribe((text) => title.setTitle(text));
  }
}
