using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Persistence;
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

// Queue selection mirrors the API: exactly one provider (ADR-005).
builder.Services.AddOrderQueue(builder.Configuration);

builder.Services.AddHostedService<OrderProcessorHost>();

var host = builder.Build();
host.Run();

