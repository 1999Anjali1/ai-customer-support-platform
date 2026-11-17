import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { I18nService } from './core/i18n.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `<router-outlet />`
})
export class App {
  // Injected once at start-up so the saved language/direction is applied immediately.
  protected readonly i18n = inject(I18nService);
}
