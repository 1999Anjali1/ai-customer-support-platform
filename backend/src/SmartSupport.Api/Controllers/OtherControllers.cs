using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSupport.Api.Auth;
using SmartSupport.Application.Accounts;
using SmartSupport.Application.Analytics;
using SmartSupport.Application.Dtos;
using SmartSupport.Application.Knowledge;
using SmartSupport.Application.Tickets;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize(Roles = "Customer")]
public sealed class AccountController(AccountService accounts) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<AccountDto>> Me(CancellationToken ct)
    {
        var account = await accounts.GetForUserAsync(User.GetUserId(), ct);
        if (account is null) return NotFound();
        return account;
    }
}

[ApiController]
[Route("api/plans")]
public sealed class PlansController(AccountService accounts) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<IReadOnlyList<PlanDto>> List(CancellationToken ct) => accounts.ListPlansAsync(ct);
}

public record UpdateTicketStatusRequest(TicketStatus Status);

[ApiController]
[Route("api/tickets")]
[Authorize]
public sealed class TicketsController(TicketService tickets) : ControllerBase
{
    [HttpGet("mine")]
    [Authorize(Roles = "Customer")]
    public Task<IReadOnlyList<TicketDto>> Mine(CancellationToken ct) => tickets.ListForCustomerAsync(User.GetUserId(), ct);

    [HttpGet]
    [Authorize(Roles = "Agent,Admin")]
    public Task<IReadOnlyList<TicketDto>> All([FromQuery] TicketStatus? status, CancellationToken ct) => tickets.ListAllAsync(status, ct);

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "Agent,Admin")]
    public async Task<IActionResult> UpdateStatus(Guid id, UpdateTicketStatusRequest request, CancellationToken ct)
    {
        await tickets.UpdateStatusAsync(id, request.Status, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/knowledge")]
[Authorize(Roles = "Agent,Admin")]
public sealed class KnowledgeController(KnowledgeService knowledge) : ControllerBase
{
    private static readonly string[] AllowedExtensions = [".md", ".txt"];

    [HttpGet]
    public Task<IReadOnlyList<KnowledgeArticleDto>> List(CancellationToken ct) => knowledge.ListAsync(ct);

    /// <summary>Test retrieval: what would the AI find for this question?</summary>
    [HttpGet("search")]
    public Task<IReadOnlyList<KnowledgeHit>> Search([FromQuery] string q, CancellationToken ct) => knowledge.SearchAsync(q, ct);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public Task<KnowledgeArticleDto> Create(CreateArticleRequest request, CancellationToken ct) => knowledge.CreateAsync(request, ct);

    /// <summary>Upload a Markdown or text file as a new article.</summary>
    [HttpPost("upload")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(1_000_000)]
    public async Task<ActionResult<KnowledgeArticleDto>> Upload(
        IFormFile file, [FromForm] string category, [FromForm] string? language, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return Problem(statusCode: 400, title: "Only .md and .txt files are supported.");

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync(ct);
        var title = Path.GetFileNameWithoutExtension(file.FileName).Replace('-', ' ').Replace('_', ' ');

        return await knowledge.CreateAsync(new CreateArticleRequest(title, category, language ?? "en", content), ct);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, CreateArticleRequest request, CancellationToken ct)
    {
        await knowledge.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reindex")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reindex(Guid id, CancellationToken ct)
    {
        await knowledge.ReindexAsync(id, ct);
        return Accepted();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await knowledge.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/analytics")]
[Authorize(Roles = "Agent,Admin")]
public sealed class AnalyticsController(AnalyticsService analytics) : ControllerBase
{
    [HttpGet("overview")]
    public Task<AnalyticsOverviewDto> Overview([FromQuery] int days = 30, CancellationToken ct = default) =>
        analytics.GetOverviewAsync(days, ct);
}
