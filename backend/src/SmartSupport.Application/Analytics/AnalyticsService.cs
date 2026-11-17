using Microsoft.EntityFrameworkCore;
using SmartSupport.Application.Abstractions;
using SmartSupport.Application.Dtos;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Application.Analytics;

/// <summary>KPIs for the supervisor dashboard: how much the AI resolves on its own, sentiment, topics.</summary>
public sealed class AnalyticsService(IAppDbContext db)
{
    public async Task<AnalyticsOverviewDto> GetOverviewAsync(int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

        var conversations = await db.Conversations.AsNoTracking()
            .Where(c => c.CreatedAt >= since)
            .Select(c => new
            {
                c.CreatedAt,
                c.Status,
                c.EscalatedAt,
                c.ResolvedByAi,
                c.Sentiment,
                c.Topic
            })
            .ToListAsync(ct);

        var toolLists = await db.Messages.AsNoTracking()
            .Where(m => m.CreatedAt >= since && m.ToolsUsed != null)
            .Select(m => m.ToolsUsed!)
            .ToListAsync(ct);

        var waiting = await db.Conversations.CountAsync(c => c.Status == ConversationStatus.WaitingForAgent, ct);
        var openTickets = await db.SupportTickets.CountAsync(
            t => t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress, ct);
        var aiTickets = await db.SupportTickets.CountAsync(t => t.CreatedByAi && t.CreatedAt >= since, ct);

        var resolved = conversations.Where(c => c.Status == ConversationStatus.Resolved).ToList();
        var resolvedByAi = resolved.Count(c => c.ResolvedByAi == true);
        var escalated = conversations.Count(c => c.EscalatedAt != null);

        var minutesToEscalation = conversations
            .Where(c => c.EscalatedAt != null)
            .Select(c => (c.EscalatedAt!.Value - c.CreatedAt).TotalMinutes)
            .ToList();

        var daily = Enumerable.Range(0, days)
            .Select(i => DateOnly.FromDateTime(since.AddDays(i)))
            .Select(d => new DailyCount(
                d,
                conversations.Count(c => DateOnly.FromDateTime(c.CreatedAt) == d),
                conversations.Count(c => c.EscalatedAt is { } e && DateOnly.FromDateTime(e) == d)))
            .ToList();

        return new AnalyticsOverviewDto(
            TotalConversations: conversations.Count,
            ResolvedByAi: resolvedByAi,
            Escalated: escalated,
            AiResolutionRate: resolved.Count == 0 ? 0 : Math.Round(100.0 * resolvedByAi / resolved.Count, 1),
            WaitingInQueue: waiting,
            OpenTickets: openTickets,
            TicketsCreatedByAi: aiTickets,
            AverageMinutesToEscalation: minutesToEscalation.Count == 0 ? null : Math.Round(minutesToEscalation.Average(), 1),
            Sentiments: Enum.GetValues<Sentiment>()
                .Select(s => new CountItem(s.ToString(), conversations.Count(c => c.Sentiment == s)))
                .ToList(),
            Topics: conversations
                .Where(c => c.Topic != null)
                .GroupBy(c => c.Topic!)
                .Select(g => new CountItem(g.Key, g.Count()))
                .OrderByDescending(x => x.Count)
                .ToList(),
            ToolUsage: toolLists
                .SelectMany(t => t.Split('|', StringSplitOptions.RemoveEmptyEntries))
                .GroupBy(t => t)
                .Select(g => new CountItem(g.Key, g.Count()))
                .OrderByDescending(x => x.Count)
                .ToList(),
            Daily: daily);
    }
}
