using SmartSupport.Domain.Common;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;

namespace SmartSupport.UnitTests;

public class ConversationTests
{
    private static Conversation NewConversation() => Conversation.Start(Guid.NewGuid(), "en");

    [Fact]
    public void New_conversation_is_handled_by_the_bot()
    {
        var conversation = NewConversation();

        Assert.Equal(ConversationStatus.Bot, conversation.Status);
        Assert.Null(conversation.AssignedAgentId);
    }

    [Fact]
    public void Escalate_moves_conversation_to_the_agent_queue()
    {
        var conversation = NewConversation();

        conversation.Escalate("Customer is upset");

        Assert.Equal(ConversationStatus.WaitingForAgent, conversation.Status);
        Assert.Equal("Customer is upset", conversation.EscalationReason);
        Assert.NotNull(conversation.EscalatedAt);
    }

    [Fact]
    public void Cannot_escalate_twice()
    {
        var conversation = NewConversation();
        conversation.Escalate("first");

        Assert.Throws<DomainException>(() => conversation.Escalate("second"));
    }

    [Fact]
    public void Agent_cannot_claim_a_conversation_that_was_not_escalated()
    {
        var conversation = NewConversation();

        Assert.Throws<DomainException>(() => conversation.AssignTo(Guid.NewGuid()));
    }

    [Fact]
    public void Second_agent_cannot_take_over_a_claimed_conversation()
    {
        var conversation = NewConversation();
        conversation.Escalate("help");
        conversation.AssignTo(Guid.NewGuid());

        Assert.Throws<DomainException>(() => conversation.AssignTo(Guid.NewGuid()));
    }

    [Fact]
    public void Same_agent_can_claim_again_safely()
    {
        var agentId = Guid.NewGuid();
        var conversation = NewConversation();
        conversation.Escalate("help");
        conversation.AssignTo(agentId);

        conversation.AssignTo(agentId);

        Assert.Equal(ConversationStatus.WithAgent, conversation.Status);
        Assert.Equal(agentId, conversation.AssignedAgentId);
    }

    [Fact]
    public void Resolving_without_escalation_counts_as_resolved_by_ai()
    {
        var conversation = NewConversation();

        conversation.Resolve();

        Assert.Equal(ConversationStatus.Resolved, conversation.Status);
        Assert.True(conversation.ResolvedByAi);
    }

    [Fact]
    public void Resolving_after_escalation_is_not_counted_as_ai_resolution()
    {
        var conversation = NewConversation();
        conversation.Escalate("help");
        conversation.AssignTo(Guid.NewGuid());

        conversation.Resolve();

        Assert.False(conversation.ResolvedByAi);
    }

    [Fact]
    public void Cannot_add_messages_to_a_resolved_conversation()
    {
        var conversation = NewConversation();
        conversation.Resolve();

        Assert.Throws<DomainException>(() => conversation.AddMessage(MessageSender.Customer, "hello?"));
    }

    [Fact]
    public void AddMessage_stores_tools_and_sources_and_updates_last_activity()
    {
        var conversation = NewConversation();

        var message = conversation.AddMessage(
            MessageSender.Assistant, "  Your bill is QAR 100.  ",
            toolsUsed: ["get_recent_bills", "get_recent_bills"],
            sources: ["How to pay your bill"]);

        Assert.Equal("Your bill is QAR 100.", message.Content);
        Assert.Equal("get_recent_bills", message.ToolsUsed);
        Assert.Equal("How to pay your bill", message.Sources);
        Assert.Equal(message.CreatedAt, conversation.LastMessageAt);
        Assert.Single(conversation.Messages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_messages_are_rejected(string content)
    {
        Assert.Throws<DomainException>(() => NewConversation().AddMessage(MessageSender.Customer, content));
    }
}

public class CustomerAccountTests
{
    private static Plan NewPlan(string code, bool active = true) => new()
    {
        Code = code,
        NameEn = code,
        NameAr = code,
        MonthlyPriceQar = 100,
        IsActive = active
    };

    private static CustomerAccount NewAccount(Plan plan, params BillStatus[] bills)
    {
        var account = new CustomerAccount
        {
            AccountNumber = "SA-1",
            MobileNumber = "+974 5000 0000",
            FullName = "Test Customer",
            PlanId = plan.Id,
            Plan = plan
        };
        foreach (var status in bills)
            account.Bills.Add(new Bill { AmountQar = 100, Status = status });
        return account;
    }

    [Fact]
    public void ChangePlan_switches_to_the_new_plan()
    {
        var account = NewAccount(NewPlan("SMART-50"), BillStatus.Paid);
        var target = NewPlan("SMART-150");

        account.ChangePlan(target);

        Assert.Equal(target.Id, account.PlanId);
    }

    [Fact]
    public void ChangePlan_is_blocked_when_a_bill_is_overdue()
    {
        var account = NewAccount(NewPlan("SMART-50"), BillStatus.Paid, BillStatus.Overdue);

        var ex = Assert.Throws<DomainException>(() => account.ChangePlan(NewPlan("SMART-150")));
        Assert.Contains("overdue", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangePlan_to_retired_plan_is_rejected()
    {
        var account = NewAccount(NewPlan("SMART-50"));

        Assert.Throws<DomainException>(() => account.ChangePlan(NewPlan("LEGACY-75", active: false)));
    }

    [Fact]
    public void ChangePlan_to_the_same_plan_is_rejected()
    {
        var plan = NewPlan("SMART-100");
        var account = NewAccount(plan);

        Assert.Throws<DomainException>(() => account.ChangePlan(plan));
    }

    [Fact]
    public void Roaming_cannot_be_enabled_with_an_overdue_bill_but_can_be_disabled()
    {
        var account = NewAccount(NewPlan("SMART-50"), BillStatus.Overdue);
        account.IsRoamingEnabled = true;

        Assert.Throws<DomainException>(() => account.SetRoaming(true));
        account.SetRoaming(false);
        Assert.False(account.IsRoamingEnabled);
    }
}

public class SupportTicketTests
{
    [Fact]
    public void Open_creates_a_numbered_open_ticket()
    {
        var ticket = SupportTicket.Open(Guid.NewGuid(), TicketCategory.Network, TicketPriority.High,
            " No signal at home ", "Calls drop in Zone 74", createdByAi: true);

        Assert.StartsWith("TCK-", ticket.Number);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal("No signal at home", ticket.Subject);
        Assert.True(ticket.CreatedByAi);
    }

    [Fact]
    public void Closed_ticket_cannot_be_reopened()
    {
        var ticket = SupportTicket.Open(Guid.NewGuid(), TicketCategory.Billing, TicketPriority.Low, "s", "d");
        ticket.ChangeStatus(TicketStatus.Closed);

        Assert.Throws<DomainException>(() => ticket.ChangeStatus(TicketStatus.Open));
    }
}
