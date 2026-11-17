import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../../core/api.service';
import { Ticket, TicketStatus } from '../../core/models';

@Component({
  selector: 'app-tickets',
  imports: [DatePipe, FormsModule],
  template: `
    <div class="page-header">
      <h1>Support tickets</h1>
      <div class="row gap">
        <input class="search" placeholder="Search tickets…" [(ngModel)]="query" />
        <select [(ngModel)]="statusFilter">
          <option value="">All statuses</option>
          @for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
        </select>
      </div>
    </div>

    @if (error()) { <p class="alert error">{{ error() }}</p> }

    <div class="card table-card">
      <table>
        <thead>
          <tr>
            <th>Ticket</th>
            <th>Customer</th>
            <th>Subject</th>
            <th>Category</th>
            <th>Priority</th>
            <th>Source</th>
            <th>Created</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          @for (t of filtered(); track t.id) {
            <tr>
              <td><code>{{ t.number }}</code></td>
              <td>{{ t.customerName }}</td>
              <td class="subject" [title]="t.description" dir="auto">{{ t.subject }}</td>
              <td><span class="badge">{{ t.category }}</span></td>
              <td><span class="badge" [class]="'badge prio-' + t.priority.toLowerCase()">{{ t.priority }}</span></td>
              <td>{{ t.createdByAi ? '🤖 AI' : '👤 Staff' }}</td>
              <td class="muted">{{ t.createdAt | date: 'MMM d, HH:mm' }}</td>
              <td>
                <select [ngModel]="t.status" (ngModelChange)="changeStatus(t, $event)" [disabled]="t.status === 'Closed'">
                  @for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
                </select>
              </td>
            </tr>
          } @empty {
            <tr><td colspan="8" class="muted center">No tickets found.</td></tr>
          }
        </tbody>
      </table>
    </div>
  `
})
export class TicketsComponent {
  private readonly api = inject(ApiService);
  protected readonly tickets = signal<Ticket[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly statuses: TicketStatus[] = ['Open', 'InProgress', 'Resolved', 'Closed'];

  private readonly querySig = signal('');
  private readonly statusSig = signal<string>('');
  protected get query() { return this.querySig(); }
  protected set query(v: string) { this.querySig.set(v); }
  protected get statusFilter() { return this.statusSig(); }
  protected set statusFilter(v: string) { this.statusSig.set(v); }

  protected readonly filtered = computed(() => {
    const q = this.querySig().toLowerCase();
    const s = this.statusSig();
    return this.tickets().filter(
      t =>
        (!s || t.status === s) &&
        (!q || [t.number, t.subject, t.customerName, t.category].some(v => v.toLowerCase().includes(q)))
    );
  });

  constructor() {
    this.load();
  }

  protected async changeStatus(ticket: Ticket, status: TicketStatus): Promise<void> {
    try {
      await this.api.updateTicketStatus(ticket.id, status);
      this.tickets.update(list => list.map(t => (t.id === ticket.id ? { ...t, status } : t)));
    } catch (err) {
      this.error.set(errorMessage(err));
      await this.load();
    }
  }

  private async load(): Promise<void> {
    try {
      this.tickets.set(await this.api.allTickets());
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }
}
