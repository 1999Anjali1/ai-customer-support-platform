using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SmartSupport.Api.Auth;
using SmartSupport.Application;
using SmartSupport.Application.Abstractions;
using SmartSupport.Application.Conversations;
using SmartSupport.Application.Dtos;
using SmartSupport.Domain.Common;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Api.Hubs;

/// <summary>
/// Real-time channel for chat.
///  • Customers and agents join a "conversation:{id}" group to receive messages and AI tokens live.
///  • Agents/admins are also in the "agents" group to see the escalation queue update instantly.
/// </summary>
[Authorize]
public sealed class SupportHub(ConversationService conversations, ILogger<SupportHub> logger) : Hub
{
    public const string AgentsGroup = "agents";
    public static string ConversationGroup(Guid id) => $"conversation:{id}";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsStaff() == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, AgentsGroup);

        await base.OnConnectedAsync();
    }

    public async Task JoinConversation(Guid conversationId)
    {
        var user = Context.User!;
        if (!await conversations.CanAccessAsync(conversationId, user.GetUserId(), user.IsStaff()))
            throw new HubException("You don't have access to this conversation.");

        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
    }

    public Task LeaveConversation(Guid conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));

    [Authorize(Roles = "Customer")]
    public Task SendCustomerMessage(Guid conversationId, string text) =>
        RunAsync(() =>
            // CancellationToken.None: if the customer refreshes the page, the AI still finishes and saves its reply.
            conversations.HandleCustomerMessageAsync(conversationId, Context.User!.GetUserId(), Validate(text), CancellationToken.None));

    [Authorize(Roles = "Agent,Admin")]
    public Task SendAgentMessage(Guid conversationId, string text) =>
        RunAsync(() =>
            conversations.HandleAgentMessageAsync(conversationId, Context.User!.GetUserId(), Validate(text), CancellationToken.None));

    private static string Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new HubException("Message cannot be empty.");
        if (text.Length > 2000) throw new HubException("Message is too long (max 2000 characters).");
        return text;
    }

    /// <summary>Turns business errors into friendly HubExceptions the client can show.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException or ForbiddenException)
        {
            throw new HubException(ex.Message);
        }
        catch (Exception ex) when (ex is not HubException)
        {
            logger.LogError(ex, "Hub call failed");
            throw new HubException("Something went wrong. Please try again.");
        }
    }
}

/// <summary>Implements the application's notifier with SignalR (IHubContext can be used outside the hub).</summary>
public sealed class SignalRSupportNotifier(IHubContext<SupportHub> hub) : ISupportNotifier
{
    public Task AssistantDeltaAsync(Guid conversationId, Guid messageId, string delta) =>
        hub.Clients.Group(SupportHub.ConversationGroup(conversationId))
            .SendAsync("assistantDelta", new { conversationId, messageId, delta });

    public Task ToolActivityAsync(Guid conversationId, string toolName) =>
        hub.Clients.Group(SupportHub.ConversationGroup(conversationId))
            .SendAsync("toolActivity", new { conversationId, tool = toolName });

    public Task MessageAddedAsync(MessageDto message) =>
        hub.Clients.Group(SupportHub.ConversationGroup(message.ConversationId))
            .SendAsync("messageAdded", message);

    public Task ConversationUpdatedAsync(Guid conversationId, ConversationStatus status) =>
        hub.Clients.Groups(SupportHub.ConversationGroup(conversationId), SupportHub.AgentsGroup)
            .SendAsync("conversationUpdated", new { conversationId, status });
}
