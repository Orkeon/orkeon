using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-19, decision 2, through the runner host: <c>Orkeon:Rag:LlmProfile</c> names one of the
/// host's profiles, and a name the host does not offer refuses the start, listing the ones it does
/// — like an invalid <c>Llm:Profiles</c> entry, rather than at the first RAG query of a run. Only the
/// name is checked: no provider is built before a RAG component asks for one.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostRagLlmProfileTests : IDisposable
{
    private readonly string _root;

    public RunnerHostRagLlmProfileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-rag-llm-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private Microsoft.Extensions.Hosting.IHost Build(string ragProfile)
    {
        var settingsPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settingsPath, $$"""
            {
              "Llm": {
                "BaseUrl": "https://api.deepseek.com/v1", "ApiKey": "sk-ds", "Model": "deepseek-chat",
                "Profiles": {
                  "claude": { "BaseUrl": "https://api.anthropic.com/v1", "ApiKey": "sk-ant", "Model": "claude-sonnet-5" },
                  "local": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" }
                }
              },
              "Orkeon": { "Rag": { "LlmProfile": "{{ragProfile}}" } },
              "RaggableTree": { "Enabled": false }
            }
            """);
        var original = Console.Error;
        using var muted = new StringWriter();
        Console.SetError(muted);
        try
        {
            return RunnerHost.Build(
                settingsPath: settingsPath,
                mounts: new RunnerMountPlan
                {
                    CliMounts = [$"{FileSystemMount.Quote(_root)}:/crew:ro"],
                    AllowExternalMounts = true,
                },
                configureLogging: (_, b) => b.SetMinimumLevel(LogLevel.None));
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void A_rag_profile_the_host_does_not_offer_refuses_the_start_listing_the_known_ones()
    {
        var error = Assert.Throws<RunnerSettingsException>(() => Build("claud"));

        Assert.Contains("Orkeon:Rag:LlmProfile", error.Message, StringComparison.Ordinal);
        Assert.Contains("'claud'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, claude, local.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("default")]
    [InlineData("")]
    public void A_rag_profile_the_host_offers_starts(string ragProfile)
    {
        using var host = Build(ragProfile);

        Assert.NotNull(host.Services.GetRequiredService<ILlmProfileRegistry>());
    }
}
