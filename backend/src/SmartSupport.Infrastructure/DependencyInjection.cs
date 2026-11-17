using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartSupport.Application.Abstractions;
using SmartSupport.Domain.Entities;
using SmartSupport.Infrastructure.Ai;
using SmartSupport.Infrastructure.Knowledge;
using SmartSupport.Infrastructure.Persistence;
using SmartSupport.Infrastructure.Workers;

namespace SmartSupport.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
                npgsql.EnableRetryOnFailure(maxRetryCount: 5);
            }));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DbInitializer>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddScoped<IKnowledgeSearch, KnowledgeSearch>();
        services.AddSingleton<IKnowledgeIndexQueue, KnowledgeIndexQueue>();

        services.AddHostedService<KnowledgeIndexingWorker>();
        services.AddHostedService<ConversationInsightsWorker>();

        services.AddAi(configuration);
        return services;
    }
}
