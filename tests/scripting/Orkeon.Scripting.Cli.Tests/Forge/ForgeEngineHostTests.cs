using Microsoft.Extensions.DependencyInjection;
using Orkeon.Hosting;
using Orkeon.Rag.Factories;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// GAP-25 — a forge trial runs the crew <c>orkeon run</c> will run once it is promoted, so its
/// engine host offers the same RAG profiles: <c>balanced</c> and <c>quality</c> rerank with the
/// ONNX cross-encoder (<c>onnx</c>), which <c>orkeon run</c> registers and the forge did not —
/// a forged crew calling <c>rag_search</c> on a host set to <c>Orkeon:Rag:Profile = balanced</c>
/// failed its trial with "Unknown reranker 'onnx'" and worked after promotion. The host is built
/// the way the verb builds it — its mount plan and <see cref="ForgeCommand.AddEngineServices"/> —
/// and never run.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeEngineHostTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-engine-host-" + Guid.NewGuid().ToString("N"));

    public ForgeEngineHostTests()
    {
        Directory.CreateDirectory(_workspace);
        // Disabling RaggableTree keeps the on-device embedding model (ONNX) out of the host.
        File.WriteAllText(Path.Combine(_workspace, "appsettings.json"), "{ \"RaggableTree\": { \"Enabled\": false } }");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public void The_engine_host_resolves_the_onnx_reranker_the_balanced_and_quality_profiles_name()
    {
        var session = ForgeSession.Create(_workspace, "rerank");
        Directory.CreateDirectory(Path.Combine(session.Directory, TestStage.OutputDirectoryName));
        var plan = ForgeCommand.BuildMountPlan(_workspace, readRoot: null, session);

        using var host = RunnerHost.Build(
            Path.Combine(_workspace, "appsettings.json"),
            plan with { AllowExternalMounts = true },
            configureServices: (_, services) => ForgeCommand.AddEngineServices(
                services, new ForgeSubmissionBox(), new ForgeUsageTally(), new ForgeRunObserver()));

        var rerankers = host.Services.GetRequiredService<RerankerFactory>();

        Assert.True(rerankers.IsKnown("onnx"), "known: " + string.Join(", ", rerankers.KnownNames));
    }
}
