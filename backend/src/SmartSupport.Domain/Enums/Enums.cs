namespace SmartSupport.Domain.Enums;

public enum UserRole { Customer, Agent, Admin }

/// <summary>
/// Lifecycle of a support conversation:
/// Bot (AI handles it) → WaitingForAgent (escalated) → WithAgent (human claimed it) → Resolved.
/// </summary>
public enum ConversationStatus { Bot, WaitingForAgent, WithAgent, Resolved }

public enum MessageSender { Customer, Assistant, Agent, System }

public enum BillStatus { Unpaid, Paid, Overdue }

public enum TicketStatus { Open, InProgress, Resolved, Closed }

public enum TicketPriority { Low, Medium, High, Urgent }

public enum TicketCategory { Billing, Network, Plan, Roaming, Device, Account, Other }

public enum ArticleStatus { Pending, Indexed, Failed }

public enum Sentiment { Positive, Neutral, Negative }
