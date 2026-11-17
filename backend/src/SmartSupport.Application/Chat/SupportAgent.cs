using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SmartSupport.Application.Abstractions;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Application.Chat;

/// <summary>
/// Produces one AI reply for a conversation.
///
/// Flow:
/// 1. Build the prompt: system instructions + the last N messages.
/// 2. Give the model the tools (SupportTools). The FunctionInvokingChatClient
///    registered in Infrastructure runs the tool-calling loop automatically:
///    model asks for a tool → C# method runs → result goes back to the model.
/// 3. Stream the final answer token-by-token to the browser via SignalR.
/// 4. Save the reply with the tools used and the knowledge base sources.
/// </summary>
public sealed class SupportAgent(
    IChatClient chatClient,
    IAppDbContext db,
    IKnowledgeSearch knowledge,
    ISupportNotifier notifier,
    ILogger<SupportAgent> logger)
{
    private const int HistoryLimit = 20;

    public async Task<Message> ReplyAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        var customer = await db.Users.AsNoTracking()
            .FirstAsync(u => u.Id == conversation.CustomerUserId, cancellationToken);

        var account = customer.CustomerAccountId is null
            ? null
            : await db.CustomerAccounts.AsNoTracking()
                .Include(a => a.Plan)
                .FirstOrDefaultAsync(a => a.Id == customer.CustomerAccountId, cancellationToken);

        var prompt = BuildPrompt(conversation, customer, account);
        var tools = new SupportTools(db, knowledge, notifier, conversation, customer.CustomerAccountId);
        var options = new ChatOptions
        {
            Tools = tools.AsAITools(),
            // No temperature: newer models (e.g. Gemini 3.5+, OpenAI reasoning models) reject sampling parameters.
            ToolMode = ChatToolMode.Auto
        };

        var messageId = Guid.NewGuid();
        var reply = new StringBuilder();

        try
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(prompt, options, cancellationToken))
            {
                var delta = update.Text;
                if (string.IsNullOrEmpty(delta)) continue;

                reply.Append(delta);
                await notifier.AssistantDeltaAsync(conversation.Id, messageId, delta);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "AI reply failed for conversation {ConversationId}", conversation.Id);
            reply.Clear();
        }

        var text = reply.ToString().Trim();
        if (text.Length == 0) text = Prompts.Fallback(conversation.Language);

        var message = conversation.AddMessage(
            MessageSender.Assistant,
            text,
            toolsUsed: tools.UsedTools,
            sources: tools.Sources,
            messageId: messageId);

        await db.SaveChangesAsync(cancellationToken);
        return message;
    }

    internal static List<ChatMessage> BuildPrompt(Conversation conversation, User customer, CustomerAccount? account)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, Prompts.SupportAgent(customer, account, DateTime.UtcNow))
        };

        var history = conversation.Messages
            .OrderBy(m => m.CreatedAt)
            .TakeLast(HistoryLimit);

        foreach (var m in history)
        {
            switch (m.Sender)
            {
                case MessageSender.Customer:
                    messages.Add(new ChatMessage(ChatRole.User, m.Content));
                    break;
                case MessageSender.Assistant:
                    messages.Add(new ChatMessage(ChatRole.Assistant, m.Content));
                    break;
                case MessageSender.Agent:
                    messages.Add(new ChatMessage(ChatRole.Assistant, $"[Human agent] {m.Content}"));
                    break;
                // System notes ("transferred to an agent") are UI-only.
            }
        }

        return messages;
    }
}
