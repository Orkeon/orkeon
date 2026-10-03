using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;
using Orkeon.Rag.Factories;

namespace Orkeon.Host.Tests;

/// <summary>
/// GAP-25 — the <c>balanced</c> and <c>quality</c> RAG profiles rerank with the ONNX
/// cross-encoder (<c>onnx</c>), and only the <c>orkeon</c> CLI registered it: a hosted crew whose
/// <c>knowledge:</c> asked for either profile failed its first retrieval with "Unknown reranker
/// 'onnx'". The host is built the way <c>Program</c> builds it: the runner host plus
/// <see cref="HostServiceRegistration.AddHostServices"/>. It is built, never started.
/// </summary>
public sealed class HostRagProfilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-host-rag-{Guid.NewGuid():N}");

    public HostRagProfilesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void The_host_resolves_the_onnx_reranker_the_balanced_and_quality_profiles_name()
    {
        var settings = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settings, "{ \"RaggableTree\": { \"Enabled\": false } }");

        using var host = RunnerHost.Build(
            settings,
            new RunnerMountPlan { InternalMounts = [$"{FileSystemMount.Quote(_root)}:{RunnerVirtualRoots.Crew}:ro"] },
            configureServices: (context, services) =>
                services.AddHostServices(context.Configuration, HostCrewMounts.For([])));

        var rerankers = host.Services.GetRequiredService<RerankerFactory>();

        Assert.True(rerankers.IsKnown("onnx"), "known: " + string.Join(", ", rerankers.KnownNames));
        Assert.Equal("onnx", rerankers.Create("onnx").Name);
    }

    [Fact]
    public void The_hosts_allow_list_applies_to_the_rag_llm_profile()
    {
        // GAP-19: Orkeon:Host:LlmProfiles decides which profiles answer, for the RAG subsystem as
        // for the crews — a RAG profile it leaves out refuses the start.
        var settings = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settings, """
            {
              "Llm": {
                "BaseUrl": "https://api.deepseek.com/v1", "ApiKey": "sk-ds", "Model": "deepseek-chat",
                "Profiles": {
                  "claude": { "BaseUrl": "https://api.anthropic.com/v1", "ApiKey": "sk-ant", "Model": "claude-sonnet-5" },
                  "local": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" }
                }
              },
              "Orkeon": { "Host": { "LlmProfiles": ["claude"] }, "Rag": { "LlmProfile": "local" } },
              "RaggableTree": { "Enabled": false }
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(() => RunnerHost.Build(
            settings,
            new RunnerMountPlan { InternalMounts = [$"{FileSystemMount.Quote(_root)}:{RunnerVirtualRoots.Crew}:ro"] },
            configureServices: (context, services) =>
                services.AddHostServices(context.Configuration, HostCrewMounts.For([]))));

        Assert.Contains("Orkeon:Rag:LlmProfile", error.Message, StringComparison.Ordinal);
        Assert.Contains("'local'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, claude.", error.Message, StringComparison.Ordinal);
    }
}
