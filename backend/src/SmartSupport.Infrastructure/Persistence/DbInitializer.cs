using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Infrastructure.Persistence;

/// <summary>
/// Creates/migrates the database and inserts demo data on first start:
/// plans, 2 customers with bills, 1 agent, 1 admin, help-centre articles,
/// and ~3 weeks of past conversations so the analytics dashboard isn't empty.
/// </summary>
public sealed class DbInitializer(AppDbContext db, IPasswordHasher<User> hasher, ILogger<DbInitializer> logger)
{
    public const string DemoPassword = "Demo@123";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (db.Database.GetMigrations().Any())
            await db.Database.MigrateAsync(ct);
        else
            await db.Database.EnsureCreatedAsync(ct);

        if (await db.Users.AnyAsync(ct))
            return;

        logger.LogInformation("Seeding demo data...");
        await SeedAsync(ct);
        logger.LogInformation("Demo data ready. Log in with any demo account, password {Password}", DemoPassword);
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        // ---------------------------------------------------------- plans
        var smart50 = NewPlan("SMART-50", "Smart 50", "سمارت 50", 50, 5, 300, 0, is5G: false);
        var smart100 = NewPlan("SMART-100", "Smart 100", "سمارت 100", 100, 20, 1000, 30, is5G: true);
        var smart150 = NewPlan("SMART-150", "Smart 150", "سمارت 150", 150, 50, null, 100, is5G: true);
        var unlimited = NewPlan("SMART-UNL", "Smart Unlimited", "سمارت بلا حدود", 250, null, null, 300, is5G: true);
        var legacy = NewPlan("LEGACY-75", "Classic 75 (retired)", "كلاسيك 75", 75, 10, 500, 0, is5G: false);
        legacy.IsActive = false;
        db.Plans.AddRange(smart50, smart100, smart150, unlimited, legacy);

