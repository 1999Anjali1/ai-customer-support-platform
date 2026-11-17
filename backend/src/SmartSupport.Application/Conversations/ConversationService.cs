using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SmartSupport.Application.Abstractions;
using SmartSupport.Application.Chat;
using SmartSupport.Application.Dtos;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Application.Conversations;

/// <summary>
/// Use cases for conversations, for both customers and human agents.
/// Called by the REST controllers and by the SignalR hub.
/// </summary>
public sealed class ConversationService(IAppDbContext db, SupportAgent agent, ISupportNotifier notifier)
{
    // ============================================================ customer side

    /// <summary>Returns the customer's open conversation, or starts a new one with a welcome message.</summary>
    public async Task<ConversationDetailDto> StartOrResumeAsync(Guid customerUserId, string? language, CancellationToken ct = default)
    {
        var open = await db.Conversations
            .Where(c => c.CustomerUserId == customerUserId && c.Status != ConversationStatus.Resolved)
            .OrderByDescending(c => c.LastMessageAt)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (open is { } existingId)
            return await GetDetailAsync(existingId, customerUserId, isStaff: false, ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == customerUserId, ct)
                   ?? throw new NotFoundException("User not found.");

        var conversation = Conversation.Start(user.Id, language ?? user.PreferredLanguage);
        conversation.AddMessage(MessageSender.Assistant, Prompts.Welcome(user.FullName, conversation.Language));

        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);

