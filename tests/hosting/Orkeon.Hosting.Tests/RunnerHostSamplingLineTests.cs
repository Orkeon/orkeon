using Microsoft.Extensions.Logging;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-36: the startup line says what a run sends. An <c>Llm</c> section without
/// <c>Temperature</c> sends none, and the model applies its own — the line called it
/// "(default)", which read as the engine's 0.7, no longer sent.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostSamplingLineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-sampling-line-" + Guid.NewGuid().ToString("N"));

    public RunnerHostSamplingLineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string ResolvedLine(string llmSection)
    {
        var settingsPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settingsPath, $$"""{ "Llm": {{llmSection}} }""");
        using var logs = new CapturingLoggerProvider();
        using (RunnerHost.Build(
            settingsPath: settingsPath,
            mounts: new RunnerMountPlan(),
            configureLogging: (_, b) =>
            {
                b.AddProvider(logs);
                b.SetMinimumLevel(LogLevel.Information);
            }))
        {
            // Built for its startup lines only.
        }

        return Assert.Single(logs.Entries, e => e.Message.StartsWith("LLM resolved:", StringComparison.Ordinal)).Message;
    }

    [Fact]
    public void A_section_without_a_temperature_says_the_model_applies_its_own()
    {
        var line = ResolvedLine("""{ "BaseUrl": "http://localhost:11434", "Model": "qwen3" }""");

        Assert.Contains("temperature=(not set: the model's own)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_with_a_temperature_shows_it()
    {
        var line = ResolvedLine("""{ "BaseUrl": "http://localhost:11434", "Model": "qwen3", "Temperature": 0.7 }""");

        Assert.Contains("temperature=0.7", line, StringComparison.Ordinal);
    }
}
