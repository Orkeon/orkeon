using Orkeon.Domain.Tools;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Scripting.Tests.Toolchain;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.FileSystem;

namespace Orkeon.Scripting.Tests.Examples;

/// <summary>
/// Runs every procedural example of <c>examples/scripting</c> through <see cref="ScriptHost"/>
/// against a provider that really suspends — an acceptance criterion of SCR-25.
/// </summary>
/// <remarks>
/// <para>The catalogue passed against the echo provider while two of its shapes hung against
/// any HTTP provider: the echo completes synchronously, so every await settled on the same
/// thread and no wake-up was ever lost. <see cref="SlowLlmProvider"/> yields to the thread
/// pool before answering, like <c>EngineThreadingContractTests</c> does, which is what made the
/// hangs reproducible. <c>11-await-before-run</c> and <c>12-events-async-handlers</c> were added
/// to the catalogue for exactly those shapes; the older files pin that the fix cost nothing
/// they relied on.</para>
/// <para><c>08-rag</c> is not here: it has no crew at all and needs the RAG backend
/// (<c>rag.ingest</c> / <c>rag.query</c> over an embedding provider and a document store) — an
/// engine without one fails loudly on the first call by design. <c>crew-review-desk/</c> is the
/// declarative shape, whose bodies never run; the CI validates it separately.</para>
/// <para>The scripts are real files bundled by esbuild (<c>RunFromFileAsync</c> with the physical
/// path), so a relative import would resolve; a 20 s guard turns a hang into a failure.</para>
/// </remarks>
public sealed class ProceduralExamplesUnderSuspendingProviderTests
{
    private static readonly TimeSpan SettleGuard = TimeSpan.FromSeconds(20);

    /// <summary>Time given to a cancelled drain to surface its cancellation before the wait itself gives up.</summary>
    private static readonly TimeSpan ReleaseSlack = TimeSpan.FromSeconds(5);

    /// <summary>The one file of the catalogue this class cannot run, and why — see the class remarks.</summary>
    private const string RagExample = "08-rag.ork.ts";


    private static readonly string[] Files =
    [
        "01-hello-world.ork.ts",
        "02-procedural.ork.ts",
        "03-mixed-llm.ork.ts",
        "04-dynamic-spawn.ork.ts",
        "05-state-with-and-lock.ork.ts",
        "06-custom-tool-and-hooks.ork.ts",
        "07-fsm-and-graph.ork.ts",
        "09-tools-and-act.ork.ts",
        "10-inputs-and-memory.ork.ts",
        "11-await-before-run.ork.ts",
        "12-events-async-handlers.ork.ts",
    ];

