using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using SmartSupport.Application.Abstractions;
using SmartSupport.Application.Chat;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Application.Insights;

/// <summary>
/// AI features for the human side of support: handover summaries, suggested replies,
/// and sentiment/topic classification used by the analytics dashboard.
/// </summary>
public sealed class InsightsService(IChatClient chatClient, IAppDbContext db, IKnowledgeSearch knowledge)
{
    /// <summary>Short handover note so the agent doesn't have to read the whole chat.</summary>
    public async Task<string> SummarizeAsync(Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await LoadAsync(conversationId, ct);
        var transcript = BuildTranscript(conversation.Messages);

        var response = await chatClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, Prompts.HandoverSummary),
            new ChatMessage(ChatRole.User, $"Escalation reason: {conversation.EscalationReason ?? "n/a"}\n\nTranscript:\n{transcript}")
        ], new ChatOptions(), ct);

        conversation.Summary = response.Text.Trim();
        await db.SaveChangesAsync(ct);
        return conversation.Summary;
    }

    /// <summary>Drafts a reply for the agent, grounded in the knowledge base.</summary>
    public async Task<string> SuggestReplyAsync(Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await LoadAsync(conversationId, ct);
        var lastCustomerMessage = conversation.Messages
            .Where(m => m.Sender == MessageSender.Customer)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault()?.Content;

        if (lastCustomerMessage is null) return string.Empty;

        var hits = await knowledge.SearchAsync(lastCustomerMessage, top: 3, ct);
        var context = hits.Count == 0
            ? "(no relevant articles)"
            : string.Join("\n---\n", hits.Select(h => $"{h.ArticleTitle}: {h.Content}"));

        var response = await chatClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, Prompts.SuggestReply(conversation.Language)),
            new ChatMessage(ChatRole.User,
                $"Knowledge base extracts:\n{context}\n\nConversation:\n{BuildTranscript(conversation.Messages)}")
        ], new ChatOptions(), ct);

        return response.Text.Trim();
    }

    /// <summary>Sets sentiment and topic on a finished conversation.</summary>
    public async Task ClassifyAsync(Conversation conversation, CancellationToken ct = default)
    {
        var transcript = BuildTranscript(conversation.Messages);
        var response = await chatClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, Prompts.Classify),
            new ChatMessage(ChatRole.User, transcript)
        ], new ChatOptions(), ct);

        var (sentiment, topic) = InsightParser.Parse(response.Text);
        conversation.Sentiment = sentiment;
        conversation.Topic = topic;
    }

    private async Task<Conversation> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Conversations.Include(c => c.Messages).FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException("Conversation not found.");

    internal static string BuildTranscript(IEnumerable<Message> messages)
    {
        var sb = new StringBuilder();
        foreach (var m in messages.OrderBy(m => m.CreatedAt).TakeLast(40))
        {
            var who = m.Sender switch
            {
                MessageSender.Customer => "Customer",
                MessageSender.Assistant => "AI assistant",
                MessageSender.Agent => "Human agent",
                _ => "System"
            };
            sb.Append(who).Append(": ").AppendLine(m.Content);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Parses the classifier's JSON. LLMs sometimes wrap JSON in text or code fences,
/// so we extract the first {...} block and fall back to safe defaults.
/// </summary>
public static class InsightParser
{
    private static readonly string[] Topics = ["Billing", "Plans", "Roaming", "Network", "Device", "Account", "Payments", "Other"];

    public static (Sentiment Sentiment, string Topic) Parse(string? raw)
    {
        var sentiment = Sentiment.Neutral;
        var topic = "Other";
        if (string.IsNullOrWhiteSpace(raw)) return (sentiment, topic);

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return (sentiment, topic);

        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;

            if (root.TryGetProperty("sentiment", out var s) &&
                Enum.TryParse<Sentiment>(s.GetString(), ignoreCase: true, out var parsed))
                sentiment = parsed;

            if (root.TryGetProperty("topic", out var t))
            {
                var value = t.GetString();
                topic = Topics.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? "Other";
            }
        }
        catch (JsonException)
        {
            // keep defaults
        }

        return (sentiment, topic);
    }
}
