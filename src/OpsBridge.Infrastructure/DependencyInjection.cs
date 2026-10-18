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
        services.AddScoped<IConsumerProcessingRepository, EfConsumerProcessingRepository>();
        services.AddScoped<IOutboxRepository, EfOutboxRepository>();
        services.AddScoped<OpsBridge.Application.Common.IUnitOfWork, EfUnitOfWork>();

        // Reconciliation Repositories
        services.AddScoped<OpsBridge.Application.Reconciliation.IPurchaseOrderRepository, EfPurchaseOrderRepository>();
        services.AddScoped<OpsBridge.Application.Reconciliation.IGoodsReceiptRepository, EfGoodsReceiptRepository>();
        services.AddScoped<OpsBridge.Application.Reconciliation.ISupplierInvoiceRepository, EfSupplierInvoiceRepository>();
        services.AddScoped<OpsBridge.Application.Reconciliation.IMappingRuleRepository, EfMappingRuleRepository>();
        services.AddScoped<OpsBridge.Application.Reconciliation.IReconciliationRepository, EfReconciliationRepository>();
        services.AddScoped<OpsBridge.Application.Reconciliation.IAuditEventRepository, EfAuditEventRepository>();

        // Adapters
        services.AddSingleton<IWebhookSignatureVerifier, HmacWebhookVerifier>();
        services.AddSingleton<IContractValidator, JsonContractValidator>();
        services.AddHttpClient<IFactoryFlowClient, MockFactoryFlowClient>();

        // Application Use Cases
        services.AddScoped<ReceiveWebhookUseCase>();
        services.AddScoped<ReconcileInvoiceUseCase>();
        services.AddScoped<ReviewDiscrepancyUseCase>();

        return services;
    }
}
