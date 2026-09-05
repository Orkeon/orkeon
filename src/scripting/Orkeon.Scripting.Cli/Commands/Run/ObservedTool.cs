using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Commands.Run;

/// <summary>
/// Wraps a tool so every call it receives becomes two events (BUS-03).
/// <para>
/// Instrumenting here rather than inside the agent loops is what makes this affordable: there
/// are three loops plus the scripting facade, and a tool is the one thing all of them go
/// through. One decorator, applied where tools enter the process, covers every path — including
/// paths written after it.
/// </para>
/// <para>
/// It implements <see cref="ITool"/> and not merely <see cref="IBaseTool"/> **on purpose**:
/// <c>CrewFactory</c> assigns tools with <c>tool is ITool</c> and <c>AgentMapper</c> with
/// <c>OfType&lt;ITool&gt;()</c>, so a decorator that only implemented the base interface would
/// be silently filtered out and the agents would run with no tools at all.
/// </para>
/// <para>
/// Two tool names get a richer event than the rest, because what they mean is not "a tool ran":
/// <c>delegate_work</c> is one agent handing work to another, and <c>spawn_agent</c> is a team
/// growing at runtime. Those are the two things that make an autonomous run hard to follow, so
/// they are named rather than buried among tool calls.
/// </para>
/// </summary>
internal sealed class ObservedTool : ITool, IDisposable
{
    /// <summary>
    /// The tool whose call means one agent delegated to another — the wire name of
    /// <c>DelegateWorkTool</c>'s <c>[ToolContract]</c>, which is what <c>Name</c> returns.
    /// The first version of this constant said <c>delegate_work</c>, and the branch below was
    /// unreachable: every delegation was reported as an ordinary tool call.
    /// </summary>
    public const string DelegateToolName = "delegate_work_to_coworker";

    /// <summary>The tool whose call means the team grew at runtime (<c>SpawnAgentTool</c>'s contract name).</summary>
    public const string SpawnToolName = "spawn_agent";

    private readonly IBaseTool _inner;
    private readonly OrkeonEventWriter _events;

    /// <summary>Wraps <paramref name="inner"/>, reporting onto <paramref name="events"/>.</summary>
    public ObservedTool(IBaseTool inner, OrkeonEventWriter events)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public string Description => _inner.Description;

    /// <inheritdoc />
    public global::Orkeon.Domain.Tools.Protocol.ToolSchema Schema => _inner.Schema;

    /// <inheritdoc />
    public ToolAccess Access => _inner.Access;

    /// <inheritdoc />
    [SuppressMessage("Minor Code Smell", "S2737:\"catch\" clauses should do more than rethrow", Justification = "The catch clause exists only to carry its exception filter: ReportFailure emits the tool-returned event during the first pass and always returns false, so the body is never entered and the exception keeps travelling untouched. Removing the rethrow-only clause would remove the reporting with it.")]
    public async Task<global::Orkeon.Domain.Tools.Protocol.ToolCallResponse> CallAsync(
        global::Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        var correlationId = Announce(request?.Parameters);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _inner.CallAsync(request!, cancellationToken).ConfigureAwait(false);
            Report(correlationId, response?.Success ?? false, stopwatch);
            return response!;
        }
        catch (Exception) when (ReportFailure(correlationId, stopwatch))
        {
            throw;   // the filter never matches; it only lets the event out before unwinding
        }
    }

    /// <inheritdoc />
    public bool ValidateInput(string input) => _inner.ValidateInput(input);

    /// <inheritdoc />
    [SuppressMessage("Minor Code Smell", "S2737:\"catch\" clauses should do more than rethrow", Justification = "The catch clause exists only to carry its exception filter: ReportFailure emits the tool-returned event during the first pass and always returns false, so the body is never entered and the exception keeps travelling untouched. Removing the rethrow-only clause would remove the reporting with it.")]
    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        var correlationId = Announce(arguments: null);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _inner.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
            Report(correlationId, result?.Success ?? false, stopwatch);
            return result!;
        }
        catch (Exception) when (ReportFailure(correlationId, stopwatch))
        {
            throw;
        }
    }

    private string Announce(IReadOnlyDictionary<string, object?>? arguments)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var scope = new OrkeonEventScope { CorrelationId = correlationId };

        switch (Name)
        {
            case DelegateToolName:
                // Field names come from DelegateWorkRequest's wire shape (snake_case, with the
                // camel spelling tolerated) — not from names one would like the tool to have.
                _events.Emit(RunEventKinds.DelegationStarted, scope, new
                {
                    toRole = Argument(arguments, "coworker_role") ?? Argument(arguments, "coworkerRole"),
                });
                break;

            case SpawnToolName:
                _events.Emit(RunEventKinds.AgentSpawned, scope, new
                {
                    role = Argument(arguments, "role"),
                    reason = Argument(arguments, "goal"),
                });
                break;

            default:
                _events.Emit(RunEventKinds.ToolCalled, scope, new
                {
                    toolName = Name,
                    argsSummary = Summarize(arguments),
                });
                break;
        }

        return correlationId;
    }

    private void Report(string correlationId, bool success, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        _events.Emit(
            RunEventKinds.ToolReturned,
            new OrkeonEventScope { CorrelationId = correlationId },
            new { toolName = Name, success, durationMs = stopwatch.ElapsedMilliseconds });
    }

    private bool ReportFailure(string correlationId, Stopwatch stopwatch)
    {
        // A tool that throws still returned, and a watcher that only sees the call would show a
        // step running forever. Reported from an exception filter so the event goes out before
        // the stack unwinds, and false so the exception keeps travelling untouched.
        Report(correlationId, success: false, stopwatch);
        return false;
    }

    private static string? Argument(IReadOnlyDictionary<string, object?>? arguments, string name) =>
        arguments is not null && arguments.TryGetValue(name, out var value) ? value?.ToString() : null;

    /// <summary>
    /// Disposal cascades to the wrapped tool: the container tracks what the decorating
    /// factory returned — this instance — so without the cascade the inner tool's resources
    /// (HttpToolBase owns an HttpClient) would never be released on observed runs, while
    /// unobserved runs release them fine.
    /// </summary>
    public void Dispose() => (_inner as IDisposable)?.Dispose();

    /// <summary>
    /// A short, printable digest of the arguments. Deliberately not the arguments themselves:
    /// a tool call can carry a whole file, and the stream is read by a UI, not an archive.
    /// </summary>
    private static string? Summarize(IReadOnlyDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return null;

        var summary = string.Join(", ", arguments.Keys.Take(6));
        return arguments.Count > 6 ? summary + ", …" : summary;
    }
}

/// <summary>
/// The <see cref="Orkeon.Application.Interfaces.Ports.IToolDecorator"/> face of
/// <see cref="ObservedTool"/> — what lets tools built outside DI (the per-agent delegation
/// pair above all) be observed like the registered ones.
/// </summary>
internal sealed class ObservedToolDecorator : Orkeon.Application.Interfaces.Ports.IToolDecorator
{
    private readonly OrkeonEventWriter _events;

    /// <summary>Reports every decorated tool onto <paramref name="events"/>.</summary>
    public ObservedToolDecorator(OrkeonEventWriter events) =>
        _events = events ?? throw new ArgumentNullException(nameof(events));

    /// <inheritdoc />
    public ITool Decorate(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return tool is ObservedTool ? tool : new ObservedTool(tool, _events);
    }
}
