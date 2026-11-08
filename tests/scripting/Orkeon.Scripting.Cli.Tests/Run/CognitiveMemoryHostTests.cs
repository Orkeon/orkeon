using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// FWK-04 — the runner host, built as the binaries build it, wires the cognitive memory under
/// <c>Orkeon:CognitiveMemory:Enabled</c> and only then, with the prerequisites it resolves from.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class CognitiveMemoryHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-cognitive-host-" + Guid.NewGuid().ToString("N"));

    public CognitiveMemoryHostTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private IHost BuildHost(bool enabled)
    {
        var settings = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(
            settings,
            $$"""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "CognitiveMemory": { "Enabled": {{(enabled ? "true" : "false")}} } } }""");
        using var console = new TestConsole();
        return RunnerHost.Build(
            settings,
            new RunnerMountPlan { CliMounts = [$"{Orkeon.Domain.FileSystem.FileSystemMount.Quote(_root)}:/crew:ro"], AllowExternalMounts = true },
            configureLogging: (_, logging) => logging.SetMinimumLevel(LogLevel.None),
            configureServices: (_, _) => { });
    }

    [Fact]
    public void Enabled_resolves_the_cognitive_services_and_their_prerequisites()
    {
        using var host = BuildHost(enabled: true);

        var cognitive = host.Services.GetRequiredService<ICognitiveMemoryService>();

        Assert.Same(cognitive, host.Services.GetRequiredService<ICognitiveMemoryService>());
        Assert.NotNull(host.Services.GetRequiredService<IMemoryService>());
        Assert.NotNull(host.Services.GetService<IEmbeddingProvider>());
        Assert.Contains(host.Services.GetServices<IBaseTool>(), tool => tool.Name == "memory_recall");
    }

    [Fact]
    public void Disabled_leaves_the_cognitive_memory_off()
    {
        using var host = BuildHost(enabled: false);

        Assert.Null(host.Services.GetService<ICognitiveMemoryService>());
        Assert.DoesNotContain(host.Services.GetServices<IBaseTool>(), tool => tool.Name == "memory_recall");
    }
}
