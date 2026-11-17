using Microsoft.Extensions.DependencyInjection;
using SmartSupport.Application.Accounts;
using SmartSupport.Application.Analytics;
using SmartSupport.Application.Chat;
using SmartSupport.Application.Conversations;
using SmartSupport.Application.Insights;
using SmartSupport.Application.Knowledge;
using SmartSupport.Application.Tickets;

namespace SmartSupport.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<SupportAgent>();
        services.AddScoped<ConversationService>();
        services.AddScoped<InsightsService>();
        services.AddScoped<KnowledgeService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<TicketService>();
        services.AddScoped<AccountService>();
        return services;
    }
}
