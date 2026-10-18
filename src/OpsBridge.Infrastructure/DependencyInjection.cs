using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsBridge.Application.Connectors;
using OpsBridge.Application.Contracts;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Application.Ingestion;
using OpsBridge.Application.Security;
using OpsBridge.Application.UseCases;
using OpsBridge.Infrastructure.Connectors;
using OpsBridge.Infrastructure.Contracts;
using OpsBridge.Infrastructure.Persistence;
using OpsBridge.Infrastructure.Security;

namespace OpsBridge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOpsBridgeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OpsBridgeDb")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=OpsBridgeDb;Username=postgres;Password=postgres";

        services.AddDbContext<OpsBridgeDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Repositories
        services.AddScoped<IConnectorRepository, EfConnectorRepository>();
        services.AddScoped<IIngestedEventRepository, EfIngestedEventRepository>();
        services.AddScoped<IInboxRepository, EfInboxRepository>();

        // Adapters
        services.AddSingleton<IWebhookSignatureVerifier, HmacWebhookVerifier>();
        services.AddSingleton<IContractValidator, JsonContractValidator>();
        services.AddHttpClient<IFactoryFlowClient, MockFactoryFlowClient>();

        // Application Use Cases
        services.AddScoped<ReceiveWebhookUseCase>();

        return services;
    }
}
