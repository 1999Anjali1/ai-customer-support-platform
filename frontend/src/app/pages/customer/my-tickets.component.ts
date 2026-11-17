import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { I18nService } from '../../core/i18n.service';
import { Ticket } from '../../core/models';

@Component({
  selector: 'app-my-tickets',
  imports: [DatePipe],
  template: `
    <div class="page-header">
      <h1>{{ i18n.t('myTickets') }}</h1>
    </div>

    @if (loading()) {
      <p class="muted">Loading…</p>
    } @else if (tickets().length === 0) {
      <div class="card empty">{{ i18n.t('noTickets') }}</div>
    } @else {
      <div class="ticket-grid">
        @for (t of tickets(); track t.id) {
          <article class="card ticket">
            <div class="row between">
              <code>{{ t.number }}</code>
              <span class="status" [class]="'status ' + t.status.toLowerCase()">{{ t.status }}</span>
            </div>
            <h3 dir="auto">{{ t.subject }}</h3>
            <p class="small muted" dir="auto">{{ t.description }}</p>
            <div class="row gap small">
              <span class="badge">{{ t.category }}</span>
              <span class="badge" [class]="'badge prio-' + t.priority.toLowerCase()">{{ t.priority }}</span>
              @if (t.createdByAi) { <span class="badge ai">🤖 opened by AI</span> }
              <span class="muted push-end">{{ t.createdAt | date: 'mediumDate' }}</span>
            </div>
          </article>
        }
      </div>
    }
  `
})
export class MyTicketsComponent {
  private readonly api = inject(ApiService);
  protected readonly i18n = inject(I18nService);
  protected readonly tickets = signal<Ticket[]>([]);
  protected readonly loading = signal(true);

  constructor() {
    this.api
      .myTickets()
      .then(t => this.tickets.set(t))
      .finally(() => this.loading.set(false));
  }
}
