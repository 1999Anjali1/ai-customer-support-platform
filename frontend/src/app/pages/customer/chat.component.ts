import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, ElementRef, OnDestroy, computed, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../../core/api.service';
import { I18nService } from '../../core/i18n.service';
import { Account, ConversationStatus, Message, TOOL_LABELS } from '../../core/models';
import { RealtimeService, hubError } from '../../core/realtime.service';
import { MarkdownPipe } from '../../shared/markdown.pipe';

const SUGGESTIONS = {
  en: [
    'What is my bill this month?',
    'I am travelling to Saudi Arabia next week – what roaming pack do you recommend?',
    'Can I upgrade to a plan with more data?',
    'My mobile data is very slow at home',
    'How do I activate eSIM?'
  ],
  ar: ['كم فاتورتي هذا الشهر؟', 'مسافر إلى السعودية الأسبوع القادم، ما هي باقة التجوال المناسبة؟', 'أريد باقة فيها بيانات أكثر', 'الإنترنت بطيء جداً في البيت']
};

@Component({
  selector: 'app-chat',
  imports: [FormsModule, MarkdownPipe, DatePipe, DecimalPipe],
  template: `
    <div class="chat-layout">
      <section class="chat-panel card">
        <header class="chat-header">
          <div class="row gap">
            <span class="bot-avatar">AI</span>
            <div>
              <strong>{{ i18n.t('assistant') }}</strong>
              <div class="status-line">
                @switch (status()) {
                  @case ('Bot') { <span class="dot green"></span> {{ i18n.t('online') }} }
                  @case ('WaitingForAgent') { <span class="dot amber"></span> {{ i18n.t('agentWillJoin') }} }
                  @case ('WithAgent') { <span class="dot blue"></span> {{ i18n.t('withAgent') }} {{ agentName() }} }
                  @case ('Resolved') { <span class="dot grey"></span> {{ i18n.t('chatClosed') }} }
                }
              </div>
            </div>
          </div>
          <div class="row gap">
            @if (status() === 'Bot') {
              <button class="btn ghost small" (click)="talkToHuman()" [disabled]="!conversationId()">🙋 {{ i18n.t('talkToHuman') }}</button>
            }
            @if (status() !== 'Resolved') {
              <button class="btn ghost small" (click)="endChat()" [disabled]="!conversationId()">{{ i18n.t('endChat') }}</button>
            } @else {
              <button class="btn primary small" (click)="start()">{{ i18n.t('newChat') }}</button>
            }
          </div>
        </header>

        <div class="messages" #scroller>
          @for (m of messages(); track m.id) {
            @if (m.sender === 'System') {
              <div class="system-note">{{ m.content }}</div>
            } @else {
              <div class="msg" [class.mine]="m.sender === 'Customer'" [class.agent]="m.sender === 'Agent'">
                <div class="bubble" dir="auto">
                  @if (m.sender === 'Agent') {
                    <div class="sender">🎧 {{ m.senderName ?? 'Agent' }}</div>
                  }
                  @if (m.sender === 'Customer') {
                    <div class="text">{{ m.content }}</div>
                  } @else {
                    <div class="text md" [innerHTML]="m.content | markdown"></div>
                    @if (m.streaming) { <span class="cursor"></span> }
                  }
                </div>
                @if (m.toolsUsed.length || m.sources.length) {
                  <div class="msg-meta">
                    @for (t of m.toolsUsed; track t) {
                      <span class="chip" [title]="t">{{ toolLabel(t) }}</span>
                    }
                    @if (m.sources.length) {
                      <span class="sources">📚 {{ i18n.t('sources') }}: {{ m.sources.join(' · ') }}</span>
                    }
                  </div>
                }
                <time class="time">{{ m.createdAt | date: 'shortTime' }}</time>
              </div>
            }
          }

          @if (thinking() && !streamingNow()) {
            <div class="msg">
              <div class="bubble thinking">
                <span class="typing"><i></i><i></i><i></i></span>
                {{ activeTool() ? toolLabel(activeTool()!) + '…' : i18n.t('aiThinking') }}
              </div>
            </div>
          }
        </div>

        @if (showSuggestions()) {
          <div class="suggestions">
            <small class="muted">{{ i18n.t('suggestions') }}:</small>
            @for (s of suggestions(); track s) {
              <button class="chip clickable" (click)="send(s)">{{ s }}</button>
            }
          </div>
        }

        @if (error()) {
          <p class="alert error slim">{{ error() }}</p>
        }

        <form class="composer" (ngSubmit)="send()">
          <input
            name="draft"
            dir="auto"
            [(ngModel)]="draft"
            [placeholder]="i18n.t('typeMessage')"
            [disabled]="status() === 'Resolved' || !conversationId()"
            maxlength="2000"
            autocomplete="off" />
          <button class="btn primary" type="submit" [disabled]="!draft.trim() || thinking() || status() === 'Resolved'">
            {{ i18n.t('send') }}
          </button>
        </form>
      </section>

      <aside class="side-panel">
        @if (account(); as a) {
          <div class="card account-card">
            <h3>{{ i18n.t('myAccount') }}</h3>
            <p class="muted small">{{ a.accountNumber }} · <span dir="ltr">{{ a.mobileNumber }}</span></p>

            <div class="plan-box">
              <small>{{ i18n.t('plan') }}</small>
              <strong>{{ i18n.lang() === 'ar' ? a.plan.nameAr : a.plan.nameEn }}</strong>
              <span>QAR {{ a.plan.monthlyPriceQar | number: '1.0-2' }}/mo @if (a.plan.is5G) { <span class="badge">5G</span> }</span>
            </div>

            <div class="usage">
              <div class="row between small">
                <span>{{ i18n.t('dataUsed') }}</span>
                <span>{{ a.dataUsedGb | number: '1.0-1' }} / {{ a.plan.dataGb ?? '∞' }} GB</span>
              </div>
              <div class="bar"><span [style.width.%]="usagePercent()"></span></div>
            </div>

            <dl class="facts">
              <dt>{{ i18n.t('outstanding') }}</dt>
              <dd [class.danger]="hasOverdue()">QAR {{ a.outstandingQar | number: '1.0-2' }}</dd>
              <dt>{{ i18n.t('roaming') }}</dt>
              <dd>{{ a.isRoamingEnabled ? i18n.t('on') : i18n.t('off') }}</dd>
            </dl>

            <h4>{{ i18n.t('recentBills') }}</h4>
            <ul class="bills">
              @for (b of a.bills.slice(0, 4); track b.id) {
                <li>
                  <span>{{ b.period | date: 'MMM yyyy' }}</span>
                  <span>QAR {{ b.amountQar | number: '1.0-2' }}</span>
                  <span class="status" [class]="'status ' + b.status.toLowerCase()">{{ b.status }}</span>
                </li>
              }
            </ul>
          </div>
        }

        <div class="card how-it-works">
          <h4>{{ i18n.t('howItWorks') }}</h4>
          <p class="small muted">{{ i18n.t('howItWorksText') }}</p>
        </div>
      </aside>
    </div>
  `
})
export class ChatComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly i18n = inject(I18nService);

  private readonly scroller = viewChild<ElementRef<HTMLDivElement>>('scroller');

  protected readonly conversationId = signal<string | null>(null);
  protected readonly status = signal<ConversationStatus>('Bot');
  protected readonly agentName = signal<string | null>(null);
  protected readonly messages = signal<Message[]>([]);
  protected readonly account = signal<Account | null>(null);
  protected readonly thinking = signal(false);
  protected readonly activeTool = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected draft = '';

  protected readonly streamingNow = computed(() => this.messages().some(m => m.streaming));
  protected readonly suggestions = computed(() => SUGGESTIONS[this.i18n.lang()]);
  protected readonly showSuggestions = computed(
    () => this.status() === 'Bot' && !this.thinking() && !this.messages().some(m => m.sender === 'Customer')
  );
  protected readonly hasOverdue = computed(() => this.account()?.bills.some(b => b.status === 'Overdue') ?? false);
  protected readonly usagePercent = computed(() => {
    const a = this.account();
    if (!a || !a.plan.dataGb) return 15;
    return Math.min(100, Math.round((a.dataUsedGb / a.plan.dataGb) * 100));
  });

  constructor() {
    this.subscribeToRealtime();
    this.start();
    this.loadAccount();

    // keep the newest message in view
    effect(() => {
      this.messages();
      this.thinking();
      queueMicrotask(() => {
        const el = this.scroller()?.nativeElement;
        if (el) el.scrollTop = el.scrollHeight;
      });
    });
  }

  ngOnDestroy(): void {
    const id = this.conversationId();
    if (id) this.realtime.leave(id).catch(() => undefined);
  }

  protected async start(): Promise<void> {
    this.error.set(null);
    try {
      const previous = this.conversationId();
      if (previous) await this.realtime.leave(previous).catch(() => undefined);

      const detail = await this.api.startConversation(this.i18n.lang());
      this.conversationId.set(detail.conversation.id);
      this.status.set(detail.conversation.status);
      this.agentName.set(detail.conversation.assignedAgentName);
      this.messages.set(detail.messages);
    } catch (err) {
      this.error.set(errorMessage(err, 'Could not start the chat.'));
      return;
    }
    try {
      await this.realtime.join(this.conversationId()!);
    } catch {
      this.error.set('Live connection unavailable – please refresh the page.');
    }
  }

  protected async send(text?: string): Promise<void> {
    const content = (text ?? this.draft).trim();
    const id = this.conversationId();
    if (!content || !id || this.thinking()) return;

    this.draft = '';
    this.error.set(null);
    if (this.status() === 'Bot') this.thinking.set(true);

    try {
      // Resolves when the server has finished (including the full AI reply).
      await this.realtime.sendCustomerMessage(id, content);
    } catch (err) {
      this.error.set(hubError(err));
    } finally {
      this.thinking.set(false);
      this.activeTool.set(null);
      this.loadAccount(); // the AI may have changed the plan or roaming
    }
  }

  protected async talkToHuman(): Promise<void> {
    const id = this.conversationId();
    if (!id) return;
    try {
      await this.api.requestHuman(id);
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }

  protected async endChat(): Promise<void> {
    const id = this.conversationId();
    if (!id) return;
    try {
      await this.api.closeConversation(id);
      this.status.set('Resolved');
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }

  protected toolLabel(tool: string): string {
    const label = TOOL_LABELS[tool];
    return label ? `${label.icon} ${label[this.i18n.lang()]}` : tool;
  }

  private async loadAccount(): Promise<void> {
    try {
      this.account.set(await this.api.myAccount());
    } catch {
      /* account card is optional */
    }
  }

  private subscribeToRealtime(): void {
    const mine = (conversationId: string) => conversationId === this.conversationId();

    this.realtime.assistantDelta$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(e => {
      if (!mine(e.conversationId)) return;
      this.messages.update(list => {
        const existing = list.find(m => m.id === e.messageId);
        if (existing) {
          return list.map(m => (m.id === e.messageId ? { ...m, content: m.content + e.delta } : m));
        }
        return [
          ...list,
          {
            id: e.messageId,
            conversationId: e.conversationId,
            sender: 'Assistant',
            senderName: null,
            content: e.delta,
            toolsUsed: [],
            sources: [],
            createdAt: new Date().toISOString(),
            streaming: true
          }
        ];
      });
    });

    this.realtime.toolActivity$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(e => {
      if (mine(e.conversationId)) this.activeTool.set(e.tool);
    });

    this.realtime.messageAdded$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(message => {
      if (!mine(message.conversationId)) return;
      this.messages.update(list =>
        list.some(m => m.id === message.id) ? list.map(m => (m.id === message.id ? message : m)) : [...list, message]
      );
      if (message.sender === 'Agent') this.agentName.set(message.senderName);
    });

    this.realtime.conversationUpdated$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(async e => {
      if (!mine(e.conversationId)) return;
      this.status.set(e.status);
      if (e.status === 'WithAgent') {
        const detail = await this.api.getConversation(e.conversationId);
        this.agentName.set(detail.conversation.assignedAgentName);
      }
    });
  }
}
