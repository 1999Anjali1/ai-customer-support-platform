import { DatePipe } from '@angular/common';
import { Component, DestroyRef, ElementRef, OnDestroy, computed, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { debounceTime } from 'rxjs';
import { ApiService, errorMessage } from '../../core/api.service';
import { ConversationDetail, ConversationSummary, TOOL_LABELS } from '../../core/models';
import { RealtimeService, hubError } from '../../core/realtime.service';
import { MarkdownPipe } from '../../shared/markdown.pipe';

/**
 * Human agent workspace:
 *  left   – live queue of escalated chats (updates instantly via SignalR)
 *  centre – the conversation, including what the AI already said and which tools it used
 *  right  – AI copilot: handover summary + suggested reply
 */
@Component({
  selector: 'app-agent-console',
  imports: [FormsModule, DatePipe, MarkdownPipe],
  template: `
    <div class="console">
      <aside class="queue card">
        <div class="row between">
          <h3>Queue</h3>
          <span class="badge">{{ waitingCount() }} waiting</span>
        </div>
        @if (queue().length === 0) {
          <p class="muted small empty">No escalated chats right now. 🎉<br />Log in as a customer in another window and press “Talk to a human”.</p>
        }
        @for (c of queue(); track c.id) {
          <button class="queue-item" [class.selected]="c.id === selectedId()" (click)="open(c.id)">
            <div class="row between">
              <strong>{{ c.customerName }}</strong>
              <span class="status" [class]="'status ' + c.status.toLowerCase()">{{ statusLabel(c.status) }}</span>
            </div>
            <p class="small reason" dir="auto">{{ c.escalationReason }}</p>
            <p class="small muted ellipsis" dir="auto">{{ c.lastMessage }}</p>
            <small class="muted">{{ c.escalatedAt | date: 'shortTime' }} · {{ c.language === 'ar' ? 'Arabic' : 'English' }}</small>
          </button>
        }
      </aside>

      <section class="card chat-panel">
        @if (detail(); as d) {
          <header class="chat-header">
            <div>
              <strong>{{ d.conversation.customerName }}</strong>
              <div class="status-line small muted">
                <span dir="ltr">{{ d.conversation.customerMobile }}</span> ·
                {{ statusLabel(d.conversation.status) }}
                @if (d.conversation.assignedAgentName) { · {{ d.conversation.assignedAgentName }} }
              </div>
            </div>
            <div class="row gap">
              @if (d.conversation.status === 'WaitingForAgent') {
                <button class="btn primary small" (click)="claim()">Accept chat</button>
              }
              @if (d.conversation.status !== 'Resolved') {
                <button class="btn ghost small" (click)="resolve()">✔ Resolve</button>
              }
            </div>
          </header>

          <div class="messages" #scroller>
            @for (m of d.messages; track m.id) {
              @if (m.sender === 'System') {
                <div class="system-note">{{ m.content }}</div>
              } @else {
                <div class="msg" [class.mine]="m.sender === 'Agent'" [class.ai]="m.sender === 'Assistant'">
                  <div class="bubble" dir="auto">
                    <div class="sender">
                      {{ m.sender === 'Customer' ? '👤 ' + d.conversation.customerName : m.sender === 'Assistant' ? '🤖 AI' : '🎧 ' + (m.senderName ?? 'Agent') }}
                    </div>
                    <div class="text md" [innerHTML]="m.content | markdown"></div>
                  </div>
                  @if (m.toolsUsed.length) {
                    <div class="msg-meta">
                      @for (t of m.toolsUsed; track t) { <span class="chip">{{ toolLabel(t) }}</span> }
                    </div>
                  }
                  <time class="time">{{ m.createdAt | date: 'shortTime' }}</time>
                </div>
              }
            }
          </div>

          @if (error()) { <p class="alert error slim">{{ error() }}</p> }

          <form class="composer" (ngSubmit)="send()">
            <textarea
              name="reply"
              rows="2"
              dir="auto"
              [(ngModel)]="reply"
              placeholder="Write a reply…  (Ctrl+Enter to send)"
              (keydown.control.enter)="send()"
              [disabled]="d.conversation.status === 'Resolved'"></textarea>
            <button class="btn primary" type="submit" [disabled]="!reply.trim() || d.conversation.status === 'Resolved'">Send</button>
          </form>
        } @else {
          <div class="empty-state">
            <span>🎧</span>
            <p>Select a conversation from the queue.</p>
          </div>
        }
      </section>

      <aside class="copilot">
        <div class="card">
          <div class="row between">
            <h3>🤖 AI handover summary</h3>
            <button class="btn ghost small" (click)="summarize()" [disabled]="!selectedId() || busy() === 'summary'">
              {{ busy() === 'summary' ? '…' : summary() ? 'Refresh' : 'Generate' }}
            </button>
          </div>
          @if (summary()) {
            <div class="md small" [innerHTML]="summary() | markdown"></div>
          } @else {
            <p class="muted small">Get a short brief of the issue, what the AI already tried and the customer's mood – no need to read the whole chat.</p>
          }
        </div>

        <div class="card">
          <div class="row between">
            <h3>✍️ Suggested reply</h3>
            <button class="btn ghost small" (click)="suggest()" [disabled]="!selectedId() || busy() === 'suggest'">
              {{ busy() === 'suggest' ? '…' : 'Suggest' }}
            </button>
          </div>
          @if (suggestion()) {
            <p class="suggestion" dir="auto">{{ suggestion() }}</p>
            <button class="btn small" (click)="useSuggestion()">Use this reply</button>
          } @else {
            <p class="muted small">Drafted from the conversation and the knowledge base, in the customer's language. Always review before sending.</p>
          }
        </div>
      </aside>
    </div>
  `
})
export class AgentConsoleComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly scroller = viewChild<ElementRef<HTMLDivElement>>('scroller');

  protected readonly queue = signal<ConversationSummary[]>([]);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly detail = signal<ConversationDetail | null>(null);
  protected readonly summary = signal<string | null>(null);
  protected readonly suggestion = signal<string | null>(null);
  protected readonly busy = signal<'summary' | 'suggest' | null>(null);
  protected readonly error = signal<string | null>(null);
  protected reply = '';

  protected readonly waitingCount = computed(() => this.queue().filter(c => c.status === 'WaitingForAgent').length);

  constructor() {
    this.loadQueue();

    // Any status change (new escalation, claim, resolve) → refresh the queue (debounced).
    this.realtime.conversationUpdated$
      .pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef))
      .subscribe(e => {
        this.loadQueue();
        if (e.conversationId === this.selectedId()) this.reload();
      });

    this.realtime.messageAdded$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(message => {
      const d = this.detail();
      if (!d || message.conversationId !== d.conversation.id) return;
      if (d.messages.some(m => m.id === message.id)) return;
      this.detail.set({ ...d, messages: [...d.messages, message] });
    });

    effect(() => {
      this.detail();
      queueMicrotask(() => {
        const el = this.scroller()?.nativeElement;
        if (el) el.scrollTop = el.scrollHeight;
      });
    });
  }

  ngOnDestroy(): void {
    const id = this.selectedId();
    if (id) this.realtime.leave(id).catch(() => undefined);
  }

  protected async open(id: string): Promise<void> {
    const previous = this.selectedId();
    if (previous === id) return;
    if (previous) await this.realtime.leave(previous).catch(() => undefined);

    this.selectedId.set(id);
    this.suggestion.set(null);
    this.error.set(null);
    await this.reload();
    await this.realtime.join(id);
  }

  protected async claim(): Promise<void> {
    await this.run(() => this.api.claim(this.selectedId()!));
  }

  protected async resolve(): Promise<void> {
    await this.run(() => this.api.resolve(this.selectedId()!));
  }

  protected async send(): Promise<void> {
    const text = this.reply.trim();
    const id = this.selectedId();
    if (!text || !id) return;
    this.reply = '';
    try {
      await this.realtime.sendAgentMessage(id, text);
    } catch (err) {
      this.reply = text;
      this.error.set(hubError(err));
    }
  }

  protected async summarize(): Promise<void> {
    this.busy.set('summary');
    try {
      this.summary.set((await this.api.summarize(this.selectedId()!)).text);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(null);
    }
  }

  protected async suggest(): Promise<void> {
    this.busy.set('suggest');
    try {
      this.suggestion.set((await this.api.suggestReply(this.selectedId()!)).text || null);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(null);
    }
  }

  protected useSuggestion(): void {
    this.reply = this.suggestion() ?? '';
  }

  protected statusLabel(status: string): string {
    return { Bot: 'AI', WaitingForAgent: 'Waiting', WithAgent: 'In progress', Resolved: 'Resolved' }[status] ?? status;
  }

  protected toolLabel(tool: string): string {
    const label = TOOL_LABELS[tool];
    return label ? `${label.icon} ${label.en}` : tool;
  }

  private async loadQueue(): Promise<void> {
    try {
      this.queue.set(await this.api.queue());
    } catch {
      /* ignore */
    }
  }

  private async reload(): Promise<void> {
    const id = this.selectedId();
    if (!id) return;
    const detail = await this.api.getConversation(id);
    this.detail.set(detail);
    this.summary.set(detail.summary);
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.error.set(null);
    try {
      await action();
      await Promise.all([this.reload(), this.loadQueue()]);
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }
}
