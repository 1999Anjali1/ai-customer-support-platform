using SmartSupport.Domain.Common;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Domain.Entities;

/// <summary>
/// A chat session between a customer and support (AI first, human if escalated).
/// All status changes go through methods so the state machine is always valid.
/// </summary>
public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CustomerUserId { get; set; }
    public User? Customer { get; set; }

    public ConversationStatus Status { get; private set; } = ConversationStatus.Bot;

    public Guid? AssignedAgentId { get; private set; }
    public User? AssignedAgent { get; set; }

    /// <summary>"en" or "ar" – detected from the customer's messages.</summary>
    public string Language { get; set; } = "en";

    public string? EscalationReason { get; private set; }

    // AI-generated insights
    public string? Summary { get; set; }
    public Sentiment? Sentiment { get; set; }
    public string? Topic { get; set; }

    /// <summary>True when the conversation was closed without any human agent involved.</summary>
    public bool? ResolvedByAi { get; private set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
    public DateTime? EscalatedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    public List<Message> Messages { get; set; } = [];

    public static Conversation Start(Guid customerUserId, string language) => new()
    {
        CustomerUserId = customerUserId,
        Language = language == "ar" ? "ar" : "en"
    };

    public Message AddMessage(
        MessageSender sender,
        string content,
        Guid? senderUserId = null,
        IEnumerable<string>? toolsUsed = null,
        IEnumerable<string>? sources = null,
        Guid? messageId = null)
    {
        if (Status == ConversationStatus.Resolved)
            throw new DomainException("This conversation is closed. Please start a new chat.");

        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException("A message cannot be empty.");

        var message = new Message
        {
            Id = messageId ?? Guid.NewGuid(),
            ConversationId = Id,
            Sender = sender,
            SenderUserId = senderUserId,
            Content = content.Trim(),
            ToolsUsed = JoinOrNull(toolsUsed),
            Sources = JoinOrNull(sources),
            CreatedAt = DateTime.UtcNow
        };

        Messages.Add(message);
        LastMessageAt = message.CreatedAt;
        return message;
    }

    /// <summary>The AI (or the customer) asks for a human agent.</summary>
    public void Escalate(string reason)
    {
        if (Status != ConversationStatus.Bot)
            throw new DomainException("This conversation is already with the human support team.");

        Status = ConversationStatus.WaitingForAgent;
        EscalationReason = string.IsNullOrWhiteSpace(reason) ? "Customer requested a human agent" : reason.Trim();
        EscalatedAt = DateTime.UtcNow;
    }

    /// <summary>A human agent picks the conversation from the queue.</summary>
    public void AssignTo(Guid agentId)
    {
        switch (Status)
        {
            case ConversationStatus.Resolved:
                throw new DomainException("This conversation is already resolved.");
            case ConversationStatus.Bot:
                throw new DomainException("This conversation has not been escalated to a human agent.");
            case ConversationStatus.WithAgent when AssignedAgentId != agentId:
                throw new DomainException("Another agent is already handling this conversation.");
        }

        Status = ConversationStatus.WithAgent;
        AssignedAgentId = agentId;
    }

    public void Resolve()
    {
        if (Status == ConversationStatus.Resolved)
            return;

        ResolvedByAi = EscalatedAt is null;
        Status = ConversationStatus.Resolved;
        ResolvedAt = DateTime.UtcNow;
    }

    private static string? JoinOrNull(IEnumerable<string>? values)
    {
        if (values is null) return null;
        var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        return list.Count == 0 ? null : string.Join('|', list);
    }
}
