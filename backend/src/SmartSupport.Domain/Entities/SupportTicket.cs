using SmartSupport.Domain.Common;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Domain.Entities;

public class SupportTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Number { get; set; }
    public Guid CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }
    public Guid? ConversationId { get; set; }

    public TicketCategory Category { get; set; }
    public TicketPriority Priority { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public required string Subject { get; set; }
    public required string Description { get; set; }

    /// <summary>True when the AI agent opened the ticket on the customer's behalf.</summary>
    public bool CreatedByAi { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static SupportTicket Open(
        Guid customerAccountId,
        TicketCategory category,
        TicketPriority priority,
        string subject,
        string description,
        Guid? conversationId = null,
        bool createdByAi = false)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new DomainException("A ticket needs a subject.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("A ticket needs a description.");

        var now = DateTime.UtcNow;
        return new SupportTicket
        {
            Number = $"TCK-{now:yyMMdd}-{Random.Shared.Next(1000, 9999)}",
            CustomerAccountId = customerAccountId,
            Category = category,
            Priority = priority,
            Subject = subject.Trim(),
            Description = description.Trim(),
            ConversationId = conversationId,
            CreatedByAi = createdByAi,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void ChangeStatus(TicketStatus status)
    {
        if (Status == TicketStatus.Closed)
            throw new DomainException("A closed ticket cannot be changed.");

        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }
}
