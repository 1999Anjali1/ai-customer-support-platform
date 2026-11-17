import { DOCUMENT, Injectable, computed, effect, inject, signal } from '@angular/core';

export type Lang = 'en' | 'ar';

const STRINGS = {
  en: {
    appTagline: 'AI customer support for Sahab Telecom',
    chat: 'Support chat',
    myTickets: 'My tickets',
    agentConsole: 'Agent console',
    tickets: 'Tickets',
    knowledge: 'Knowledge base',
    analytics: 'Analytics',
    logout: 'Sign out',
    typeMessage: 'Type your message…',
    send: 'Send',
    talkToHuman: 'Talk to a human',
    endChat: 'End chat',
    newChat: 'Start new chat',
    aiThinking: 'SmartSupport is thinking…',
    agentWillJoin: 'Waiting for an agent to join…',
    withAgent: 'You are chatting with',
    chatClosed: 'This chat is closed.',
    sources: 'Sources',
    myAccount: 'My account',
    plan: 'Plan',
    dataUsed: 'Data used',
    unlimited: 'Unlimited',
    outstanding: 'Outstanding',
    roaming: 'Roaming',
    on: 'On',
    off: 'Off',
    recentBills: 'Recent bills',
    suggestions: 'Try asking',
    noTickets: 'You have no tickets yet.',
    assistant: 'SmartSupport AI',
    you: 'You',
    system: 'System',
    online: 'Online · replies instantly',
    howItWorks: 'How this demo works',
    howItWorksText:
      'The AI calls secure C# tools to read your account, change plans or open tickets – business rules (like “no plan change with an overdue bill”) are enforced in code. Policy answers come from the help centre via hybrid RAG search.'
  },
  ar: {
    appTagline: 'خدمة عملاء ذكية لسحاب تيليكوم',
    chat: 'محادثة الدعم',
    myTickets: 'تذاكري',
    agentConsole: 'لوحة الموظف',
    tickets: 'التذاكر',
    knowledge: 'قاعدة المعرفة',
    analytics: 'التحليلات',
    logout: 'تسجيل الخروج',
    typeMessage: 'اكتب رسالتك…',
    send: 'إرسال',
    talkToHuman: 'التحدث مع موظف',
    endChat: 'إنهاء المحادثة',
    newChat: 'بدء محادثة جديدة',
    aiThinking: 'المساعد الذكي يفكر…',
    agentWillJoin: 'بانتظار انضمام أحد الموظفين…',
    withAgent: 'أنت تتحدث مع',
    chatClosed: 'تم إغلاق هذه المحادثة.',
    sources: 'المصادر',
    myAccount: 'حسابي',
    plan: 'الباقة',
    dataUsed: 'البيانات المستخدمة',
    unlimited: 'غير محدود',
    outstanding: 'المبلغ المستحق',
    roaming: 'التجوال',
    on: 'مفعّل',
    off: 'غير مفعّل',
    recentBills: 'آخر الفواتير',
    suggestions: 'جرّب أن تسأل',
    noTickets: 'لا توجد تذاكر حتى الآن.',
    assistant: 'المساعد الذكي',
    you: 'أنت',
    system: 'النظام',
    online: 'متصل · يرد فوراً',
    howItWorks: 'كيف يعمل هذا العرض',
    howItWorksText:
      'يستخدم المساعد الذكي أدوات آمنة لقراءة حسابك وتغيير الباقة وفتح التذاكر، وقواعد العمل (مثل منع تغيير الباقة مع وجود فاتورة متأخرة) مطبّقة في الكود. وتأتي الإجابات عن السياسات من مركز المساعدة عبر البحث الذكي (RAG).'
  }
} as const;

export type TranslationKey = keyof (typeof STRINGS)['en'];

/**
 * Tiny signal-based i18n: switches UI text and page direction (RTL for Arabic).
 * Kept deliberately simple – no extra library needed for two languages.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly document = inject(DOCUMENT);
  readonly lang = signal<Lang>(this.load());
  readonly dir = computed(() => (this.lang() === 'ar' ? 'rtl' : 'ltr'));

  constructor() {
    effect(() => {
      const lang = this.lang();
      this.document.documentElement.lang = lang;
      this.document.documentElement.dir = this.dir();
      try {
        localStorage.setItem('ss.lang', lang);
      } catch {
        /* storage unavailable */
      }
    });
  }

  t(key: TranslationKey): string {
    return STRINGS[this.lang()][key];
  }

  toggle(): void {
    this.lang.update(l => (l === 'en' ? 'ar' : 'en'));
  }

  set(lang: Lang): void {
    this.lang.set(lang);
  }

  private load(): Lang {
    try {
      return localStorage.getItem('ss.lang') === 'ar' ? 'ar' : 'en';
    } catch {
      return 'en';
    }
  }
}
