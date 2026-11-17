import { Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AuthService } from './auth.service';
import { ConversationStatus, Message } from './models';

export interface AssistantDelta {
  conversationId: string;
  messageId: string;
  delta: string;
}

export interface ToolActivity {
  conversationId: string;
  tool: string;
}

export interface ConversationUpdate {
  conversationId: string;
  status: ConversationStatus;
}

/**
 * One SignalR connection per browser tab. Components subscribe to the streams they need.
 * The server pushes: AI tokens (assistantDelta), tool usage, new messages and status changes.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;
  private starting: Promise<void> | null = null;
  private readonly joined = new Set<string>();

  readonly state = signal<'disconnected' | 'connecting' | 'connected' | 'reconnecting'>('disconnected');

  readonly assistantDelta$ = new Subject<AssistantDelta>();
  readonly toolActivity$ = new Subject<ToolActivity>();
  readonly messageAdded$ = new Subject<Message>();
  readonly conversationUpdated$ = new Subject<ConversationUpdate>();

  async connect(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) return;
    if (this.starting) return this.starting;

    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/support', { accessTokenFactory: () => this.auth.token() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('assistantDelta', (e: AssistantDelta) => this.assistantDelta$.next(e));
    this.connection.on('toolActivity', (e: ToolActivity) => this.toolActivity$.next(e));
    this.connection.on('messageAdded', (m: Message) => this.messageAdded$.next(m));
    this.connection.on('conversationUpdated', (e: ConversationUpdate) => this.conversationUpdated$.next(e));

    this.connection.onreconnecting(() => this.state.set('reconnecting'));
    this.connection.onreconnected(async () => {
      this.state.set('connected');
      // groups are per-connection, so join them again after a reconnect
      for (const id of this.joined) await this.connection!.invoke('JoinConversation', id);
    });
    this.connection.onclose(() => this.state.set('disconnected'));

    this.state.set('connecting');
    this.starting = this.connection
      .start()
      .then(() => this.state.set('connected'))
      .finally(() => (this.starting = null));
    return this.starting;
  }

  async join(conversationId: string): Promise<void> {
    await this.connect();
    await this.connection!.invoke('JoinConversation', conversationId);
    this.joined.add(conversationId);
  }

  async leave(conversationId: string): Promise<void> {
    this.joined.delete(conversationId);
    if (this.connection?.state === HubConnectionState.Connected)
      await this.connection.invoke('LeaveConversation', conversationId);
  }

  async sendCustomerMessage(conversationId: string, text: string): Promise<void> {
    await this.connect();
    await this.connection!.invoke('SendCustomerMessage', conversationId, text);
  }

  async sendAgentMessage(conversationId: string, text: string): Promise<void> {
    await this.connect();
    await this.connection!.invoke('SendAgentMessage', conversationId, text);
  }

  async disconnect(): Promise<void> {
    this.joined.clear();
    await this.connection?.stop();
    this.connection = null;
  }
}

/** SignalR wraps server HubExceptions as "An unexpected error occurred invoking 'X' on the server. HubException: msg". */
export function hubError(err: unknown): string {
  const text = (err as Error)?.message ?? String(err);
  const idx = text.indexOf('HubException:');
  return idx >= 0 ? text.slice(idx + 'HubException:'.length).trim() : 'Connection problem – please try again.';
}
