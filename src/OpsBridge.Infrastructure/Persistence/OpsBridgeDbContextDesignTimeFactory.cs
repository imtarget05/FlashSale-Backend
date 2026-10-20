using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpsBridge.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef migrations` works for
/// <see cref="OpsBridgeDbContext"/> without booting the API host.
///
/// The connection string is only used for design-time tooling (scaffolding and
/// `dotnet ef database update`); it is never read at runtime — the runtime
/// wiring lives in <see cref="DependencyInjection.AddOpsBridgeInfrastructure"/>.
/// </summary>
public sealed class OpsBridgeDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OpsBridgeDbContext>
{
    public OpsBridgeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OpsBridgeDb")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=OpsBridgeDb;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<OpsBridgeDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new OpsBridgeDbContext(options);
    }
}
