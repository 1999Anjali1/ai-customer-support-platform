using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSupport.Application.Abstractions;
using SmartSupport.Application.Insights;
using SmartSupport.Domain.Enums;
using SmartSupport.Infrastructure.Ai;
using SmartSupport.Infrastructure.Persistence;

namespace SmartSupport.Infrastructure.Workers;

/// <summary>
/// Runs every minute:
///  1. Closes AI conversations that have been idle for 30 minutes.
///  2. Classifies finished conversations (sentiment + topic) with the LLM for the analytics dashboard.
/// </summary>
public sealed class ConversationInsightsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AiOptions> aiOptions,
    ILogger<ConversationInsightsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await CloseIdleConversationsAsync(stoppingToken);
                if (aiOptions.Value.Chat.IsConfigured)
                    await ClassifyFinishedConversationsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Conversation insights run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CloseIdleConversationsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<ISupportNotifier>();

        var cutoff = DateTime.UtcNow - IdleTimeout;
        var idle = await db.Conversations
            .Include(c => c.Messages)
            .Where(c => c.Status == ConversationStatus.Bot
                        && c.LastMessageAt < cutoff
                        && c.Messages.Any(m => m.Sender == MessageSender.Customer))
            .OrderBy(c => c.LastMessageAt)
            .Take(50)
            .ToListAsync(ct);

        if (idle.Count == 0) return;

        foreach (var conversation in idle)
        {
            conversation.AddMessage(MessageSender.System,
                conversation.Language == "ar" ? "تم إغلاق المحادثة بسبب عدم النشاط." : "Conversation closed after 30 minutes of inactivity.");
            conversation.Resolve();
        }

        await db.SaveChangesAsync(ct);
        foreach (var conversation in idle)
            await notifier.ConversationUpdatedAsync(conversation.Id, conversation.Status);

        logger.LogInformation("Closed {Count} idle conversations", idle.Count);
    }

    private async Task ClassifyFinishedConversationsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var insights = scope.ServiceProvider.GetRequiredService<InsightsService>();

        var conversations = await db.Conversations
            .Include(c => c.Messages)
            .Where(c => c.Status == ConversationStatus.Resolved
                        && c.Sentiment == null
                        && c.Messages.Any(m => m.Sender == MessageSender.Customer))
            .OrderByDescending(c => c.ResolvedAt)
            .Take(5)
            .ToListAsync(ct);

        foreach (var conversation in conversations)
        {
            try
            {
                await insights.ClassifyAsync(conversation, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Classification failed for conversation {Id}", conversation.Id);
            }
        }

        if (conversations.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
