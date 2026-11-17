import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { errorMessage } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n.service';

interface DemoAccount {
  email: string;
  name: string;
  role: string;
  note: string;
}

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <div class="login-page">
      <section class="login-hero">
        <div class="brand big">
          <span class="brand-mark">S</span>
          <strong>SmartSupport <em>AI</em></strong>
        </div>
        <h1>AI-first customer support,<br />with humans in the loop.</h1>
        <ul class="hero-points">
          <li><span>🤖</span> An AI agent that reads bills, changes plans and opens tickets through secure tools</li>
          <li><span>📚</span> Answers grounded in the help centre with hybrid vector + keyword search (RAG)</li>
          <li><span>🌐</span> Arabic &amp; English with automatic right-to-left layout</li>
          <li><span>🎧</span> Live hand-over to human agents with AI summaries and suggested replies</li>
        </ul>
        <p class="muted small">Demo for the fictional operator “Sahab Telecom”, Qatar.</p>
      </section>

      <section class="login-card card">
        <div class="row between">
          <h2>Sign in</h2>
          <button type="button" class="btn ghost small" (click)="i18n.toggle()">
            {{ i18n.lang() === 'en' ? 'العربية' : 'English' }}
          </button>
        </div>

        <form (ngSubmit)="submit()" class="stack">
          <label>
            Email
            <input name="email" type="email" [(ngModel)]="email" required autocomplete="username" />
          </label>
          <label>
            Password
            <input name="password" type="password" [(ngModel)]="password" required autocomplete="current-password" />
          </label>
          @if (error()) {
            <p class="alert error">{{ error() }}</p>
          }
          <button class="btn primary" type="submit" [disabled]="busy()">
            {{ busy() ? 'Signing in…' : 'Sign in' }}
          </button>
        </form>

        <div class="demo-accounts">
          <p class="muted small">Demo accounts (password <code>Demo&#64;123</code>) – click to fill:</p>
          @for (a of demo; track a.email) {
            <button type="button" class="demo-account" (click)="use(a)">
              <strong>{{ a.name }}</strong>
              <span class="badge">{{ a.role }}</span>
              <small>{{ a.note }}</small>
            </button>
          }
        </div>
      </section>
    </div>
  `
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly i18n = inject(I18nService);

  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly demo: DemoAccount[] = [
    { email: 'rahul@demo.qa', name: 'Rahul Nair', role: 'Customer', note: 'Smart 50 plan, has an overdue bill' },
    { email: 'fatima@demo.qa', name: 'Fatima Al-Marri', role: 'Customer', note: 'Smart 100 plan, prefers Arabic' },
    { email: 'sara@demo.qa', name: 'Sara Thomas', role: 'Agent', note: 'Handles escalated chats' },
    { email: 'admin@demo.qa', name: 'Omar Haddad', role: 'Admin', note: 'Knowledge base & analytics' }
  ];

  constructor() {
    if (this.auth.isLoggedIn()) this.router.navigateByUrl(this.auth.homeFor(this.auth.role()));
  }

  protected use(account: DemoAccount): void {
    this.email = account.email;
    this.password = 'Demo@123';
    this.submit();
  }

  protected async submit(): Promise<void> {
    if (!this.email || !this.password) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const user = await this.auth.login(this.email, this.password);
      if (user.role === 'Customer') this.i18n.set(user.preferredLanguage);
      await this.router.navigateByUrl(this.auth.homeFor(user.role));
    } catch (err) {
      this.error.set(errorMessage(err, 'Sign in failed.'));
    } finally {
      this.busy.set(false);
    }
  }
}
