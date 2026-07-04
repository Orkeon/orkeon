using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Cli.Scripting.Tests.Doubles;

/// <summary>
/// Counting double for a transient, disposable <see cref="IBaseTool"/> registration
/// (R10.4 / ANT-005). Each construction increments the shared
/// <see cref="ToolInstantiationCounter"/>, so a test can prove how many tool sets the
/// container actually materialized across repeated <c>ctx.services.get("tools")</c> calls.
/// </summary>
public sealed class FakeCountingDisposableTool : IBaseTool, IDisposable
{
    public FakeCountingDisposableTool(ToolInstantiationCounter counter)
    {
        ArgumentNullException.ThrowIfNull(counter);
        counter.Increment();
    }

    public string Name => "fake_counting_tool";

    public string Description => "Counts instantiations (hand-written test double).";

    public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

    public Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new ToolCallResponse(true, null, null));

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(ToolResult.CreateSuccess(string.Empty));

    public bool ValidateInput(string input) => true;

    public void Dispose()
    {
        // Nothing to release — IDisposable is implemented so the DI container tracks this
        // transient exactly like production ToolBase tools (the retention at the heart of ANT-005).
    }
}

/// <summary>Thread-safe instantiation tally shared by <see cref="FakeCountingDisposableTool"/> instances.</summary>
public sealed class ToolInstantiationCounter
{
    private int _count;

    /// <summary>Number of <see cref="FakeCountingDisposableTool"/> constructions so far.</summary>
    public int Count => Volatile.Read(ref _count);

    internal void Increment() => Interlocked.Increment(ref _count);
}
