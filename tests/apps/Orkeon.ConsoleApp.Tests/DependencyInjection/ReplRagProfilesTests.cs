using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Cli.Commands.Scripting.Configuration;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Rag.Factories;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// GAP-25 — the <c>balanced</c> and <c>quality</c> RAG profiles rerank with the ONNX
/// cross-encoder (<c>onnx</c>), which only the <c>orkeon</c> CLI registered: <c>rag_search</c>
/// under either profile failed in the REPL with "Unknown reranker 'onnx'". The container is the
/// one <c>Program</c> composes; it is built, never run.
/// </summary>
public sealed class ReplRagProfilesTests
{
    [Fact]
    public void The_repl_resolves_the_onnx_reranker_the_balanced_and_quality_profiles_name()
    {
        // The reranker reads its model through the VFS, so the container needs one mount —
        // given the way an operator gives it, with --mount.
        var workspace = Directory.CreateTempSubdirectory("orkeon-repl-rag-").FullName;
        try
        {
            using var host = Host.CreateDefaultBuilder([])
                .ConfigureServices((context, services) => Program.ConfigureServices(
                    context, services, UiMode.Plain,
                    ScriptedCommandsCliOptions.Parse(["--mount", $"{workspace}:/workspace:ro"]),
                    replWordWrap: false))
                .Build();

            var rerankers = host.Services.GetRequiredService<RerankerFactory>();

            Assert.True(rerankers.IsKnown("onnx"), "known: " + string.Join(", ", rerankers.KnownNames));
            Assert.Equal("onnx", rerankers.Create("onnx").Name);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
