import { DecimalPipe } from '@angular/common';
import { AfterViewInit, Component, ElementRef, OnDestroy, inject, signal, viewChild } from '@angular/core';
import { Chart, registerables } from 'chart.js';
import { ApiService, errorMessage } from '../../core/api.service';
import { AnalyticsOverview, TOOL_LABELS } from '../../core/models';

Chart.register(...registerables);

const COLORS = {
  maroon: '#8a1538',
  teal: '#0f766e',
  amber: '#d97706',
  slate: '#64748b',
  green: '#16a34a',
  red: '#dc2626'
};

@Component({
  selector: 'app-analytics',
  imports: [DecimalPipe],
  template: `
    <div class="page-header">
      <div>
        <h1>Support analytics</h1>
        <p class="muted">How much the AI resolves on its own, and how customers feel.</p>
      </div>
      <div class="segmented">
        @for (d of [7, 14, 30]; track d) {
          <button [class.active]="days() === d" (click)="load(d)">{{ d }} days</button>
        }
      </div>
    </div>

    @if (error()) { <p class="alert error">{{ error() }}</p> }

    @if (data(); as d) {
      <div class="kpis">
        <div class="kpi card">
          <small>Conversations</small>
          <strong>{{ d.totalConversations }}</strong>
        </div>
        <div class="kpi card highlight">
          <small>Resolved by AI</small>
          <strong>{{ d.aiResolutionRate | number: '1.0-1' }}%</strong>
          <span class="muted small">{{ d.resolvedByAi }} without a human</span>
        </div>
        <div class="kpi card">
          <small>Escalated to agents</small>
          <strong>{{ d.escalated }}</strong>
          <span class="muted small">
            @if (d.averageMinutesToEscalation !== null) { avg {{ d.averageMinutesToEscalation | number: '1.0-1' }} min to hand-over }
          </span>
        </div>
        <div class="kpi card">
          <small>Waiting in queue</small>
          <strong>{{ d.waitingInQueue }}</strong>
        </div>
        <div class="kpi card">
          <small>Open tickets</small>
          <strong>{{ d.openTickets }}</strong>
          <span class="muted small">{{ d.ticketsCreatedByAi }} opened by AI</span>
        </div>
      </div>
    }

    <div class="charts">
      <div class="card chart-card wide">
        <h3>Conversations per day</h3>
        <div class="chart-box"><canvas #daily></canvas></div>
      </div>
      <div class="card chart-card">
        <h3>Customer sentiment</h3>
        <div class="chart-box"><canvas #sentiment></canvas></div>
      </div>
      <div class="card chart-card">
        <h3>Top topics</h3>
        <div class="chart-box"><canvas #topics></canvas></div>
      </div>
      <div class="card chart-card">
        <h3>AI tools used</h3>
        <div class="chart-box"><canvas #tools></canvas></div>
      </div>
    </div>
  `
})
export class AnalyticsComponent implements AfterViewInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly dailyCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('daily');
  private readonly sentimentCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('sentiment');
  private readonly topicsCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('topics');
  private readonly toolsCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('tools');
  private charts: Chart[] = [];

  protected readonly data = signal<AnalyticsOverview | null>(null);
  protected readonly days = signal(30);
  protected readonly error = signal<string | null>(null);

  ngAfterViewInit(): void {
    this.load(30);
  }

  ngOnDestroy(): void {
    this.charts.forEach(c => c.destroy());
  }

  protected async load(days: number): Promise<void> {
    this.days.set(days);
    try {
      const data = await this.api.analytics(days);
      this.data.set(data);
      this.render(data);
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }

  private render(d: AnalyticsOverview): void {
    this.charts.forEach(c => c.destroy());
    const grid = { color: 'rgba(100,116,139,.15)' };

    this.charts = [
      new Chart(this.dailyCanvas().nativeElement, {
        type: 'line',
        data: {
          labels: d.daily.map(x => new Date(x.date).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })),
          datasets: [
            { label: 'Conversations', data: d.daily.map(x => x.conversations), borderColor: COLORS.teal, backgroundColor: 'rgba(15,118,110,.12)', fill: true, tension: 0.35 },
            { label: 'Escalations', data: d.daily.map(x => x.escalations), borderColor: COLORS.maroon, backgroundColor: COLORS.maroon, tension: 0.35 }
          ]
        },
        options: { maintainAspectRatio: false, scales: { y: { beginAtZero: true, ticks: { precision: 0 }, grid }, x: { grid: { display: false } } } }
      }),
      new Chart(this.sentimentCanvas().nativeElement, {
        type: 'doughnut',
        data: {
          labels: d.sentiments.map(s => s.label),
          datasets: [{ data: d.sentiments.map(s => s.count), backgroundColor: [COLORS.green, COLORS.slate, COLORS.red], borderWidth: 0 }]
        },
        options: { maintainAspectRatio: false, cutout: '65%', plugins: { legend: { position: 'bottom' } } }
      }),
      new Chart(this.topicsCanvas().nativeElement, {
        type: 'bar',
        data: {
          labels: d.topics.map(t => t.label),
          datasets: [{ label: 'Conversations', data: d.topics.map(t => t.count), backgroundColor: COLORS.maroon, borderRadius: 6 }]
        },
        options: { maintainAspectRatio: false, indexAxis: 'y', plugins: { legend: { display: false } }, scales: { x: { beginAtZero: true, ticks: { precision: 0 }, grid } } }
      }),
      new Chart(this.toolsCanvas().nativeElement, {
        type: 'bar',
        data: {
          labels: d.toolUsage.map(t => TOOL_LABELS[t.label]?.en ?? t.label),
          datasets: [{ label: 'Calls', data: d.toolUsage.map(t => t.count), backgroundColor: COLORS.teal, borderRadius: 6 }]
        },
        options: { maintainAspectRatio: false, indexAxis: 'y', plugins: { legend: { display: false } }, scales: { x: { beginAtZero: true, ticks: { precision: 0 }, grid } } }
      })
    ];
  }
}
