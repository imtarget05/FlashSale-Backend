using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace OpsBridge.UnitTests;

public class OpsBridgeArchitectureTests
{
    private static string[] ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

    [Fact]
    public void OpsBridge_Domain_MustNotReference_AnyInfrastructurePackage()
    {
        var refs = ReferencedAssemblyNames(typeof(OpsBridge.Domain.OpsBridgeRoles).Assembly);
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("StackExchange.Redis", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("RabbitMQ.Client", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("OpsBridge.Application", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("OpsBridge.Infrastructure", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("FlashSale", StringComparison.Ordinal));
    }

    [Fact]
    public void OpsBridge_Application_MustDependOn_Domain()
    {
        var refs = ReferencedAssemblyNames(typeof(OpsBridge.Application.UseCases.ReceiveWebhookUseCase).Assembly);
        Assert.Contains("OpsBridge.Domain", refs);
    }

    [Fact]
    public void OpsBridge_Infrastructure_MustNotReference_FlashSale()
    {
        var refs = ReferencedAssemblyNames(typeof(OpsBridge.Infrastructure.DependencyInjection).Assembly);
        Assert.DoesNotContain(refs, r => r.StartsWith("FlashSale.Infrastructure", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("FlashSale.Application", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("FlashSale.Domain", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectReferences_MustFollowCleanArchitectureDirection()
    {
        Assert.Empty(ProjectReferences("src/OpsBridge.Domain/OpsBridge.Domain.csproj"));
        Assert.Equal(
            ["../OpsBridge.Domain/OpsBridge.Domain.csproj"],
            ProjectReferences("src/OpsBridge.Application/OpsBridge.Application.csproj"));
        Assert.Equal(
            ["../OpsBridge.Application/OpsBridge.Application.csproj"],
            ProjectReferences("src/OpsBridge.Infrastructure/OpsBridge.Infrastructure.csproj"));
        Assert.Equal(
            ["../OpsBridge.Infrastructure/OpsBridge.Infrastructure.csproj"],
            ProjectReferences("src/OpsBridge.Api/OpsBridge.Api.csproj"));
    }

    [Fact]
    public void ApiEndpoints_MustNotDependOnInfrastructureDbContext()
    {
        var endpointDirectory = Path.Combine(RepositoryRoot(), "src", "OpsBridge.Api", "Endpoints");
        var violations = Directory.EnumerateFiles(endpointDirectory, "*Endpoints.cs")
            .Where(path => File.ReadAllText(path).Contains("OpsBridgeDbContext", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static string[] ProjectReferences(string relativeProjectPath)
    {
        var projectPath = Path.Combine(RepositoryRoot(), relativeProjectPath);
        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value.Replace('\\', '/'))
            .Where(reference => reference is not null)
            .Select(reference => reference!)
            .ToArray();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AzureFlashSale.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate AzureFlashSale.slnx.");
    }
}