        return await GetDetailAsync(conversation.Id, customerUserId, isStaff: false, ct);
    }

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListForCustomerAsync(Guid customerUserId, CancellationToken ct = default) =>
        await db.Conversations.AsNoTracking()
            .Where(c => c.CustomerUserId == customerUserId)
            .OrderByDescending(c => c.LastMessageAt)
            .Take(30)
            .Select(SummaryProjection)
            .ToListAsync(ct);

    /// <summary>
    /// Saves the customer's message, then either lets the AI answer (status Bot)
    /// or leaves it for the human agent (escalated).
    /// </summary>
    public async Task HandleCustomerMessageAsync(Guid conversationId, Guid customerUserId, string text, CancellationToken ct = default)
    {
        var conversation = await LoadTrackedAsync(conversationId, ct);
        if (conversation.CustomerUserId != customerUserId)
            throw new ForbiddenException("This is not your conversation.");

        conversation.Language = LanguageDetector.Detect(text, conversation.Language);
        var message = conversation.AddMessage(MessageSender.Customer, Truncate(text, 2000), customerUserId);
        await db.SaveChangesAsync(ct);
        await notifier.MessageAddedAsync(message.ToDto());

        if (conversation.Status == ConversationStatus.Bot)
        {
            var reply = await agent.ReplyAsync(conversation, ct);
            await notifier.MessageAddedAsync(reply.ToDto());
        }
        else
        {
            // Let agents know there is new activity in the queue.
            await notifier.ConversationUpdatedAsync(conversation.Id, conversation.Status);
        }
    }

    /// <summary>The customer pressed "Talk to a human".</summary>
    public async Task RequestHumanAsync(Guid conversationId, Guid customerUserId, CancellationToken ct = default)
    {
        var conversation = await LoadTrackedAsync(conversationId, ct);
        if (conversation.CustomerUserId != customerUserId)
            throw new ForbiddenException("This is not your conversation.");

        if (conversation.Status != ConversationStatus.Bot) return;

        conversation.Escalate("Customer asked for a human agent");
        var note = conversation.AddMessage(MessageSender.System,
            conversation.Language == "ar"
                ? "تم تحويل المحادثة إلى أحد موظفي خدمة العملاء. سينضم إليك موظف قريباً."
                : "You're being connected to a human support agent. Someone will join shortly.");
        await db.SaveChangesAsync(ct);

        await notifier.MessageAddedAsync(note.ToDto());
        await notifier.ConversationUpdatedAsync(conversation.Id, conversation.Status);
    }

    public async Task CloseAsync(Guid conversationId, Guid userId, bool isStaff, CancellationToken ct = default)
    {
        var conversation = await LoadTrackedAsync(conversationId, ct);
        if (!isStaff && conversation.CustomerUserId != userId)
            throw new ForbiddenException("This is not your conversation.");

        if (conversation.Status == ConversationStatus.Resolved) return;

        var note = conversation.AddMessage(MessageSender.System,
            conversation.Language == "ar" ? "تم إغلاق المحادثة. شكراً لتواصلك معنا." : "This conversation has been closed. Thank you for contacting us.");
        conversation.Resolve();
        await db.SaveChangesAsync(ct);

        await notifier.MessageAddedAsync(note.ToDto());
        await notifier.ConversationUpdatedAsync(conversation.Id, conversation.Status);
    }

    // ============================================================ agent side

    /// <summary>Escalated conversations waiting for someone, plus the ones this agent is handling.</summary>
    public async Task<IReadOnlyList<ConversationSummaryDto>> GetQueueAsync(Guid agentId, CancellationToken ct = default) =>
        await db.Conversations.AsNoTracking()
            .Where(c => c.Status == ConversationStatus.WaitingForAgent
                        || (c.Status == ConversationStatus.WithAgent && c.AssignedAgentId == agentId))
            .OrderBy(c => c.EscalatedAt)
            .Select(SummaryProjection)
            .ToListAsync(ct);

    /// <summary>All recent conversations (admin / supervisor view).</summary>
    public async Task<IReadOnlyList<ConversationSummaryDto>> ListRecentAsync(ConversationStatus? status, CancellationToken ct = default)
    {
        var query = db.Conversations.AsNoTracking();
        if (status is not null) query = query.Where(c => c.Status == status);

        return await query
            .OrderByDescending(c => c.LastMessageAt)
            .Take(100)
            .Select(SummaryProjection)
            .ToListAsync(ct);
    }

    public async Task ClaimAsync(Guid conversationId, Guid agentId, CancellationToken ct = default)
    {
        var conversation = await LoadTrackedAsync(conversationId, ct);
        if (conversation.Status == ConversationStatus.WithAgent && conversation.AssignedAgentId == agentId) return;

        await AssignAndAnnounceAsync(conversation, agentId, ct);
    }

    public async Task HandleAgentMessageAsync(Guid conversationId, Guid agentId, string text, CancellationToken ct = default)
    {
        var conversation = await LoadTrackedAsync(conversationId, ct);

        if (conversation.AssignedAgentId != agentId || conversation.Status != ConversationStatus.WithAgent)
            await AssignAndAnnounceAsync(conversation, agentId, ct);

        var agentName = await db.Users.Where(u => u.Id == agentId).Select(u => u.FullName).FirstAsync(ct);
        var message = conversation.AddMessage(MessageSender.Agent, Truncate(text, 2000), agentId);
        await db.SaveChangesAsync(ct);

        await notifier.MessageAddedAsync(message.ToDto(agentName));
    }

    // ============================================================ shared

    public async Task<ConversationDetailDto> GetDetailAsync(Guid conversationId, Guid userId, bool isStaff, CancellationToken ct = default)
    {
        var summary = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(SummaryProjection)
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Conversation not found.");

        var ownerId = await db.Conversations.Where(c => c.Id == conversationId).Select(c => c.CustomerUserId).FirstAsync(ct);
        if (!isStaff && ownerId != userId)
            throw new ForbiddenException("This is not your conversation.");

        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var senderIds = messages.Where(m => m.SenderUserId != null).Select(m => m.SenderUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => senderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var summaryText = await db.Conversations.Where(c => c.Id == conversationId).Select(c => c.Summary).FirstAsync(ct);

        return new ConversationDetailDto(
            summary,
            isStaff ? summaryText : null,
            messages.Select(m => m.ToDto(m.SenderUserId is { } id && names.TryGetValue(id, out var n) ? n : null)).ToList());
    }

    /// <summary>Used by the SignalR hub to check a user may join a conversation's live updates.</summary>
    public async Task<bool> CanAccessAsync(Guid conversationId, Guid userId, bool isStaff, CancellationToken ct = default) =>
        isStaff
            ? await db.Conversations.AnyAsync(c => c.Id == conversationId, ct)
            : await db.Conversations.AnyAsync(c => c.Id == conversationId && c.CustomerUserId == userId, ct);

    private async Task AssignAndAnnounceAsync(Conversation conversation, Guid agentId, CancellationToken ct)
    {
        conversation.AssignTo(agentId);
        var agentName = await db.Users.Where(u => u.Id == agentId).Select(u => u.FullName).FirstAsync(ct);
        var note = conversation.AddMessage(MessageSender.System,
            conversation.Language == "ar" ? $"انضم {agentName} إلى المحادثة." : $"{agentName} from the support team joined the chat.");
        await db.SaveChangesAsync(ct);

        await notifier.MessageAddedAsync(note.ToDto());
        await notifier.ConversationUpdatedAsync(conversation.Id, conversation.Status);
    }

    private async Task<Conversation> LoadTrackedAsync(Guid id, CancellationToken ct) =>
        await db.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException("Conversation not found.");

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    /// <summary>EF Core turns this expression into a single SQL query (no N+1).</summary>
    internal static readonly Expression<Func<Conversation, ConversationSummaryDto>> SummaryProjection = c =>
        new ConversationSummaryDto(
            c.Id,
            c.Status,
            c.Language,
            c.Customer!.FullName,
            c.Customer.CustomerAccount != null ? c.Customer.CustomerAccount.MobileNumber : null,
            c.AssignedAgent != null ? c.AssignedAgent.FullName : null,
            c.EscalationReason,
            c.Messages
                .Where(m => m.Sender != MessageSender.System)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => m.Content)
                .FirstOrDefault(),
            c.CreatedAt,
            c.LastMessageAt,
            c.EscalatedAt,
            c.Sentiment,
            c.Topic);
}
