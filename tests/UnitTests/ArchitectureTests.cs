using FlashSale.Application.Orders;
using FlashSale.Domain.Entities;

namespace FlashSale.UnitTests;

/// <summary>
/// Executable guard for the Clean Architecture dependency rule — fails the build
/// if someone ever lets infrastructure leak into Domain/Application.
/// </summary>
public class ArchitectureTests
{
    private static string[] ReferencedAssemblyNames(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

    [Fact]
    public void Domain_MustNotReference_AnyInfrastructureOrFrameworkPackage()
    {
        var refs = ReferencedAssemblyNames(typeof(Order).Assembly);

        // Domain may only use the BCL/primitive assemblies.
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("StackExchange.Redis", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Azure.Messaging.ServiceBus", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("FlashSale.Application", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("FlashSale.Infrastructure", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_MustNotReference_PersistenceMessagingOrWebPackages()
    {
        var refs = ReferencedAssemblyNames(typeof(OrderProcessor).Assembly);

        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("StackExchange.Redis", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Azure.Messaging.ServiceBus", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("FlashSale.Infrastructure", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_MustDependOn_Domain()
    {
        var refs = ReferencedAssemblyNames(typeof(OrderProcessor).Assembly);
        Assert.Contains("FlashSale.Domain", refs);
    }
}