    public static TheoryData<string> ProceduralExamples
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var file in Files) data.Add(file);
            return data;
        }
    }

    private static EsbuildTranspiler RequireTranspiler()
    {
        var t = EsbuildTestEnvironment.TryCreate();
        Assert.SkipWhen(t is null, "esbuild binary not available in this test environment.");
        return t!;
    }

    [Theory]
    [MemberData(nameof(ProceduralExamples))]
    public async Task Example_settles_against_a_provider_that_suspends(string fileName)
    {
        var (result, source) = await RunExampleAsync(fileName);

        // The run settled without throwing, which is the whole claim. The files that assign
        // globalThis.result hand a plain object back (the CLI prints it); the others hand back
        // nothing, and the CLI prints `{ "result": null }` for them.
        if (source.Contains("globalThis.result =", StringComparison.Ordinal))
            Assert.IsType<IDictionary<string, object?>>(result, exactMatch: false);
        else
            Assert.Null(result);
    }

    /// <summary>
    /// SCR-25's scenario 1: the crew is started from the continuation of the file read, and both
    /// bodies still reach the provider (the echo would answer with the prompt itself; the slow
    /// provider prefixes it).
    /// </summary>
    [Fact]
    public async Task Await_before_run_reads_the_brief_then_runs_both_agents()
    {
        var (result, _) = await RunExampleAsync("11-await-before-run.ork.ts");

        var map = Assert.IsType<IDictionary<string, object?>>(result, exactMatch: false);
        Assert.Equal(3d, map["openItems"]);
        Assert.Equal(new object[] { "triager", "writer" }, Assert.IsType<object[]>(map["agents"]));
        Assert.StartsWith("R:", Assert.IsType<string>(map["output"]), StringComparison.Ordinal);
    }

    /// <summary>
    /// SCR-25's scenarios 6 and 2: each reporter publishes after an await, the digest handler
    /// awaits the provider for every event and the publish waits for it (two lines, in publication
    /// order, each carrying two answers), then a second crew runs from the first one's
    /// continuation.
    /// </summary>
    [Fact]
    public async Task Async_topic_handlers_run_to_completion_then_the_second_crew_runs()
    {
        var (result, _) = await RunExampleAsync("12-events-async-handlers.ork.ts");

        var map = Assert.IsType<IDictionary<string, object?>>(result, exactMatch: false);
        var digest = Assert.IsType<object[]>(map["digest"]).Cast<string>().ToArray();
        Assert.Equal(2, digest.Length);
        Assert.StartsWith("[north] R:Turn this into a digest line: R:", digest[0], StringComparison.Ordinal);
        Assert.StartsWith("[south] R:Turn this into a digest line: R:", digest[1], StringComparison.Ordinal);

        // The digest lines above only prove that both handlers ran before the script ended. The
        // editor's prompt is what pins that each publish waited for its handler: the second crew
        // built it from the digest as it stood when the first run settled, and a delivery loop that
        // stopped awaiting handlers would have left the south line (3 ms away) out of it.
        Assert.Equal(
            "R:Write a two-line summary of:\n" + digest[0] + "\n" + digest[1],
            Assert.IsType<string>(map["summary"]));
    }

    /// <summary>
    /// Runs one example the way <c>orkeon run</c> does — its directory mounted at <c>/script</c>,
    /// the physical path handed to esbuild — with the slow provider and the one built-in tool the
    /// catalogue uses (<c>file_read</c>, for 09 and 11). Returns what the script handed back and
    /// its source.
    /// </summary>
    private static async Task<(object? Result, string Source)> RunExampleAsync(string fileName)
    {
        using var transpiler = RequireTranspiler();

        var ct = TestContext.Current.CancellationToken;
        var examplesDir = ExamplesDirectory();
        var physicalPath = Path.Combine(examplesDir, fileName);
        Assert.True(File.Exists(physicalPath), $"Missing example '{physicalPath}'.");

        var fs = new DiskBackedFileSystemService(examplesDir, "/script");
        using var fileRead = new FileReadTool(fs, new StubPathValidator().AllowAll());
        var factory = new JsEngineFactory(
            builtInTools: new IBaseTool[] { fileRead },
            llmProvider: new SlowLlmProvider());
        var host = new ScriptHost(fs, transpiler, factory);

        // The guard token reaches the root pump: a script that hangs on an idle wait of the drain
        // releases its pool thread and the engine gate when the guard fires, instead of running on
        // orphaned until the process exits. The WaitAsync keeps the failure deterministic for a drain
        // that never reaches an idle wait (a regression to the nested pump of before SCR-25).
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
        guard.CancelAfter(SettleGuard);
        object? result;
        try
        {
            result = await host.RunFromFileAsync(physicalPath, $"/script/{fileName}", guard.Token)
                .WaitAsync(SettleGuard + ReleaseSlack, ct);
        }
        catch (Exception ex) when (guard.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            Assert.Fail($"HANG: {fileName} did not settle within {SettleGuard.TotalSeconds:0} s against a provider that suspends ({ex.GetType().Name}).");
            throw;
        }

        return (result, await File.ReadAllTextAsync(physicalPath, ct));
    }

    /// <summary>
    /// A new numbered example must be added to <see cref="Files"/>: this class is what guarantees
    /// that the catalogue runs under a suspending provider, and a theory that silently missed a
    /// file would leave it uncovered.
    /// </summary>
    [Fact]
    public void Every_numbered_example_is_listed()
    {
        var onDisk = Directory.EnumerateFiles(ExamplesDirectory(), "*.ork.ts")
            .Select(Path.GetFileName)
            .Where(f => f != RagExample)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        var listed = Files.OrderBy(f => f, StringComparer.Ordinal).ToArray();

        Assert.Equal(onDisk, listed);
    }

    private static string ExamplesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "examples", "scripting");
    }
}
