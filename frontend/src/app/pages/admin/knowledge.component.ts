import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, ArticleInput, errorMessage } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { KnowledgeArticle, KnowledgeHit } from '../../core/models';

/**
 * Admin page for the help-centre articles the AI uses (RAG).
 * New/edited articles are chunked and embedded by a background worker on the server.
 */
@Component({
  selector: 'app-knowledge',
  imports: [FormsModule, DatePipe],
  template: `
    <div class="page-header">
      <div>
        <h1>Knowledge base</h1>
        <p class="muted">Articles the AI searches before answering policy questions. {{ articles().length }} articles · {{ totalChunks() }} chunks indexed.</p>
      </div>
      @if (isAdmin()) {
        <div class="row gap">
          <label class="btn ghost">
            ⬆ Upload .md / .txt
            <input type="file" accept=".md,.txt" hidden (change)="upload($event)" />
          </label>
          <button class="btn primary" (click)="newArticle()">+ New article</button>
        </div>
      }
    </div>

    @if (error()) { <p class="alert error">{{ error() }}</p> }

    <div class="kb-layout">
      <section class="card">
        <h3>🔎 Test retrieval</h3>
        <p class="muted small">Type a customer question to see which chunks the AI would receive (hybrid vector + keyword search).</p>
        <form class="row gap" (ngSubmit)="search()">
          <input name="q" class="grow" dir="auto" [(ngModel)]="query" placeholder="e.g. I'm going to Riyadh, how much is roaming?" />
          <button class="btn" type="submit" [disabled]="!query.trim()">Search</button>
        </form>
        @if (hits(); as list) {
          @if (list.length === 0) {
            <p class="muted small">No relevant chunks found.</p>
          }
          @for (h of list; track $index) {
            <div class="hit">
              <div class="row between small">
                <strong>{{ h.articleTitle }}</strong>
                <span class="muted">score {{ h.score }} · {{ h.matchedBy }}</span>
              </div>
              <p class="small" dir="auto">{{ h.content }}</p>
            </div>
          }
        }
      </section>

      @if (editing(); as form) {
        <section class="card">
          <h3>{{ form.id ? 'Edit article' : 'New article' }}</h3>
          <form class="stack" (ngSubmit)="save()">
            <label>Title <input name="title" [(ngModel)]="form.title" required dir="auto" /></label>
            <div class="row gap">
              <label class="grow">Category
                <input name="category" [(ngModel)]="form.category" list="categories" required />
                <datalist id="categories">
                  @for (c of categories(); track c) { <option [value]="c"></option> }
                </datalist>
              </label>
              <label>Language
                <select name="language" [(ngModel)]="form.language">
                  <option value="en">English</option>
                  <option value="ar">Arabic</option>
                </select>
              </label>
            </div>
            <label>Content (Markdown supported)
              <textarea name="content" rows="12" [(ngModel)]="form.content" required dir="auto"></textarea>
            </label>
            <div class="row gap">
              <button class="btn primary" type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save & index' }}</button>
              <button class="btn ghost" type="button" (click)="editing.set(null)">Cancel</button>
            </div>
          </form>
        </section>
      }
    </div>

    <div class="card table-card">
      <table>
        <thead>
          <tr><th>Title</th><th>Category</th><th>Lang</th><th>Chunks</th><th>Status</th><th>Indexed</th><th></th></tr>
        </thead>
        <tbody>
          @for (a of articles(); track a.id) {
            <tr>
              <td dir="auto"><strong>{{ a.title }}</strong></td>
              <td><span class="badge">{{ a.category }}</span></td>
              <td>{{ a.language.toUpperCase() }}</td>
              <td>{{ a.chunkCount }}</td>
              <td>
                <span class="status" [class]="'status ' + a.status.toLowerCase()" [title]="a.indexingError ?? ''">{{ a.status }}</span>
                @if (a.indexingError) { <span class="warn-icon" [title]="a.indexingError">⚠️</span> }
              </td>
              <td class="muted small">{{ a.indexedAt ? (a.indexedAt | date: 'MMM d, HH:mm') : '—' }}</td>
              <td class="actions">
                @if (isAdmin()) {
                  <button class="btn ghost small" (click)="edit(a)">Edit</button>
                  <button class="btn ghost small" (click)="reindex(a)">Re-index</button>
                  <button class="btn ghost small danger" (click)="remove(a)">Delete</button>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `
})
export class KnowledgeComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  protected readonly articles = signal<KnowledgeArticle[]>([]);
  protected readonly hits = signal<KnowledgeHit[] | null>(null);
  protected readonly editing = signal<(ArticleInput & { id?: string }) | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected query = '';

  protected readonly isAdmin = computed(() => this.auth.role() === 'Admin');
  protected readonly totalChunks = computed(() => this.articles().reduce((sum, a) => sum + a.chunkCount, 0));
  protected readonly categories = computed(() => [...new Set(this.articles().map(a => a.category))].sort());

  constructor() {
    this.load();
  }

  protected newArticle(): void {
    this.editing.set({ title: '', category: '', language: 'en', content: '' });
  }

  protected edit(a: KnowledgeArticle): void {
    this.editing.set({ id: a.id, title: a.title, category: a.category, language: a.language, content: a.content });
  }

  protected async save(): Promise<void> {
    const form = this.editing();
    if (!form) return;
    this.saving.set(true);
    this.error.set(null);
    try {
      const { id, ...input } = form;
      if (id) await this.api.updateArticle(id, input);
      else await this.api.createArticle(input);
      this.editing.set(null);
      await this.loadUntilIndexed();
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.saving.set(false);
    }
  }

  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    const category = prompt('Category for this article (e.g. Roaming, Billing):', 'General');
    if (!category) return;
    try {
      await this.api.uploadArticle(file, category, 'en');
      await this.loadUntilIndexed();
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }

  protected async reindex(a: KnowledgeArticle): Promise<void> {
    await this.api.reindexArticle(a.id);
    await this.loadUntilIndexed();
  }

  protected async remove(a: KnowledgeArticle): Promise<void> {
    if (!confirm(`Delete "${a.title}"? The AI will no longer use it.`)) return;
    await this.api.deleteArticle(a.id);
    await this.load();
  }

  protected async search(): Promise<void> {
    try {
      this.hits.set(await this.api.searchKnowledge(this.query));
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }

  private async load(): Promise<void> {
    try {
      this.articles.set(await this.api.articles());
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }

  /** Indexing runs in the background – poll briefly so the status flips to "Indexed" on screen. */
  private async loadUntilIndexed(): Promise<void> {
    for (let i = 0; i < 10; i++) {
      await this.load();
      if (!this.articles().some(a => a.status === 'Pending')) return;
      await new Promise(r => setTimeout(r, 1500));
    }
  }
}
