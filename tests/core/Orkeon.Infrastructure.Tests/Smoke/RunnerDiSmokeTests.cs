using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tools.Abstractions.DependencyInjection;
using Orkeon.Tools.Code.DependencyInjection;
using Orkeon.Tools.Data.DependencyInjection;
using Orkeon.Tools.FileSystem.DependencyInjection;
using Orkeon.Tools.Web.DependencyInjection;

namespace Orkeon.Infrastructure.Tests.Smoke;

/// <summary>
/// End-to-end DI smoke tests for the example runner topology. Each time a
/// tool gains a new IFileSystemService/IPathValidator/etc. constructor, it
/// risks creating an ambiguous overload that the default
/// <see cref="ServiceCollection"/> resolver cannot disambiguate. Such
/// regressions surface only at runtime when the first crew starts —
/// R13 (2026-04-20) lost a full day to this after commits
/// 6a968f94 (p3-vfs-22) and a9d474f2 (p2-vfs-14).
///
/// These tests re-build the DI graph the runner assembles in
/// <c>RunnerHost.Build</c>, resolve every <see cref="IBaseTool"/>, and
/// fail eagerly with a readable error if any ctor is ambiguous or any
/// required dependency is missing.
/// </summary>
public class RunnerDiSmokeTests
{
    private static (IServiceProvider Provider, string MountBase) BuildRunnerLikeProvider()
    {
        // Register at least one mount so AddOrkeonFileSystem can bind FileSystemRegistry.
        // Using a per-test temp dir avoids polluting the workspace.
        var mountBase = Path.Combine(Path.GetTempPath(), "orkeon-smoke-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(mountBase);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:FileSystem:Mounts:0"] = $"{mountBase}:/tmp:rw",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // Exactly the extensions RunnerHost.Build calls for a typical crew run.
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        services.AddOrkeonFileSystem(configuration);
        services.AddOrkeonFileSystemTools();
        services.AddOrkeonDataTools();
        services.AddOrkeonWebTools();
        services.AddOrkeonCodeTools();
        services.AddOrkeonAbstractionTools();

        return (services.BuildServiceProvider(), mountBase);
    }

    private static void Cleanup(string mountBase)
    {
        try { Directory.Delete(mountBase, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void AllTools_Resolve_WithoutAmbiguousConstructorException()
    {
        var (provider, mountBase) = BuildRunnerLikeProvider();
        try
        {
            using var sp = (ServiceProvider)provider;

            // The failure observed in R13 happens at resolution time, not at
            // Build time — force it by enumerating every IBaseTool registration.
            var tools = sp.GetServices<IBaseTool>().ToList();

            Assert.NotEmpty(tools);
            // Spot-check tools that were actually broken in R13 to keep this
            // test specific rather than "at least one tool works".
            Assert.Contains(tools, t => t.GetType().Name == "DocxWriteTool");
            Assert.Contains(tools, t => t.GetType().Name == "XlsxWriteTool");
            Assert.Contains(tools, t => t.GetType().Name == "CsvReaderTool");
            Assert.Contains(tools, t => t.GetType().Name == "ImageGenerationTool");
            // count_pattern is the R13.2 addition — smoke-check it surfaces too.
            Assert.Contains(tools, t => t.GetType().Name == "CountPatternTool");
            // list_mounts comes from Orkeon.Tools.Abstractions — added in experiment 07 #13.
            Assert.Contains(tools, t => t.Name == "list_mounts");
        }
        finally
        {
            Cleanup(mountBase);
        }
    }

    [Fact]
    public void AllTools_HaveUniqueNames()
    {
        var (provider, mountBase) = BuildRunnerLikeProvider();
        try
        {
            using var sp = (ServiceProvider)provider;
            var tools = sp.GetServices<IBaseTool>().ToList();

            var duplicates = tools
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.Empty(duplicates);
        }
        finally
        {
            Cleanup(mountBase);
        }
    }
}
