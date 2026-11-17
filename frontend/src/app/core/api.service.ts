import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  Account,
  AnalyticsOverview,
  ConversationDetail,
  ConversationStatus,
  ConversationSummary,
  KnowledgeArticle,
  KnowledgeHit,
  Ticket,
  TicketStatus
} from './models';

export interface ArticleInput {
  title: string;
  category: string;
  language: 'en' | 'ar';
  content: string;
}

/** Typed wrapper around the REST API (promises keep component code simple). */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  // ----- customer
  startConversation(language: string) {
    return firstValueFrom(this.http.post<ConversationDetail>('/api/conversations/start', { language }));
  }
  getConversation(id: string) {
    return firstValueFrom(this.http.get<ConversationDetail>(`/api/conversations/${id}`));
  }
  requestHuman(id: string) {
    return firstValueFrom(this.http.post<void>(`/api/conversations/${id}/human`, {}));
  }
  closeConversation(id: string) {
    return firstValueFrom(this.http.post<void>(`/api/conversations/${id}/close`, {}));
  }
  myAccount() {
    return firstValueFrom(this.http.get<Account>('/api/account/me'));
  }
  myTickets() {
    return firstValueFrom(this.http.get<Ticket[]>('/api/tickets/mine'));
  }

  // ----- agent
  queue() {
    return firstValueFrom(this.http.get<ConversationSummary[]>('/api/agent/queue'));
  }
  recentConversations(status?: ConversationStatus) {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return firstValueFrom(this.http.get<ConversationSummary[]>('/api/agent/conversations', { params }));
  }
  claim(id: string) {
    return firstValueFrom(this.http.post<void>(`/api/agent/conversations/${id}/claim`, {}));
  }
  resolve(id: string) {
    return firstValueFrom(this.http.post<void>(`/api/agent/conversations/${id}/resolve`, {}));
  }
  summarize(id: string) {
    return firstValueFrom(this.http.post<{ text: string }>(`/api/agent/conversations/${id}/summary`, {}));
  }
  suggestReply(id: string) {
    return firstValueFrom(this.http.post<{ text: string }>(`/api/agent/conversations/${id}/suggest-reply`, {}));
  }
  allTickets(status?: TicketStatus) {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return firstValueFrom(this.http.get<Ticket[]>('/api/tickets', { params }));
  }
  updateTicketStatus(id: string, status: TicketStatus) {
    return firstValueFrom(this.http.patch<void>(`/api/tickets/${id}/status`, { status }));
  }

  // ----- knowledge base
  articles() {
    return firstValueFrom(this.http.get<KnowledgeArticle[]>('/api/knowledge'));
  }
  createArticle(input: ArticleInput) {
    return firstValueFrom(this.http.post<KnowledgeArticle>('/api/knowledge', input));
  }
  updateArticle(id: string, input: ArticleInput) {
    return firstValueFrom(this.http.put<void>(`/api/knowledge/${id}`, input));
  }
  uploadArticle(file: File, category: string, language: string) {
    const form = new FormData();
    form.append('file', file);
    form.append('category', category);
    form.append('language', language);
    return firstValueFrom(this.http.post<KnowledgeArticle>('/api/knowledge/upload', form));
  }
  reindexArticle(id: string) {
    return firstValueFrom(this.http.post<void>(`/api/knowledge/${id}/reindex`, {}));
  }
  deleteArticle(id: string) {
    return firstValueFrom(this.http.delete<void>(`/api/knowledge/${id}`));
  }
  searchKnowledge(q: string) {
    return firstValueFrom(this.http.get<KnowledgeHit[]>('/api/knowledge/search', { params: { q } }));
  }

  // ----- analytics
  analytics(days = 30) {
    return firstValueFrom(this.http.get<AnalyticsOverview>('/api/analytics/overview', { params: { days } }));
  }
}

/** Pulls the human-readable message out of an API error (ProblemDetails). */
export function errorMessage(err: unknown, fallback = 'Something went wrong.'): string {
  const e = err as { error?: { title?: string; detail?: string }; message?: string };
  return e?.error?.detail || e?.error?.title || e?.message || fallback;
}
