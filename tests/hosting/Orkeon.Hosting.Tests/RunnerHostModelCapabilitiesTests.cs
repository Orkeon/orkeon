#pragma warning disable ORKEXP005 // The capability registry is experimental; this test reads what the host resolved.
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.LLMs.Capabilities;

namespace Orkeon.Hosting.Tests;

/// <summary>FWK final review L7: <c>"ReasoningEfforts": []</c> in the settings file says the model takes no effort.</summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostModelCapabilitiesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-model-capabilities-" + Guid.NewGuid().ToString("N"));

    public RunnerHostModelCapabilitiesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void An_empty_json_array_of_efforts_means_none_and_a_missing_one_means_no_restriction()
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, """
            { "RaggableTree": { "Enabled": false },
              "Llm": { "ModelCapabilities": { "acme-none": { "ReasoningEfforts": [] }, "acme-free": { "AudioInput": true },
                                              "acme-always": { "Thinking": "EffortOnly" } } } }
            """);
        var original = Console.Error;
        using var muted = new StringWriter();
        Console.SetError(muted);
        try
        {
            using var host = RunnerHost.Build(path, new RunnerMountPlan());
            var registry = host.Services.GetRequiredService<IModelCapabilityRegistry>();

            Assert.Empty(registry.TryGet("acme-none-1")!.ReasoningEfforts!);
            Assert.Null(registry.TryGet("acme-free-1")!.ReasoningEfforts);
            // The Thinking key of the settings file (#356); left out, the provider's value rules.
            Assert.Equal(Orkeon.Domain.SharedKernel.ValueObjects.ThinkingSupport.EffortOnly, registry.TryGet("acme-always-1")!.Thinking);
            Assert.Null(registry.TryGet("acme-free-1")!.Thinking);
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
