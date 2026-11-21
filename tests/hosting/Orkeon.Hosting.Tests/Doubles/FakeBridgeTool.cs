using System.Collections.Concurrent;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// A tool on either side of the tool bridge (#445): an <c>action</c> parameter whose values carry their own access
/// class, a response it returns as given, the calls it received, and a mode that waits for the call's cancellation.
/// </summary>
public class FakeBridgeTool : IBaseTool
{
    /// <summary>Builds the tool under <paramref name="name"/>.</summary>
    public FakeBridgeTool(string name, params string[] actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Actions = actions.Length == 0 ? ["read", "write"] : actions;
        Schema = new ToolSchema(name, "a fake tool of the bridge", new Dictionary<string, ParameterSchema>
        {
            ["action"] = new("string", "what to do", Required: true, Enum: [.. Actions]),
            ["text"] = new("string", "a text", Required: false),
        });
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The values of <c>action</c>.</summary>
    public IReadOnlyList<string> Actions { get; }

    /// <inheritdoc />
    public string Description { get; init; } = "a fake tool of the bridge";

    /// <inheritdoc />
    public ToolSchema Schema { get; init; }

    /// <inheritdoc />
    public ToolAccess Access { get; init; } = ToolAccess.Execute;

    /// <summary>The class of the action <c>write</c>; <see cref="ToolAccess.Read"/> for every other one.</summary>
    public ToolAccess WriteAccess { get; init; } = ToolAccess.Edit;

    /// <summary>The response every call returns.</summary>
    public ToolCallResponse Response { get; set; } = new(true, "done", null);

    /// <summary>When set, a call waits for its cancellation and records it.</summary>
    public bool WaitsForCancellation { get; set; }

    /// <summary>The calls received, in order.</summary>
    public ConcurrentQueue<ToolCallRequest> Calls { get; } = new();

    /// <summary>Completes when a call that waits for its cancellation has started.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when a call that waited was cancelled.</summary>
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public ToolAccess AccessFor(IReadOnlyDictionary<string, object?> arguments) =>
        arguments.TryGetValue("action", out var action) && action?.ToString() == "write" ? WriteAccess
        : arguments.ContainsKey("action") ? ToolAccess.Read
        : Access;

    /// <inheritdoc />
    public async Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(request);
        if (!WaitsForCancellation)
            return Response;
        Started.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Cancelled.TrySetResult();
            throw;
        }

        return Response;
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolResult.CreateSuccess("done"));

    /// <inheritdoc />
    public bool ValidateInput(string input) => true;
}

/// <summary>A tool of the bridge whose contract says it acts on the workspace.</summary>
[ToolContract("workspace_tool", ActsOnWorkspace = true)]
public sealed class FakeWorkspaceBridgeTool : FakeBridgeTool
{
    /// <summary>Builds the tool, named <c>workspace_tool</c>.</summary>
    public FakeWorkspaceBridgeTool() : base("workspace_tool")
    {
    }
}
