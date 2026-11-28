using Jint;
using Orkeon.Tests.Shared.Timing;
using Jint.Runtime;
using Orkeon.Cli.Commands.Scripting.Dispatch;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

/// <summary>
/// #493: serialising <c>options.state</c> refuses a value JSON cannot hold, but an engine stop raised inside a
/// <c>toJSON</c> or a getter - the statement cap, the time cap, a cancellation - is the script being stopped and
/// escapes as it does everywhere else, never turned into the "must be JSON-serialisable" refusal.
/// </summary>
public sealed class LaunchStateTests
{
    private const string LoopingState = "({ state: { toJSON() { for (;;) {} } } })";

    [Fact]
    [Trait("BR", "#493")]
    public void ShouldLetTheStatementCapEscape_WhenAToJsonRunsPastIt()
    {
        using var engine = new Engine(o => o.MaxStatements(10_000).Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var options = engine.Evaluate(LoopingState);

        Assert.Throws<StatementsCountOverflowException>(() => LaunchState.Read(options, "commands.post"));
    }

    [Fact]
    [Trait("BR", "#493")]
    public void ShouldLetTheCancellationEscape_WhenTheEngineIsCancelledWhileSerialising()
    {
        using var cts = new CancellationTokenSource();
        using var engine = new Engine(o => o.CancellationToken(cts.Token).Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var options = engine.Evaluate(LoopingState);
        cts.Cancel();

        Assert.Throws<ExecutionCanceledException>(() => LaunchState.Read(options, "commands.post"));
    }

    [Fact]
    [Trait("BR", "#493")]
    public void ShouldStillRefuse_WhenAToJsonThrowsAScriptError()
    {
        using var engine = new Engine(o => o.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var options = engine.Evaluate("({ state: { toJSON() { throw new Error('no'); } } })");

        var refused = Assert.Throws<JavaScriptException>(() => LaunchState.Read(options, "commands.post"));
        Assert.StartsWith("commands.post: options.state must be a JSON-serialisable value", refused.Message, StringComparison.Ordinal);
    }
}