        // ---------------------------------------------------------- customer accounts
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);

        var fatimaAccount = new CustomerAccount
        {
            AccountNumber = "SA-100245",
            MobileNumber = "+974 5512 3401",
            FullName = "Fatima Al-Marri",
            Plan = smart100,
            DataUsedGb = 13.4m,
            IsRoamingEnabled = false
        };
        AddBills(fatimaAccount, thisMonth, [100m, 100m, 112.5m, 100m, 135m, 100m], overdueMonths: 0);

        var rahulAccount = new CustomerAccount
        {
            AccountNumber = "SA-100871",
            MobileNumber = "+974 3398 7720",
            FullName = "Rahul Nair",
            Plan = smart50,
            DataUsedGb = 4.8m,
            IsRoamingEnabled = false
        };
        AddBills(rahulAccount, thisMonth, [50m, 50m, 68m, 50m, 57.5m, 50m], overdueMonths: 1);

        db.CustomerAccounts.AddRange(fatimaAccount, rahulAccount);

        // ---------------------------------------------------------- users
        var fatima = NewUser("fatima@demo.qa", "Fatima Al-Marri", UserRole.Customer, "ar", fatimaAccount);
        var rahul = NewUser("rahul@demo.qa", "Rahul Nair", UserRole.Customer, "en", rahulAccount);
        var agent = NewUser("sara@demo.qa", "Sara Thomas", UserRole.Agent, "en");
        var admin = NewUser("admin@demo.qa", "Omar Haddad", UserRole.Admin, "en");
        db.Users.AddRange(fatima, rahul, agent, admin);

        // ---------------------------------------------------------- knowledge base
        foreach (var (title, category, language, content) in SeedKnowledge.Articles)
        {
            db.KnowledgeArticles.Add(new KnowledgeArticle
            {
                Title = title,
                Category = category,
                Language = language,
                Content = content.Trim(),
                Status = ArticleStatus.Pending // the indexing worker picks these up on start
            });
        }

        // ---------------------------------------------------------- tickets
        db.SupportTickets.Add(SupportTicket.Open(rahulAccount.Id, TicketCategory.Network, TicketPriority.High,
            "Weak 4G signal at home in Al Khor",
            "Customer reports calls dropping and slow data at home (Al Khor, Zone 74). Restart and network mode change did not help.",
            createdByAi: true));
        db.SupportTickets.Add(SupportTicket.Open(fatimaAccount.Id, TicketCategory.Billing, TicketPriority.Medium,
            "Question about roaming charge on August bill",
            "Customer was charged QAR 35 for roaming data while in Dubai although she believed roaming was off."));

        await db.SaveChangesAsync(ct);

        // ---------------------------------------------------------- history for analytics
        SeedHistory([fatima, rahul], agent);
        await db.SaveChangesAsync(ct);
    }

    private static readonly (string Topic, Sentiment Sentiment, bool Escalated, string Customer, string Ai, string Tools)[] HistoryTemplates =
    [
        ("Billing", Sentiment.Positive, false, "What's my bill for this month?", "Your bill for this month is QAR 100, due on the 15th.", "get_recent_bills"),
        ("Roaming", Sentiment.Positive, false, "I'm travelling to Riyadh next week, what pack do you recommend?", "The GCC Roaming Pack (QAR 50 for 7 days, 5 GB) covers Saudi Arabia.", "search_knowledge_base"),
        ("Plans", Sentiment.Positive, false, "Can I upgrade to more data?", "Done! You're now on Smart 150 with 50 GB.", "list_available_plans|change_plan"),
        ("Network", Sentiment.Negative, true, "Internet has been very slow at home for 3 days", "I've opened a network ticket and connected you with an agent.", "search_knowledge_base|create_support_ticket|escalate_to_human_agent"),
        ("Device", Sentiment.Neutral, false, "How do I activate eSIM on my iPhone?", "Request the eSIM in the app, then scan the QR code in Settings > Mobile Data.", "search_knowledge_base"),
        ("Payments", Sentiment.Neutral, false, "How can I pay my bill?", "You can pay in the app, website, stores, kiosks or by bank transfer.", "search_knowledge_base"),
        ("Billing", Sentiment.Negative, true, "I was charged for roaming but I never used it!", "I'm sorry about that. I've passed this to our billing team.", "get_recent_bills|escalate_to_human_agent"),
        ("Account", Sentiment.Neutral, true, "My phone was stolen, please block my SIM", "I'm connecting you with an agent right away to block the line.", "search_knowledge_base|escalate_to_human_agent"),
        ("Plans", Sentiment.Positive, false, "What plans have 5G?", "Smart 100, Smart 150 and Smart Unlimited all include 5G.", "list_available_plans")
    ];

    private void SeedHistory(IReadOnlyList<User> customers, User agent)
    {
        var random = new Random(42); // deterministic demo data
        var now = DateTime.UtcNow;

        for (var i = 0; i < 48; i++)
        {
            var t = HistoryTemplates[random.Next(HistoryTemplates.Length)];
            var customer = customers[random.Next(customers.Count)];
            var start = now.Date.AddDays(-random.Next(1, 21)).AddHours(random.Next(7, 22)).AddMinutes(random.Next(60));

            var conversation = Conversation.Start(customer.Id, "en");
            conversation.CreatedAt = start;
            conversation.AddMessage(MessageSender.Customer, t.Customer, customer.Id);
            conversation.AddMessage(MessageSender.Assistant, t.Ai, toolsUsed: t.Tools.Split('|'));

            if (t.Escalated)
            {
                conversation.Escalate("Seeded example escalation");
                conversation.AssignTo(agent.Id);
                conversation.AddMessage(MessageSender.Agent, "Thanks for waiting, I've taken care of this for you.", agent.Id);
            }

            conversation.Resolve();
            conversation.Sentiment = t.Sentiment;
            conversation.Topic = t.Topic;

            // Spread timestamps realistically across the conversation.
            var minute = 0;
            foreach (var message in conversation.Messages)
                message.CreatedAt = start.AddMinutes(minute += random.Next(1, 4));
            conversation.LastMessageAt = conversation.Messages[^1].CreatedAt;

            db.Conversations.Add(conversation);

            // These have private setters (only the domain may change them), so set the
            // historical values through EF's change tracker.
            var entry = db.Entry(conversation);
            if (t.Escalated)
                entry.Property(c => c.EscalatedAt).CurrentValue = start.AddMinutes(3);
            entry.Property(c => c.ResolvedAt).CurrentValue = conversation.LastMessageAt.AddMinutes(1);
        }
    }

    // ---------------------------------------------------------- helpers

    private static Plan NewPlan(string code, string en, string ar, decimal price, int? dataGb, int? minutes, int intl, bool is5G) => new()
    {
        Code = code,
        NameEn = en,
        NameAr = ar,
        MonthlyPriceQar = price,
        DataGb = dataGb,
        LocalMinutes = minutes,
        InternationalMinutes = intl,
        Is5G = is5G
    };

    private User NewUser(string email, string name, UserRole role, string language, CustomerAccount? account = null)
    {
        var user = new User
        {
            Email = email,
            FullName = name,
            Role = role,
            PreferredLanguage = language,
            CustomerAccount = account
        };
        user.PasswordHash = hasher.HashPassword(user, DemoPassword);
        return user;
    }

    /// <summary>amounts[0] = current month (unpaid), older months paid, except <paramref name="overdueMonths"/> most recent past months.</summary>
    private static void AddBills(CustomerAccount account, DateOnly thisMonth, decimal[] amounts, int overdueMonths)
    {
        for (var i = 0; i < amounts.Length; i++)
        {
            var period = thisMonth.AddMonths(-i);
            var due = period.AddDays(14);
            var status = i == 0 ? BillStatus.Unpaid : i <= overdueMonths ? BillStatus.Overdue : BillStatus.Paid;

            account.Bills.Add(new Bill
            {
                Period = period,
                AmountQar = amounts[i],
                DueDate = due,
                Status = status,
                PaidAt = status == BillStatus.Paid
                    ? due.AddDays(-3).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc)
                    : null
            });
        }
    }
}
