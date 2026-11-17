using Microsoft.EntityFrameworkCore;
using SmartSupport.Application.Dtos;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Application.Abstractions;

/// <summary>
/// The database as seen by the application layer. Implemented by AppDbContext
/// in Infrastructure, so use cases don't depend on PostgreSQL directly.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Plan> Plans { get; }
    DbSet<CustomerAccount> CustomerAccounts { get; }
    DbSet<Bill> Bills { get; }
    DbSet<SupportTicket> SupportTickets { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<KnowledgeArticle> KnowledgeArticles { get; }
    DbSet<KnowledgeChunk> KnowledgeChunks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Pushes real-time events to browsers. Implemented with SignalR in the API project.
/// </summary>
public interface ISupportNotifier
{
    /// <summary>A piece of the AI reply as it is generated (token streaming).</summary>
    Task AssistantDeltaAsync(Guid conversationId, Guid messageId, string delta);

    /// <summary>The AI started using a tool, e.g. "get_recent_bills".</summary>
    Task ToolActivityAsync(Guid conversationId, string toolName);

    Task MessageAddedAsync(MessageDto message);

    /// <summary>Status changed (escalated, claimed, resolved) or new activity for the agent queue.</summary>
    Task ConversationUpdatedAsync(Guid conversationId, ConversationStatus status);
}

/// <summary>Hybrid (vector + keyword) search over the knowledge base.</summary>
public interface IKnowledgeSearch
{
    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int top, CancellationToken cancellationToken = default);
}

/// <summary>Turns text into embedding vectors. Disabled when no embedding model is configured.</summary>
public interface ITextEmbedder
{
    bool IsEnabled { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

/// <summary>Queue of articles waiting to be chunked and embedded by the background worker.</summary>
public interface IKnowledgeIndexQueue
{
    ValueTask EnqueueAsync(Guid articleId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}
