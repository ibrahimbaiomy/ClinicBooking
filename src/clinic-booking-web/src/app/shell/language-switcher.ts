import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LanguageService } from '../core/i18n/language.service';

@Component({
  selector: 'cb-language-switcher',
  imports: [TranslocoPipe],
  templateUrl: './language-switcher.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LanguageSwitcher {
  protected readonly languages = inject(LanguageService);
}
