using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// The contract <c>crew.d.ts</c> declares for <c>run()</c>:
/// <c>run&lt;TOut&gt;(opts?): Promise&lt;CrewResult&lt;TOut&gt;&gt;</c>.
///
/// <para>
/// A script therefore expects two things from <c>await crew.run()</c>: the crew has finished,
/// and what comes back is a <c>CrewResult</c> carrying <c>output</c> and <c>tasks</c>. Both are
/// asserted here because both were once false — <c>run()</c> handed a bare CLR
/// <see cref="System.Threading.Tasks.Task{TResult}"/> to Jint, and awaiting a non-thenable
/// returns it unchanged, so the script read <c>undefined</c> off a Task and walked on while the
/// body was still running. Every procedural example in the repository ends on this call.
/// </para>
/// </summary>
public sealed class CrewRunAwaitContractTests
{
    private static ScriptHost CreateHost(out string virtualRoot)
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        virtualRoot = "/scripts";
        var fs = new DiskBackedFileSystemService(fixturesDir, virtualRoot);
        return new ScriptHost(fs, new JsEngineFactory());
    }

    private static async Task<IDictionary<string, object?>> RunContractFixtureAsync()
    {
        var host = CreateHost(out var root);

        var raw = await host.RunAsync($"{root}/crew-run-contract.ork.ts", CancellationToken.None);

        var map = raw as IDictionary<string, object?>;
        Assert.NotNull(map);
        return map;
    }

    [Fact]
    public async Task Await_crew_run_returns_only_after_an_async_body_completed()
    {
        var result = await RunContractFixtureAsync();

        // "started" means the await handed back before the body's own await resumed.
        Assert.Equal("finished", result["stage"]);
    }

    [Fact]
    public async Task Await_crew_run_resolves_to_the_CrewResult_the_typings_declare()
    {
        var result = await RunContractFixtureAsync();

        Assert.Equal("ok", result["output"]);
        Assert.Equal(1d, Convert.ToDouble(result["taskCount"], System.Globalization.CultureInfo.InvariantCulture));
    }


    /// <summary>
    /// A Jint engine is neither thread-safe nor re-entrant, and an agent body that really
    /// suspends makes that matter: <c>crew.run()</c> is called from inside the engine, and the
    /// promise its body returns has to be settled by re-entering that same engine. Do it from
    /// another thread and the two meet — the symptoms are a body resuming into an environment
    /// where its own parameters are unbound, or a script whose result never settles.
    ///
    /// <para>
    /// Each iteration owns its host and engine, so nothing is shared by construction; what this
    /// creates is contention. It is the detector for that class of defect.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Async_bodies_survive_being_run_under_contention()
    {
        var runs = Enumerable.Range(0, 24)
            .Select(_ => Task.Run(RunContractFixtureAsync))
            .ToArray();

        var results = await Task.WhenAll(runs);

        Assert.All(results, r => Assert.Equal("finished", r["stage"]));
        Assert.All(results, r => Assert.Equal("ok", r["output"]));
    }
}
