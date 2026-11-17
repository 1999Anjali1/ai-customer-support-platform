using SmartSupport.Domain.Enums;

namespace SmartSupport.Application.Dtos;

// ---------- Conversations ----------

public record MessageDto(
    Guid Id,
    Guid ConversationId,
    MessageSender Sender,
    string? SenderName,
    string Content,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<string> Sources,
    DateTime CreatedAt);

public record ConversationSummaryDto(
    Guid Id,
    ConversationStatus Status,
    string Language,
    string CustomerName,
    string? CustomerMobile,
    string? AssignedAgentName,
    string? EscalationReason,
    string? LastMessage,
    DateTime CreatedAt,
    DateTime LastMessageAt,
    DateTime? EscalatedAt,
    Sentiment? Sentiment,
    string? Topic);

public record ConversationDetailDto(
    ConversationSummaryDto Conversation,
    string? Summary,
    IReadOnlyList<MessageDto> Messages);

// ---------- Customer account ----------

public record PlanDto(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    decimal MonthlyPriceQar,
    int? DataGb,
    int? LocalMinutes,
    int InternationalMinutes,
    bool Is5G);

public record BillDto(Guid Id, DateOnly Period, decimal AmountQar, DateOnly DueDate, BillStatus Status, DateTime? PaidAt);

public record AccountDto(
    string AccountNumber,
    string MobileNumber,
    string FullName,
    PlanDto Plan,
    decimal DataUsedGb,
    bool IsRoamingEnabled,
    decimal OutstandingQar,
    IReadOnlyList<BillDto> Bills);

// ---------- Tickets ----------

public record TicketDto(
    Guid Id,
    string Number,
    string CustomerName,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    string Subject,
    string Description,
    bool CreatedByAi,
    Guid? ConversationId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// ---------- Knowledge base ----------

public record KnowledgeArticleDto(
    Guid Id,
    string Title,
    string Category,
    string Language,
    string Content,
    ArticleStatus Status,
    string? IndexingError,
    int ChunkCount,
    DateTime CreatedAt,
    DateTime? IndexedAt);

public record CreateArticleRequest(string Title, string Category, string Language, string Content);

public record KnowledgeHit(Guid ArticleId, string ArticleTitle, string Content, double Score, string MatchedBy);

// ---------- Analytics ----------

public record CountItem(string Label, int Count);

public record DailyCount(DateOnly Date, int Conversations, int Escalations);

public record AnalyticsOverviewDto(
    int TotalConversations,
    int ResolvedByAi,
    int Escalated,
    double AiResolutionRate,
    int WaitingInQueue,
    int OpenTickets,
    int TicketsCreatedByAi,
    double? AverageMinutesToEscalation,
    IReadOnlyList<CountItem> Sentiments,
    IReadOnlyList<CountItem> Topics,
    IReadOnlyList<CountItem> ToolUsage,
    IReadOnlyList<DailyCount> Daily);
