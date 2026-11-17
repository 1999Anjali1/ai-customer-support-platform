using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSupport.Api.Auth;
using SmartSupport.Application.Conversations;
using SmartSupport.Application.Dtos;

namespace SmartSupport.Api.Controllers;

public record StartConversationRequest(string? Language);

/// <summary>
/// Customer chat endpoints. Sending messages happens over SignalR (SupportHub)
/// so the AI reply can be streamed; these endpoints load and manage conversations.
/// </summary>
[ApiController]
[Route("api/conversations")]
[Authorize]
public sealed class ConversationsController(ConversationService conversations) : ControllerBase
{
    /// <summary>Resume the open conversation or start a new one.</summary>
    [HttpPost("start")]
    [Authorize(Roles = "Customer")]
    public Task<ConversationDetailDto> Start(StartConversationRequest request, CancellationToken ct) =>
        conversations.StartOrResumeAsync(User.GetUserId(), request.Language, ct);

    [HttpGet("mine")]
    [Authorize(Roles = "Customer")]
    public Task<IReadOnlyList<ConversationSummaryDto>> Mine(CancellationToken ct) =>
        conversations.ListForCustomerAsync(User.GetUserId(), ct);

    [HttpGet("{id:guid}")]
    public Task<ConversationDetailDto> Get(Guid id, CancellationToken ct) =>
        conversations.GetDetailAsync(id, User.GetUserId(), User.IsStaff(), ct);

    /// <summary>"Talk to a human" button.</summary>
    [HttpPost("{id:guid}/human")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> RequestHuman(Guid id, CancellationToken ct)
    {
        await conversations.RequestHumanAsync(id, User.GetUserId(), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        await conversations.CloseAsync(id, User.GetUserId(), User.IsStaff(), ct);
        return NoContent();
    }
}
