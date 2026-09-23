using FlashSale.Application.Automation;
using FlashSale.Application.Events;
using FlashSale.Application.Automation;
using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Persistence;
using FlashSale.Infrastructure.Automation;
using FlashSale.Infrastructure.Events;
using FlashSale.Infrastructure.Messaging;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Clean Architecture: the worker is another composition root over the same
// Application ports — the use case (OrderProcessor) is shared with the API.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=FlashSaleDb;Username=postgres;Password=postgres;Maximum Pool Size=40";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<OrderProcessor>();

// Queue selection mirrors the API: exactly one provider (ADR-005), and the same
// Production guard. Both composition roots must resolve the SAME provider, or the
// API publishes to a broker nobody consumes from.
builder.Services.AddOrderQueue(builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddHostedService<OrderProcessorHost>();

// Business automation platform (spec §1/§11): the same event publisher plus the
// audit-first AutomationWorkerHost (consume automation.events → AutomationRun
// record → workflow logic). Same ADR-005 gate as the order queue: RabbitMQ only,
// so API and worker stay on one topology; both composition roots must agree.
builder.Services.AddDomainEventPublisher(builder.Configuration);
builder.Services.AddAutomationWorkerHost(builder.Configuration);

// Payment automation (spec §4/§5): config-bound options, payment transitions,
// and the periodic abandoned-payment scan. Provider-independent (DB-backed),
// so it runs regardless of the messaging gate above.
var paymentOptions = builder.Configuration
    .GetSection(PaymentAutomationOptions.SectionName).Get<PaymentAutomationOptions>()
    ?? new PaymentAutomationOptions();
builder.Services.AddSingleton(paymentOptions);
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<PaymentAutomationUseCase>();
builder.Services.AddHostedService<PaymentTimeoutHostedService>();

// Inventory automation (spec §6): periodic low-stock scan, same use case as the
// API's manual trigger (trigger_type distinguishes timer vs manual in audit).
var inventoryOptions = builder.Configuration
    .GetSection(InventoryAutomationOptions.SectionName).Get<InventoryAutomationOptions>()
    ?? new InventoryAutomationOptions();
builder.Services.AddSingleton(inventoryOptions);
builder.Services.AddScoped<IStockAlertRepository, StockAlertRepository>();
builder.Services.AddScoped<LowStockAlertUseCase>();
builder.Services.AddHostedService<LowStockScanHostedService>();

var host = builder.Build();
host.Run();

