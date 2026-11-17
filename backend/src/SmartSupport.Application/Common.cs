using SmartSupport.Application.Dtos;
using SmartSupport.Domain.Entities;

namespace SmartSupport.Application;

/// <summary>Mapped to HTTP 404 by the API.</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>Mapped to HTTP 403 by the API.</summary>
public class ForbiddenException(string message) : Exception(message);

public static class Mapping
{
    public static MessageDto ToDto(this Message m, string? senderName = null) => new(
        m.Id,
        m.ConversationId,
        m.Sender,
        senderName,
        m.Content,
        Split(m.ToolsUsed),
        Split(m.Sources),
        m.CreatedAt);

    public static PlanDto ToDto(this Plan p) => new(
        p.Id, p.Code, p.NameEn, p.NameAr, p.MonthlyPriceQar, p.DataGb, p.LocalMinutes, p.InternationalMinutes, p.Is5G);

    public static BillDto ToDto(this Bill b) => new(b.Id, b.Period, b.AmountQar, b.DueDate, b.Status, b.PaidAt);

    private static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrEmpty(value) ? [] : value.Split('|', StringSplitOptions.RemoveEmptyEntries);
}
