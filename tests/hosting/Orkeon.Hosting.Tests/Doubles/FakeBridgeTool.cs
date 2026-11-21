using System.Collections.Concurrent;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.Doubles;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// A tool on either side of the tool bridge (#445): an <c>action</c> parameter whose values carry their own access
/// class, a response it returns as given, the calls it received, and a mode that waits for the call's cancellation.
/// Each call records the agent run it ran in and puts one resource in that run's scope, as the browser of
/// <c>firefox</c> does. Its contract declares no kind, so the lone <c>*</c> selects it and its subclasses.
/// </summary>
[ToolContract("fake_bridge_tool")]
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

    /// <summary>When set, the class of a call it gives a class to, before the class of <c>action</c>.</summary>
    public Func<IReadOnlyDictionary<string, object?>, ToolAccess?>? AccessOf { get; init; }

    /// <summary>The class of the action <c>write</c>; <see cref="ToolAccess.Read"/> for every other one.</summary>
    public ToolAccess WriteAccess { get; init; } = ToolAccess.Edit;

    /// <summary>The response every call returns.</summary>
    public ToolCallResponse Response { get; set; } = new(true, "done", null);

    /// <summary>When set, a call waits for its cancellation and records it.</summary>
    public bool WaitsForCancellation { get; set; }

    /// <summary>The calls received, in order.</summary>
    public ConcurrentQueue<ToolCallRequest> Calls { get; } = new();

    /// <summary>The agent run each call ran in, in order; null for a call outside any run.</summary>
    public ConcurrentQueue<AgentRunScope?> Runs { get; } = new();

    /// <summary>The resource each run got at its first call, in order: its disposal is the end of that run.</summary>
    public ConcurrentQueue<FakeAsyncDisposable> RunResources { get; } = new();

    /// <inheritdoc />
    public bool UsesFileSystem { get; init; }

    /// <summary>Completes when a call that waits for its cancellation has started.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when a call that waited was cancelled.</summary>
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public ToolAccess AccessFor(IReadOnlyDictionary<string, object?> arguments) =>
        AccessOf?.Invoke(arguments) is { } given ? given
        : arguments.TryGetValue("action", out var action) && action?.ToString() == "write" ? WriteAccess
        : arguments.ContainsKey("action") ? ToolAccess.Read
        : Access;

    /// <summary>When set, each call throws it once recorded.</summary>
    public Exception? Throws { get; set; }

    /// <inheritdoc />
    public virtual async Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(request);
        if (Throws is { } thrown)
            throw thrown;
        var run = AgentRunScope.Current;
        Runs.Enqueue(run);
        run?.GetOrAdd(this, () =>
        {
            var resource = new FakeAsyncDisposable { Name = run.RunId };
            RunResources.Enqueue(resource);
            return resource;
        });
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

/// <summary>
/// A tool of the bridge that uses the file system: <c>save_to_path</c> writes <see cref="Written"/> there, <c>read_from</c>
/// reads the text there (null for a file it did not find or may not reach), both through the file system it was given,
/// before the response of <see cref="FakeBridgeTool"/>.
/// </summary>
public sealed class FakeFileBridgeTool : FakeBridgeTool
{
    private readonly IFileSystemService _fileSystem;

    /// <summary>Builds the tool under <paramref name="name"/> over <paramref name="fileSystem"/>.</summary>
    public FakeFileBridgeTool(string name, IFileSystemService fileSystem) : base(name)
    {
        _fileSystem = fileSystem;
        UsesFileSystem = true;
        Schema = new ToolSchema(name, "a fake tool of the bridge that writes and reads files", new Dictionary<string, ParameterSchema>
        {
            ["action"] = new("string", "what to do", Required: true, Enum: [.. Actions]),
            ["save_to_path"] = new("string", "where to write", Required: false),
            ["output_dir"] = new("string", "a folder the call may write under", Required: false),
            ["text"] = new("string", "a text, never a path", Required: false),
            ["read_from"] = new("string", "what to read", Required: false),
            ["input_dir"] = new("string", "a folder whose files the tool lists", Required: false),
        });
    }

    /// <summary>The bytes <c>save_to_path</c> writes.</summary>
    public byte[] Written { get; set; } = [1, 2, 3];

    /// <summary>When set, a path the tool also writes <see cref="Written"/> to, whatever the arguments name.</summary>
    public string? AlsoWritesTo { get; set; }

    /// <summary>When set, <c>save_to_path</c> makes a symbolic link to this physical file in place of writing.</summary>
    public string? LinksTo { get; set; }

    /// <summary>The physical paths <c>save_to_path</c> resolved to, in order.</summary>
    public ConcurrentQueue<string> WrittenAt { get; } = new();

    /// <summary>The texts <c>read_from</c> read, in order; null for a file it did not find.</summary>
    public ConcurrentQueue<string?> Read { get; } = new();

    /// <summary>The files <c>input_dir</c> held on the bridge, by virtual path, in order.</summary>
    public ConcurrentQueue<string> Listed { get; } = new();

    /// <inheritdoc />
    public override async Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Parameters?.GetValueOrDefault("save_to_path") is string target)
        {
            var check = _fileSystem.ResolveAndValidate(target, FileAccessRights.Write);
            if (!check.IsAllowed)
                return new ToolCallResponse(false, null, check.DenialReason);
            WrittenAt.Enqueue(check.ResolvedPath!);
            if (LinksTo is { } link)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(check.ResolvedPath!)!);
                File.CreateSymbolicLink(check.ResolvedPath!, link);
            }
            else
            {
                await _fileSystem.WriteAllBytesAsync(target, Written, cancellationToken);
            }
        }

        if (AlsoWritesTo is { } also)
            await _fileSystem.WriteAllBytesAsync(also, Written, cancellationToken);

        if (request.Parameters?.GetValueOrDefault("input_dir") is string folder)
        {
            await foreach (var entry in _fileSystem.EnumerateFilesAsync(folder, null, cancellationToken))
            {
                if (entry.Kind == VirtualEntryKind.File)
                    Listed.Enqueue(entry.VirtualPath);
            }
        }

        if (request.Parameters?.GetValueOrDefault("read_from") is string source)
        {
            try
            {
                Read.Enqueue(await _fileSystem.TryReadAllTextAsync(source, cancellationToken));
            }
            catch (FileAccessDeniedException)
            {
                // A path outside the mounts the call runs over: nothing read.
                Read.Enqueue(null);
            }
        }
        return await base.CallAsync(request, cancellationToken);
    }
}
