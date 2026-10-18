using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsBridge.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OpsBridge.IntegrationTests;

public sealed class OpsBridgeFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("OpsBridgeTestDb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing DbContext registration
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<OpsBridgeDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // Register with Testcontainers connection string
                services.AddDbContext<OpsBridgeDbContext>(options =>
                    options.UseNpgsql(ConnectionString));
            });
        });

        // Ensure database schema is created
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsBridgeDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task ResetAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsBridgeDbContext>();
        db.IngestedEvents.RemoveRange(db.IngestedEvents);
        db.InboxMessages.RemoveRange(db.InboxMessages);
        db.Connectors.RemoveRange(db.Connectors);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory != null)
        {
            await Factory.DisposeAsync();
        }
        await _postgres.DisposeAsync();
    }
}
