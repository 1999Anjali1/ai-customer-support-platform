using SmartSupport.Domain.Enums;

namespace SmartSupport.Domain.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public MessageSender Sender { get; set; }

    /// <summary>The customer or agent who wrote it (null for AI/system messages).</summary>
    public Guid? SenderUserId { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Tools the AI called while writing this reply, separated by '|'.</summary>
    public string? ToolsUsed { get; set; }

    /// <summary>Knowledge base article titles the AI used, separated by '|'.</summary>
    public string? Sources { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
