using Microsoft.Extensions.DependencyInjection;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// End-to-end coverage for the <c>orkeon rag ingest | search</c> verbs driven
/// in-process (RAG-03/C3). The full host bootstrap and VFS mounts run for real;
/// the RAG pipelines are hand-written doubles pre-registered through the internal
/// <c>ConfigureTestServices</c> seam (they win the TryAdd race in <c>AddOrkeonRag</c>),
/// so everything stays offline and deterministic.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RagCommandTests
{
    private static readonly string[] ExpectedGlobExpansion = ["/workspace/docs/a.md", "/workspace/docs/sub/b.md"];

    // --- ingest ---

    [Fact]
    public async Task Ingest_ExpandsGlobsThroughTheVfs_AndPrintsTheCounterReport()
    {
        using var scratch = new ScriptScratch();
        var docsDir = Path.Combine(scratch.Root, "docs");
        Directory.CreateDirectory(Path.Combine(docsDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(docsDir, "a.md"), "alpha", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(docsDir, "sub", "b.md"), "beta", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(docsDir, "skip.txt"), "no", TestContext.Current.CancellationToken);

        var pipeline = new FakeIngestionPipeline
        {
            Report = new IngestionReport
            {
                Collection = "unset",
                SourcesAdded = 2,
                DocumentsLoaded = 2,
                ChunksCreated = 4,
                ChunksEmbedded = 4,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        using var console = new TestConsole();

        var exit = await RagCommand.ExecuteIngestAsync(console, new RagIngestCommandOptions
        {
            Collection = "docs",
            Sources = ["./docs/**/*.md"],
            WorkingDirectoryOverride = scratch.Root,
            ConfigureTestServices = (_, s) => s.AddSingleton<IIngestionPipeline>(pipeline),
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal("docs", pipeline.LastRequest?.Collection);
        Assert.Equal(ExpectedGlobExpansion, pipeline.LastRequest!.Sources.Select(s => s.Location));
        Assert.Contains("Ingestion report for collection 'docs':", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("2 added", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-11")]
    public async Task ShouldRefuseTheMount_WhenTheCorpusRootIsOutsideTheProcess()
    {
        using var scratch = new ScriptScratch();
        using var other = new ScriptScratch();
        Directory.CreateDirectory(Path.Combine(scratch.Root, "docs"));
        await File.WriteAllTextAsync(Path.Combine(scratch.Root, "docs", "a.md"), "alpha", TestContext.Current.CancellationToken);
        var settings = scratch.WriteFile("appsettings.json", """{ "RaggableTree": { "Enabled": false } }""");

        var refusedPipeline = new FakeIngestionPipeline();
        int refused;
        string refusedErr;
        using (var console = new TestConsole())
        {
            refused = await RagCommand.ExecuteIngestAsync(console, new RagIngestCommandOptions
            {
                Collection = "docs",
                Sources = ["./docs/**/*.md"],
                Mounts = [$"{other.OutDir}:/data:ro"],
                SettingsPath = settings,
                WorkingDirectoryOverride = scratch.Root,
                ConfigureTestServices = (_, s) => s.AddSingleton<IIngestionPipeline>(refusedPipeline),
            });
            refusedErr = console.Stderr;
        }

        Assert.NotEqual(Program.ExitOk, refused);
        Assert.Contains(other.OutDir, refusedErr, StringComparison.Ordinal);
        Assert.Contains("--allow-external-mounts", refusedErr, StringComparison.Ordinal);
        Assert.Null(refusedPipeline.LastRequest);

        var pipeline = new FakeIngestionPipeline();
        using var console2 = new TestConsole();
        var exit = await RagCommand.ExecuteIngestAsync(console2, new RagIngestCommandOptions
        {
            Collection = "docs",
            Sources = ["./docs/**/*.md"],
            SettingsPath = settings,
            WorkingDirectoryOverride = scratch.Root,
            ConfigureTestServices = (_, s) => s.AddSingleton<IIngestionPipeline>(pipeline),
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(["/workspace/docs/a.md"], pipeline.LastRequest!.Sources.Select(s => s.Location));
    }

    /// <summary>
    /// GAP-49, decision 2: a <c>/workspace</c> the <c>ORKEON_</c> environment declares is a
    /// declared root, and the verb yields to it as it yields to one of the file. It mounted its own
    /// <c>/workspace</c> over it, and read the working directory's documents instead.
    /// </summary>
    [Fact]
    public async Task Ingest_ReadsTheWorkspaceAnEnvironmentVariableDeclares()
    {
        using var scratch = new ScriptScratch();
        using var declared = new ScriptScratch();
        Directory.CreateDirectory(Path.Combine(scratch.Root, "docs"));
        Directory.CreateDirectory(Path.Combine(declared.Root, "docs"));
        await File.WriteAllTextAsync(Path.Combine(scratch.Root, "docs", "from-the-working-directory.md"), "cwd", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(declared.Root, "docs", "from-the-declared-folder.md"), "declared", TestContext.Current.CancellationToken);
        var settings = scratch.WriteFile("appsettings.json", """{ "RaggableTree": { "Enabled": false } }""");
        var pipeline = new FakeIngestionPipeline();
        using var console = new TestConsole();
        const string variable = "ORKEON_Orkeon__FileSystem__Mounts__0";
        Environment.SetEnvironmentVariable(variable, $"{declared.Root}:/workspace:ro");
        int exit;
        try
        {
            exit = await RagCommand.ExecuteIngestAsync(console, new RagIngestCommandOptions
            {
                Collection = "docs",
                Sources = ["./docs/**/*.md"],
                SettingsPath = settings,
                WorkingDirectoryOverride = scratch.Root,
                ConfigureTestServices = (_, s) => s.AddSingleton<IIngestionPipeline>(pipeline),
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(["/workspace/docs/from-the-declared-folder.md"], pipeline.LastRequest!.Sources.Select(s => s.Location));
    }

    /// <summary>
    /// GAP-49, decision 3: the verb takes neither <c>--mount-id</c> nor a crew, so the line of a
    /// root declared twice names <c>--mount</c> alone.
    /// </summary>
    [Fact]
    public async Task Ingest_ARootDeclaredTwice_NamesTheMountOptionAlone()
    {
        using var scratch = new ScriptScratch();
        var a = Directory.CreateDirectory(Path.Combine(scratch.Root, "a")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(scratch.Root, "b")).FullName;
        var idA = Orkeon.Domain.Common.MountId.Create();
        var idB = Orkeon.Domain.Common.MountId.Create();
        var settings = scratch.WriteFile("appsettings.json",
            "{ \"RaggableTree\": { \"Enabled\": false }, \"Orkeon\": { \"FileSystem\": { \"Mounts\": [ "
            + System.Text.Json.JsonSerializer.Serialize($"{idA}|{a}:/data:ro") + ", "
            + System.Text.Json.JsonSerializer.Serialize($"{idB}|{b}:/data:ro") + " ] } } }");
        var pipeline = new FakeIngestionPipeline();
        using var console = new TestConsole();

        var exit = await RagCommand.ExecuteIngestAsync(console, new RagIngestCommandOptions
        {
            Collection = "docs",
            Sources = ["./docs/**/*.md"],
            SettingsPath = settings,
            WorkingDirectoryOverride = scratch.Root,
            ConfigureTestServices = (_, s) => s.AddSingleton<IIngestionPipeline>(pipeline),
        });

        Assert.NotEqual(Program.ExitOk, exit);
        Assert.Contains(
            $"ERROR: '/data' is declared twice in {settings} ({idA}: {a}, {idB}: {b}) and nothing selects one. "
            + "Pass --mount <folder>:/data:rw to replace them all.",
            console.Stderr,
            StringComparison.Ordinal);
        Assert.Null(pipeline.LastRequest);
    }

    [Fact]
    public async Task Ingest_PropagatesChunkingStrategy_AndReindex()
    {
        using var scratch = new ScriptScratch();
        await File.WriteAllTextAsync(Path.Combine(scratch.Root, "faq.md"), "faq", TestContext.Current.CancellationToken);
        var pipeline = new FakeIngestionPipeline();
        using var console = new TestConsole();

        var exit = await RagCommand.ExecuteIngestAsync(console, new RagIngestCommandOptions
        {
            Collection = "docs",
            Sources = ["faq.md"],
            Chunking = "sentence",
            Reindex = true,
            WorkingDirectoryOverride = scratch.Root,
            ConfigureTestServices = (_, s) => s.AddSingleton<IIngestionPipeline>(pipeline),
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal("sentence", pipeline.LastRequest?.ChunkingStrategy);
        Assert.True(pipeline.LastRequest!.Reindex);
        var source = Assert.Single(pipeline.LastRequest.Sources);
        Assert.Equal("/workspace/faq.md", source.Location);
    }

    [Fact]
    public async Task Ingest_WhenNoSourceMatches_ReturnsScriptError_AndReportsOnStderr()
    {
        using var scratch = new ScriptScratch();
        var pipeline = new FakeIngestionPipeline();
        using var console = new TestConsole();

        var exit = await RagCommand.ExecuteIngestAsync(console, new RagIngestCommandOptions
        {
            Collection = "docs",
            Sources = ["./docs/**/*.pdf"],
            WorkingDirectoryOverride = scratch.Root,
            ConfigureTestServices = (_, s) => s.AddSingleton<IIngestionPipeline>(pipeline),
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Null(pipeline.LastRequest);
        Assert.Contains("no source matched", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ingest_SurfacesPipelineFailures_AsScriptErrors()
    {
        // Embedding-profile drift is the flagship hard failure: the pipeline throws
        // an InvalidOperationException with an actionable message; the CLI must
        // relay it and exit 1 (never a stack trace, never a silent reindex).
        using var scratch = new ScriptScratch();
        await File.WriteAllTextAsync(Path.Combine(scratch.Root, "a.md"), "alpha", TestContext.Current.CancellationToken);
        var pipeline = new FakeIngestionPipeline
        {
            ThrowOnIngest = new InvalidOperationException(
                "embedding profile drift: re-run with --reindex to rebuild the collection"),
        };
        using var console = new TestConsole();

        var exit = await RagCommand.ExecuteIngestAsync(console, new RagIngestCommandOptions
        {
            Collection = "docs",
            Sources = ["a.md"],
            WorkingDirectoryOverride = scratch.Root,
            ConfigureTestServices = (_, s) => s.AddSingleton<IIngestionPipeline>(pipeline),
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("embedding profile drift", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("--reindex", console.Stderr, StringComparison.Ordinal);
    }

    // --- search ---

    [Fact]
    public async Task Search_PrintsTheAnswer_Citations_AndScores()
    {
        using var scratch = new ScriptScratch();
        var pipeline = new FakeRagPipeline
        {
            Answer = new RagAnswer
            {
                Text = "Refunds are allowed within 30 days [1].",
                Citations =
                [
                    new Citation
                    {
                        Marker = 1,
                        ChunkId = "chunk-1",
                        SourceId = "faq.md",
                        Snippet = "Customers may request a full refund within 30 days.",
                        Score = 0.91,
                    },
                ],
            },
        };
        using var console = new TestConsole();

        var exit = await RagCommand.ExecuteSearchAsync(console, new RagSearchCommandOptions
        {
            Question = "refund policy?",
            Collection = "docs",
            TopN = 7,
            WorkingDirectoryOverride = scratch.Root,
            ConfigureTestServices = (_, s) => s.AddSingleton<IRagPipeline>(pipeline),
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal("refund policy?", pipeline.LastQuery?.Text);
        Assert.Equal("docs", pipeline.LastQuery?.Collection);
        Assert.Equal(7, pipeline.LastQuery?.TopN);
        Assert.Contains("Refunds are allowed within 30 days [1].", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("Sources:", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("- [1] faq.md (score: 0.91):", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_WithABlankQuestion_ReturnsScriptError()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await RagCommand.ExecuteSearchAsync(console, new RagSearchCommandOptions
        {
            Question = "   ",
            Collection = "docs",
            WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("question is required", console.Stderr, StringComparison.Ordinal);
    }

    // --- dispatch & helpers ---

    [Fact]
    public async Task Dispatch_WithAnUnknownVerb_ReturnsScriptError()
    {
        using var console = new TestConsole();

        var exit = await RagCommand.DispatchAsync(console, ["frobnicate"]);

        Assert.Equal(Program.ExitScriptError, exit);
    }

    [Fact]
    public async Task Dispatch_Ingest_WithoutRequiredOptions_ReturnsScriptError()
    {
        using var console = new TestConsole();

        var exit = await RagCommand.DispatchAsync(console, ["ingest"]);

        Assert.Equal(Program.ExitScriptError, exit);
    }

    [Theory]
    [InlineData("./docs/**/*.md", "/workspace/docs/**/*.md")]
    [InlineData("docs/a.md", "/workspace/docs/a.md")]
    [InlineData("/workspace/docs/a.md", "/workspace/docs/a.md")]
    public void ToVirtualSource_MapsRelativeAndVirtualForms(string raw, string expected)
    {
        var mounts = new List<Orkeon.Domain.FileSystem.MountInfo>
        {
            new("/workspace", Orkeon.Domain.FileSystem.FileAccessRights.Read, []),
        };

        Assert.Equal(expected, RagCommand.ToVirtualSource(raw, "/tmp/corpus", mounts));
    }

    [Theory]
    [InlineData("https://exemple.test/page?id=1")]
    [InlineData("http://exemple.test/docs/*.html")]
    public void ToVirtualSource_HandsAnHttpAddressOverAsWritten(string address)
    {
        // GAP-19, decision 5: an address became "/workspace/https://…", a path no loader reads.
        var mounts = new List<Orkeon.Domain.FileSystem.MountInfo>
        {
            new("/workspace", Orkeon.Domain.FileSystem.FileAccessRights.Read, []),
        };

        Assert.Equal(address, RagCommand.ToVirtualSource(address, "/tmp/corpus", mounts));
    }

    [Fact]
    public void ToVirtualSource_RebasesAbsolutePathsUnderTheCwd_AndRejectsForeignOnes()
    {
        var mounts = new List<Orkeon.Domain.FileSystem.MountInfo>
        {
            new("/workspace", Orkeon.Domain.FileSystem.FileAccessRights.Read, []),
        };

        Assert.Equal(
            "/workspace/docs/a.md",
            RagCommand.ToVirtualSource("/tmp/corpus/docs/a.md", "/tmp/corpus", mounts));
        Assert.Throws<ArgumentException>(
            () => RagCommand.ToVirtualSource("/etc/passwd", "/tmp/corpus", mounts));
    }

    [Fact]
    public void ClaimsVirtualRoot_MatchesExactVirtualSegmentsOnly()
    {
        Assert.True(RagCommand.ClaimsVirtualRoot("/x:/output:rw", "/output"));
        Assert.True(RagCommand.ClaimsVirtualRoot("/x:/output/:rw", "/output"));
        Assert.False(RagCommand.ClaimsVirtualRoot("/x:/output-archive:rw", "/output"));

        // The question is asked of the grammar, not of a substring: a physical path may
        // legally carry ':' when quoted, and reading the spec by hand saw an /output claim
        // that is not there — suppressing the auto-mount the command needs.
        Assert.False(RagCommand.ClaimsVirtualRoot(@"""/mnt/x:/output:y"":/corpus:ro", "/output"));

        // A spec the grammar cannot read claims nothing; the parser reports it at host build.
        Assert.False(RagCommand.ClaimsVirtualRoot("/x:/output", "/output"));
        Assert.False(RagCommand.ClaimsVirtualRoot("", "/output"));
    }
}
