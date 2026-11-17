import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { I18nService, TranslationKey } from '../core/i18n.service';
import { Role } from '../core/models';
import { RealtimeService } from '../core/realtime.service';

interface NavItem {
  path: string;
  label: TranslationKey;
  icon: string;
  roles: Role[];
}

const NAV: NavItem[] = [
  { path: '/chat', label: 'chat', icon: '💬', roles: ['Customer'] },
  { path: '/my-tickets', label: 'myTickets', icon: '🎫', roles: ['Customer'] },
  { path: '/agent', label: 'agentConsole', icon: '🎧', roles: ['Agent', 'Admin'] },
  { path: '/tickets', label: 'tickets', icon: '🎫', roles: ['Agent', 'Admin'] },
  { path: '/knowledge', label: 'knowledge', icon: '📚', roles: ['Agent', 'Admin'] },
  { path: '/analytics', label: 'analytics', icon: '📊', roles: ['Agent', 'Admin'] }
];

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <a class="brand" [routerLink]="home()">
        <span class="brand-mark">S</span>
        <span class="brand-text">
          <strong>SmartSupport <em>AI</em></strong>
          <small>{{ i18n.t('appTagline') }}</small>
        </span>
      </a>

      <nav class="nav">
        @for (item of items(); track item.path) {
          <a [routerLink]="item.path" routerLinkActive="active">
            <span aria-hidden="true">{{ item.icon }}</span> {{ i18n.t(item.label) }}
          </a>
        }
      </nav>

      <div class="topbar-end">
        <span class="conn" [class]="'conn ' + realtime.state()" [title]="'Live connection: ' + realtime.state()"></span>
        <button class="btn ghost small" (click)="i18n.toggle()" title="Switch language">
          {{ i18n.lang() === 'en' ? 'العربية' : 'English' }}
        </button>
        <div class="user-chip">
          <span class="avatar">{{ initials() }}</span>
          <span class="user-meta">
            <strong>{{ auth.user()?.fullName }}</strong>
            <small>{{ auth.role() }}</small>
          </span>
        </div>
        <button class="btn ghost small" (click)="logout()">{{ i18n.t('logout') }}</button>
      </div>
    </header>

    <main class="page">
      <router-outlet />
    </main>
  `
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly i18n = inject(I18nService);
  protected readonly realtime = inject(RealtimeService);
  private readonly router = inject(Router);

  protected readonly items = computed(() => NAV.filter(n => n.roles.includes(this.auth.role()!)));
  protected readonly home = computed(() => this.auth.homeFor(this.auth.role()));
  protected readonly initials = computed(() =>
    (this.auth.user()?.fullName ?? '?')
      .split(' ')
      .map(p => p[0])
      .slice(0, 2)
      .join('')
  );

  constructor() {
    // "/" → the right home page for this role
    if (this.router.url === '/' || this.router.url === '') this.router.navigateByUrl(this.home());
    this.realtime.connect().catch(() => undefined);
  }

  protected async logout(): Promise<void> {
    await this.realtime.disconnect();
    this.auth.logout();
  }
}
