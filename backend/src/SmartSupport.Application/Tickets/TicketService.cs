using Microsoft.EntityFrameworkCore;
using SmartSupport.Application.Abstractions;
using SmartSupport.Application.Dtos;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Application.Tickets;

public sealed class TicketService(IAppDbContext db)
{
    public async Task<IReadOnlyList<TicketDto>> ListForCustomerAsync(Guid customerUserId, CancellationToken ct = default)
    {
        var accountId = await db.Users.Where(u => u.Id == customerUserId).Select(u => u.CustomerAccountId).FirstOrDefaultAsync(ct);
        if (accountId is null) return [];

        return await db.SupportTickets.AsNoTracking()
            .Where(t => t.CustomerAccountId == accountId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(Projection)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TicketDto>> ListAllAsync(TicketStatus? status, CancellationToken ct = default)
    {
        var query = db.SupportTickets.AsNoTracking();
        if (status is not null) query = query.Where(t => t.Status == status);

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(200)
            .Select(Projection)
            .ToListAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid id, TicketStatus status, CancellationToken ct = default)
    {
        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct)
                     ?? throw new NotFoundException("Ticket not found.");
        ticket.ChangeStatus(status);
        await db.SaveChangesAsync(ct);
    }

    private static readonly System.Linq.Expressions.Expression<Func<SupportTicket, TicketDto>> Projection = t =>
        new TicketDto(
            t.Id, t.Number, t.CustomerAccount!.FullName, t.Category, t.Priority, t.Status,
            t.Subject, t.Description, t.CreatedByAi, t.ConversationId, t.CreatedAt, t.UpdatedAt);
}
