import { TestBed } from '@angular/core/testing';
import { I18nService } from './i18n.service';
import { hubError } from './realtime.service';
import { MarkdownPipe } from '../shared/markdown.pipe';
import { TOOL_LABELS } from './models';

describe('I18nService', () => {
  beforeEach(() => localStorage.clear());

  it('switches language and page direction', () => {
    const i18n = TestBed.inject(I18nService);
    expect(i18n.lang()).toBe('en');
    expect(i18n.t('send')).toBe('Send');

    i18n.toggle();
    TestBed.tick();

    expect(i18n.lang()).toBe('ar');
    expect(i18n.dir()).toBe('rtl');
    expect(i18n.t('send')).toBe('إرسال');
    expect(document.documentElement.dir).toBe('rtl');
  });
});

describe('hubError', () => {
  it('extracts the server message from a SignalR HubException', () => {
    const err = new Error("An unexpected error occurred invoking 'SendCustomerMessage' on the server. HubException: This conversation is closed.");
    expect(hubError(err)).toBe('This conversation is closed.');
  });

  it('falls back to a friendly message', () => {
    expect(hubError(new Error('WebSocket failed'))).toContain('Connection problem');
  });
});

describe('MarkdownPipe', () => {
  it('renders bold text and lists from AI replies', () => {
    const html = new MarkdownPipe().transform('**Smart 150**\n\n- 50 GB\n- 5G');
    expect(html).toContain('<strong>Smart 150</strong>');
    expect(html).toContain('<li>50 GB</li>');
  });

  it('returns empty string for empty input', () => {
    expect(new MarkdownPipe().transform(null)).toBe('');
  });
});

describe('TOOL_LABELS', () => {
  it('has English and Arabic labels for every AI tool', () => {
    const tools = [
      'get_account_summary', 'get_recent_bills', 'list_available_plans', 'change_plan', 'set_roaming',
      'create_support_ticket', 'get_my_tickets', 'search_knowledge_base', 'escalate_to_human_agent'
    ];
    for (const tool of tools) {
      expect(TOOL_LABELS[tool]?.en).toBeTruthy();
      expect(TOOL_LABELS[tool]?.ar).toBeTruthy();
    }
  });
});
