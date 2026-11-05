using FlashSale.Application.Automation;
using FlashSale.Application.Inventory;
using FlashSale.Application.Messaging;
using FlashSale.Application.Persistence;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace FlashSale.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFlashSaleInfrastructure(this IServiceCollection services, string connectionString, string redisConnectionString)
    {
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddScoped(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IWriteTransaction, EfWriteTransaction>();
        services.AddScoped<IOrderReadModel, OrderReadModel>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ICheckoutSagaRepository, CheckoutSagaRepository>();
        services.AddScoped<IAutomationRunRepository, AutomationRunRepository>();
        services.AddScoped<IStockReservationGateway, RedisStockGateway>();
        services.AddScoped<IOrderQueueProducer, RabbitMQOrderQueue>();
        services.AddScoped<IOrderQueueConsumer, RabbitMQOrderQueue>();
        return services;
    }
}
