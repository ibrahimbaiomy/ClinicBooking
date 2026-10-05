import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { CanDirective } from './core/auth/can.directive';
import { Permissions } from './core/auth/permissions';
import { SessionService } from './core/auth/session.service';
import { LanguageSwitcher } from './shell/language-switcher';

@Component({
  selector: 'cb-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoPipe, CanDirective, LanguageSwitcher],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly session = inject(SessionService);
  protected readonly permissions = Permissions;
  private readonly router = inject(Router);

  protected async signOut(): Promise<void> {
    await this.session.logout();
    await this.router.navigateByUrl('/login');
  }

  constructor() {
    const title = inject(Title);

    // Emits again after every language change, once the new file has loaded.
    inject(TranslocoService)
      .selectTranslate('app.title')
      .pipe(takeUntilDestroyed())
      .subscribe((text) => title.setTitle(text));
  }
}
