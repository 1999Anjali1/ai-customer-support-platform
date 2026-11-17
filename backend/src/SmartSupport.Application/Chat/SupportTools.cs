using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using SmartSupport.Application.Abstractions;
using SmartSupport.Domain.Common;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Application.Chat;

/// <summary>
/// The "tools" (functions) the language model is allowed to call.
///
/// One instance is created per AI reply, bound to the current conversation and the
/// customer's account. This is the security boundary: the model can only ever read or
/// change data of the customer it is talking to, and every business rule is checked in
/// C# (domain methods), not by the model.
/// </summary>
public sealed class SupportTools(
    IAppDbContext db,
    IKnowledgeSearch knowledge,
    ISupportNotifier notifier,
    Conversation conversation,
    Guid? customerAccountId)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string NoAccount = "This customer has no telecom account linked, so account data is not available.";

    private readonly List<string> _usedTools = [];
    private readonly List<string> _sources = [];

    /// <summary>Names of the tools called during this reply (shown as chips in the UI and used for analytics).</summary>
    public IReadOnlyList<string> UsedTools => _usedTools;

    /// <summary>Knowledge base articles the answer was based on (shown as citations).</summary>
    public IReadOnlyList<string> Sources => _sources;

    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(GetAccountSummaryAsync, name: "get_account_summary",
            description: "Get the customer's current plan, monthly price, data allowance and usage, roaming status and outstanding balance."),
        AIFunctionFactory.Create(GetRecentBillsAsync, name: "get_recent_bills",
            description: "Get the customer's most recent monthly bills with amount, due date and payment status."),
        AIFunctionFactory.Create(ListAvailablePlansAsync, name: "list_available_plans",
            description: "List all mobile plans the customer can switch to, with prices and allowances."),
        AIFunctionFactory.Create(ChangePlanAsync, name: "change_plan",
            description: "Switch the customer to another plan. Only call with customerConfirmed=true after the customer explicitly agreed."),
        AIFunctionFactory.Create(SetRoamingAsync, name: "set_roaming",
            description: "Turn international roaming on or off. Only call with customerConfirmed=true after the customer explicitly agreed."),
        AIFunctionFactory.Create(CreateSupportTicketAsync, name: "create_support_ticket",
            description: "Open a support ticket for an issue that needs investigation by the technical or billing team."),
        AIFunctionFactory.Create(GetMyTicketsAsync, name: "get_my_tickets",
            description: "List the customer's support tickets and their status."),
        AIFunctionFactory.Create(SearchKnowledgeBaseAsync, name: "search_knowledge_base",
            description: "Search the official help centre (policies, roaming packs, eSIM, payments, coverage, troubleshooting)."),
        AIFunctionFactory.Create(EscalateToHumanAgentAsync, name: "escalate_to_human_agent",
            description: "Transfer the conversation to a human support agent.")
    ];

    // ------------------------------------------------------------------ tools

    private async Task<string> GetAccountSummaryAsync(CancellationToken cancellationToken = default)
    {
        await TrackAsync("get_account_summary");
        var account = await LoadAccountAsync(cancellationToken);
        if (account is null) return NoAccount;

        var outstanding = account.Bills.Where(b => b.Status != BillStatus.Paid).Sum(b => b.AmountQar);
        return Serialize(new
        {
            account.AccountNumber,
            account.MobileNumber,
            account.FullName,
            plan = new
            {
                account.Plan.Code,
                name = account.Plan.NameEn,
                account.Plan.MonthlyPriceQar,
                dataGb = account.Plan.DataGb?.ToString() ?? "Unlimited",
                localMinutes = account.Plan.LocalMinutes?.ToString() ?? "Unlimited",
                account.Plan.InternationalMinutes,
                account.Plan.Is5G
            },
            account.DataUsedGb,
            dataRemainingGb = account.Plan.DataGb is { } gb ? Math.Max(0, gb - account.DataUsedGb) : (decimal?)null,
            roamingEnabled = account.IsRoamingEnabled,
            outstandingBalanceQar = outstanding,
            hasOverdueBill = account.HasOverdueBills
        });
    }

    private async Task<string> GetRecentBillsAsync(
        [Description("How many recent bills to return (1-12).")] int count = 3,
        CancellationToken cancellationToken = default)
    {
        await TrackAsync("get_recent_bills");
        var account = await LoadAccountAsync(cancellationToken);
        if (account is null) return NoAccount;

        var bills = account.Bills
            .OrderByDescending(b => b.Period)
            .Take(Math.Clamp(count, 1, 12))
            .Select(b => new
            {
                month = b.Period.ToString("MMMM yyyy"),
                b.AmountQar,
                dueDate = b.DueDate.ToString("yyyy-MM-dd"),
                status = b.Status.ToString(),
                paidOn = b.PaidAt?.ToString("yyyy-MM-dd")
            });

        return Serialize(bills);
    }

    private async Task<string> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        await TrackAsync("list_available_plans");
        var account = await LoadAccountAsync(cancellationToken);
        var plans = await db.Plans.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.MonthlyPriceQar)
            .ToListAsync(cancellationToken);

        return Serialize(plans.Select(p => new
        {
            p.Code,
            name = p.NameEn,
            nameArabic = p.NameAr,
            p.MonthlyPriceQar,
            dataGb = p.DataGb?.ToString() ?? "Unlimited",
            localMinutes = p.LocalMinutes?.ToString() ?? "Unlimited",
            p.InternationalMinutes,
            p.Is5G,
            isCurrentPlan = account?.PlanId == p.Id
        }));
    }

    private async Task<string> ChangePlanAsync(
        [Description("Code of the new plan, e.g. SMART-150.")] string planCode,
        [Description("True only if the customer explicitly confirmed this change in the chat.")] bool customerConfirmed,
        CancellationToken cancellationToken = default)
    {
        await TrackAsync("change_plan");
        var account = await LoadAccountAsync(cancellationToken);
        if (account is null) return NoAccount;

        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Code == planCode.Trim().ToUpper(), cancellationToken);
        if (plan is null) return $"NOT ALLOWED: there is no plan with code '{planCode}'. Use list_available_plans.";

        if (!customerConfirmed)
            return $"CONFIRMATION NEEDED: tell the customer the new plan will be '{plan.NameEn}' at QAR {plan.MonthlyPriceQar}/month, " +
                   "effective immediately and prorated on the next bill, then ask them to confirm.";

        try
        {
            var oldPlan = account.Plan.NameEn;
            account.ChangePlan(plan);
            await db.SaveChangesAsync(cancellationToken);
            return $"SUCCESS: plan changed from '{oldPlan}' to '{plan.NameEn}' (QAR {plan.MonthlyPriceQar}/month). " +
                   "A confirmation SMS will be sent to the customer.";
        }
        catch (DomainException ex)
        {
            return $"NOT ALLOWED: {ex.Message}";
        }
    }

    private async Task<string> SetRoamingAsync(
        [Description("True to activate roaming, false to deactivate.")] bool enabled,
        [Description("True only if the customer explicitly confirmed this change in the chat.")] bool customerConfirmed,
        CancellationToken cancellationToken = default)
    {
        await TrackAsync("set_roaming");
        var account = await LoadAccountAsync(cancellationToken);
        if (account is null) return NoAccount;

        if (!customerConfirmed)
            return $"CONFIRMATION NEEDED: ask the customer to confirm they want roaming turned {(enabled ? "ON" : "OFF")}.";

        try
        {
            account.SetRoaming(enabled);
            await db.SaveChangesAsync(cancellationToken);
            return $"SUCCESS: roaming is now {(enabled ? "ON" : "OFF")}.";
        }
        catch (DomainException ex)
        {
            return $"NOT ALLOWED: {ex.Message}";
        }
    }

    private async Task<string> CreateSupportTicketAsync(
        [Description("One of: Billing, Network, Plan, Roaming, Device, Account, Other.")] string category,
        [Description("One of: Low, Medium, High, Urgent.")] string priority,
        [Description("Short subject in English, max 80 characters.")] string subject,
        [Description("Clear description of the problem in English, including what the customer already tried.")] string description,
        CancellationToken cancellationToken = default)
    {
        await TrackAsync("create_support_ticket");
        if (customerAccountId is null) return NoAccount;

        if (!Enum.TryParse<TicketCategory>(category, ignoreCase: true, out var cat)) cat = TicketCategory.Other;
        if (!Enum.TryParse<TicketPriority>(priority, ignoreCase: true, out var prio)) prio = TicketPriority.Medium;

        try
        {
            var ticket = SupportTicket.Open(
                customerAccountId.Value, cat, prio,
                subject.Length > 120 ? subject[..120] : subject,
                description,
                conversation.Id,
                createdByAi: true);

            db.SupportTickets.Add(ticket);
            await db.SaveChangesAsync(cancellationToken);
            return $"SUCCESS: ticket {ticket.Number} created ({cat}, priority {prio}). The team usually responds within 24 hours.";
        }
        catch (DomainException ex)
        {
            return $"NOT ALLOWED: {ex.Message}";
        }
    }

    private async Task<string> GetMyTicketsAsync(CancellationToken cancellationToken = default)
    {
        await TrackAsync("get_my_tickets");
        if (customerAccountId is null) return NoAccount;

        var tickets = await db.SupportTickets.AsNoTracking()
            .Where(t => t.CustomerAccountId == customerAccountId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        return tickets.Count == 0
            ? "The customer has no support tickets."
            : Serialize(tickets.Select(t => new
            {
                t.Number,
                t.Subject,
                category = t.Category.ToString(),
                status = t.Status.ToString(),
                created = t.CreatedAt.ToString("yyyy-MM-dd")
            }));
    }

    private async Task<string> SearchKnowledgeBaseAsync(
        [Description("What to look for, e.g. 'roaming packs Saudi Arabia' or 'how to activate eSIM'.")] string query,
        CancellationToken cancellationToken = default)
    {
        await TrackAsync("search_knowledge_base");
        var hits = await knowledge.SearchAsync(query, top: 4, cancellationToken);
        if (hits.Count == 0) return "No relevant help centre articles were found.";

        foreach (var title in hits.Select(h => h.ArticleTitle).Distinct())
            if (!_sources.Contains(title)) _sources.Add(title);

        return string.Join("\n\n---\n\n", hits.Select(h => $"Article: {h.ArticleTitle}\n{h.Content}"));
    }

    private async Task<string> EscalateToHumanAgentAsync(
        [Description("Why a human is needed, in one sentence (English).")] string reason,
        CancellationToken cancellationToken = default)
    {
        await TrackAsync("escalate_to_human_agent");

        if (conversation.Status != ConversationStatus.Bot)
            return "The conversation is already with the human support team.";

        conversation.Escalate(reason);
        var systemMessage = conversation.AddMessage(MessageSender.System,
            conversation.Language == "ar"
                ? "تم تحويل المحادثة إلى أحد موظفي خدمة العملاء."
                : "This conversation has been transferred to a human support agent.");
        await db.SaveChangesAsync(cancellationToken);

        await notifier.MessageAddedAsync(systemMessage.ToDto());
        await notifier.ConversationUpdatedAsync(conversation.Id, conversation.Status);

        return "SUCCESS: escalated. Tell the customer a human agent will join this chat shortly (usually within a few minutes).";
    }

    // ---------------------------------------------------------------- helpers

    private async Task TrackAsync(string tool)
    {
        _usedTools.Add(tool);
        await notifier.ToolActivityAsync(conversation.Id, tool);
    }

    private async Task<CustomerAccount?> LoadAccountAsync(CancellationToken ct)
    {
        if (customerAccountId is null) return null;
        return await db.CustomerAccounts
            .Include(a => a.Plan)
            .Include(a => a.Bills)
            .FirstOrDefaultAsync(a => a.Id == customerAccountId, ct);
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
