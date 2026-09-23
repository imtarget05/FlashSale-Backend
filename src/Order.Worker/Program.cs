using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Persistence;
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

var host = builder.Build();
host.Run();

