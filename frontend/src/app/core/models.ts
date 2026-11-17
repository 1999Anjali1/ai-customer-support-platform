// Types mirror the .NET DTOs (enums are serialised as strings by the API).

export type Role = 'Customer' | 'Agent' | 'Admin';
export type ConversationStatus = 'Bot' | 'WaitingForAgent' | 'WithAgent' | 'Resolved';
export type MessageSender = 'Customer' | 'Assistant' | 'Agent' | 'System';
export type Sentiment = 'Positive' | 'Neutral' | 'Negative';
export type TicketStatus = 'Open' | 'InProgress' | 'Resolved' | 'Closed';
export type ArticleStatus = 'Pending' | 'Indexed' | 'Failed';

export interface User {
  id: string;
  email: string;
  fullName: string;
  role: Role;
  preferredLanguage: 'en' | 'ar';
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
  user: User;
}

export interface Message {
  id: string;
  conversationId: string;
  sender: MessageSender;
  senderName: string | null;
  content: string;
  toolsUsed: string[];
  sources: string[];
  createdAt: string;
  /** client-only: true while the AI reply is still streaming */
  streaming?: boolean;
}

export interface ConversationSummary {
  id: string;
  status: ConversationStatus;
  language: 'en' | 'ar';
  customerName: string;
  customerMobile: string | null;
  assignedAgentName: string | null;
  escalationReason: string | null;
  lastMessage: string | null;
  createdAt: string;
  lastMessageAt: string;
  escalatedAt: string | null;
  sentiment: Sentiment | null;
  topic: string | null;
}

export interface ConversationDetail {
  conversation: ConversationSummary;
  summary: string | null;
  messages: Message[];
}

export interface Plan {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  monthlyPriceQar: number;
  dataGb: number | null;
  localMinutes: number | null;
  internationalMinutes: number;
  is5G: boolean;
}

export interface Bill {
  id: string;
  period: string;
  amountQar: number;
  dueDate: string;
  status: 'Unpaid' | 'Paid' | 'Overdue';
  paidAt: string | null;
}

export interface Account {
  accountNumber: string;
  mobileNumber: string;
  fullName: string;
  plan: Plan;
  dataUsedGb: number;
  isRoamingEnabled: boolean;
  outstandingQar: number;
  bills: Bill[];
}

export interface Ticket {
  id: string;
  number: string;
  customerName: string;
  category: string;
  priority: 'Low' | 'Medium' | 'High' | 'Urgent';
  status: TicketStatus;
  subject: string;
  description: string;
  createdByAi: boolean;
  conversationId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface KnowledgeArticle {
  id: string;
  title: string;
  category: string;
  language: 'en' | 'ar';
  content: string;
  status: ArticleStatus;
  indexingError: string | null;
  chunkCount: number;
  createdAt: string;
  indexedAt: string | null;
}

export interface KnowledgeHit {
  articleId: string;
  articleTitle: string;
  content: string;
  score: number;
  matchedBy: string;
}

export interface CountItem {
  label: string;
  count: number;
}

export interface AnalyticsOverview {
  totalConversations: number;
  resolvedByAi: number;
  escalated: number;
  aiResolutionRate: number;
  waitingInQueue: number;
  openTickets: number;
  ticketsCreatedByAi: number;
  averageMinutesToEscalation: number | null;
  sentiments: CountItem[];
  topics: CountItem[];
  toolUsage: CountItem[];
  daily: { date: string; conversations: number; escalations: number }[];
}

/** Friendly labels for the AI tools, shown as chips under AI messages. */
export const TOOL_LABELS: Record<string, { en: string; ar: string; icon: string }> = {
  get_account_summary: { en: 'Checked your account', ar: 'تم فحص حسابك', icon: '👤' },
  get_recent_bills: { en: 'Looked up your bills', ar: 'تم الاطلاع على فواتيرك', icon: '🧾' },
  list_available_plans: { en: 'Compared plans', ar: 'تمت مقارنة الباقات', icon: '📶' },
  change_plan: { en: 'Plan change', ar: 'تغيير الباقة', icon: '🔄' },
  set_roaming: { en: 'Roaming settings', ar: 'إعدادات التجوال', icon: '✈️' },
  create_support_ticket: { en: 'Opened a ticket', ar: 'تم فتح تذكرة', icon: '🎫' },
  get_my_tickets: { en: 'Checked your tickets', ar: 'تم فحص التذاكر', icon: '📋' },
  search_knowledge_base: { en: 'Searched help centre', ar: 'تم البحث في مركز المساعدة', icon: '📚' },
  escalate_to_human_agent: { en: 'Transferred to an agent', ar: 'تم التحويل لموظف', icon: '🙋' }
};
