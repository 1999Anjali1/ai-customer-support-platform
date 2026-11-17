using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSupport.Api.Auth;
using SmartSupport.Application.Conversations;
using SmartSupport.Application.Dtos;
using SmartSupport.Application.Insights;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Api.Controllers;

public record TextResponse(string Text);

/// <summary>Human agent console: queue, claiming chats and AI assistance for agents.</summary>
[ApiController]
[Route("api/agent")]
[Authorize(Roles = "Agent,Admin")]
public sealed class AgentController(ConversationService conversations, InsightsService insights) : ControllerBase
{
    [HttpGet("queue")]
    public Task<IReadOnlyList<ConversationSummaryDto>> Queue(CancellationToken ct) =>
        conversations.GetQueueAsync(User.GetUserId(), ct);

    [HttpGet("conversations")]
    public Task<IReadOnlyList<ConversationSummaryDto>> Recent([FromQuery] ConversationStatus? status, CancellationToken ct) =>
        conversations.ListRecentAsync(status, ct);

    [HttpPost("conversations/{id:guid}/claim")]
    public async Task<IActionResult> Claim(Guid id, CancellationToken ct)
    {
        await conversations.ClaimAsync(id, User.GetUserId(), ct);
        return NoContent();
    }

    [HttpPost("conversations/{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct)
    {
        await conversations.CloseAsync(id, User.GetUserId(), isStaff: true, ct);
        return NoContent();
    }

    /// <summary>AI handover note for the agent.</summary>
    [HttpPost("conversations/{id:guid}/summary")]
    public async Task<TextResponse> Summary(Guid id, CancellationToken ct) =>
        new(await insights.SummarizeAsync(id, ct));

    /// <summary>AI-drafted reply the agent can edit and send.</summary>
    [HttpPost("conversations/{id:guid}/suggest-reply")]
    public async Task<TextResponse> SuggestReply(Guid id, CancellationToken ct) =>
        new(await insights.SuggestReplyAsync(id, ct));
}
