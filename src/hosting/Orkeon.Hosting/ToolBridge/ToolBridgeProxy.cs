using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ProtocolToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// A tool of the caller that runs on the bridge (#445): it wraps the caller's own instance, whose name, description,
/// schema and access classes the caller's pipeline keeps reading, and sends only the call. A bridged tool never runs
/// locally: a bridge that is lost fails the call with its reason, and the next call tries again.
/// <para>
/// Cancelling the call aborts its request and asks the bridge to cancel it there.
/// </para>
/// <para>
/// A call made in an agent run carries the run's id: the bridge runs it in its own scope of that run, which a resource
/// registered in the caller's scope ends when the run ends (<see cref="ToolBridgeRunLease"/>). A tool that uses the file
/// system, and does not act on the workspace, gets the caller's mounts presented on the bridge: the caller's files its
/// string arguments name are sent with the call, and the files it wrote there come back and are written here, through
/// this caller's file system and under the checks of its run.
/// </para>
/// </summary>
internal sealed class ToolBridgeProxy : IBaseTool, IToolWrapper
{
    /// <summary>The metadata key a failure of the bridge itself sets, so a reader tells it from the tool's own failure.</summary>
    public const string BridgeFailureKey = "bridge_failure";

    // How long the cancellation of an abandoned call may take.
    private static readonly TimeSpan CancelBound = TimeSpan.FromSeconds(5);

    // The longest string argument taken for a path.
    private const int MaxPathLength = 1024;

    private readonly ToolBridgeClient _client;
    private readonly IFileSystemService _fileSystem;
    private readonly long _maxFileBytes;
    private readonly bool _carriesFiles;

    /// <summary>Wraps <paramref name="inner"/>, whose calls go to <paramref name="client"/>.</summary>
    /// <param name="inner">The caller's own instance of the tool.</param>
    /// <param name="client">The bridge.</param>
    /// <param name="fileSystem">The caller's file system, through which the files of a call cross.</param>
    /// <param name="maxFileBytes">The largest total of the files of one call, each way.</param>
    public ToolBridgeProxy(IBaseTool inner, ToolBridgeClient client, IFileSystemService fileSystem,
        long maxFileBytes = ToolBridgeServerOptions.DefaultMaxFileBytes)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(fileSystem);
        Inner = inner;
        _client = client;
        _fileSystem = fileSystem;
        _maxFileBytes = maxFileBytes;
        _carriesFiles = inner.UsesFileSystem && !ToolContracts.ActsOnWorkspace(inner);
    }

    /// <inheritdoc />
    public IBaseTool Inner { get; }

    /// <inheritdoc />
    public string Name => Inner.Name;

    /// <inheritdoc />
    public string Description => Inner.Description;

    /// <inheritdoc />
    public ToolSchema Schema => Inner.Schema;

    /// <inheritdoc />
    public ToolAccess Access => Inner.Access;

    /// <inheritdoc />
    public ToolAccess AccessFor(IReadOnlyDictionary<string, object?> arguments) => Inner.AccessFor(arguments);

    /// <inheritdoc />
    public bool UsesFileSystem => Inner.UsesFileSystem;

    /// <inheritdoc />
    public async Task<ProtocolToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = request.Parameters ?? new Dictionary<string, object?>();
        var call = new BridgeCallRequest
        {
            CallId = Guid.NewGuid().ToString("N"),
            Tool = Name,
            Arguments = JsonSerializer.SerializeToElement(arguments, ToolBridgeProtocol.JsonOptions),
            Context = request.Context,
            CallerId = _client.CallerId,
            RunId = JoinRun(),
        };
        try
        {
            if (_carriesFiles)
            {
                if (await FilesOfAsync(arguments, cancellationToken).ConfigureAwait(false) is not { } files)
                    return Failure($"{Name} runs on the tool bridge, and the files its arguments name exceed {ToolBridgeClientOptions.SectionName}:MaxFileBytes ({ToolBridgeProtocol.Describe(_maxFileBytes)}).");
                call = call with
                {
                    Mounts = [.. _fileSystem.GetAvailableMounts()
                        .Where(m => m.Visibility == MountVisibility.AgentFacing)
                        .Select(m => new BridgeMount { Path = m.VirtualPath, Writable = m.DefaultRights.HasFlag(FileAccessRights.Write) })],
                    WorkingDirectory = _fileSystem.WorkingDirectory,
                    WriteDirectory = _fileSystem.WriteDirectory,
                    Files = files.Count == 0 ? null : files,
                };
            }

            var answer = await _client.CallAsync(call, cancellationToken).ConfigureAwait(false);
            if (await WriteBackAsync(answer.Files, cancellationToken).ConfigureAwait(false) is { } refusal)
                return Failure($"{Name} ran on the tool bridge, but {refusal}");
            return FromWire(answer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var bound = new CancellationTokenSource(CancelBound);
            await _client.CancelAsync(call.CallId, bound.Token).ConfigureAwait(false);
            return new ProtocolToolCallResponse(false, null, "Operation cancelled", new Dictionary<string, object?> { ["cancelled"] = true });
        }
        catch (ToolBridgeException ex)
        {
            return new ProtocolToolCallResponse(false, null, $"{Name} runs on the tool bridge, and {ex.Message}",
                new Dictionary<string, object?> { [BridgeFailureKey] = ex.Failure.ToString() });
        }
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> arguments;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(input) ? "{}" : input);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return ToolResult.CreateError($"{Name} takes a JSON object of arguments.");
            arguments = ToolBridgeProtocol.ToPlainObject(document.RootElement);
        }
        catch (JsonException)
        {
            return ToolResult.CreateError($"{Name} takes a JSON object of arguments.");
        }

        var response = await CallAsync(new ProtocolToolCallRequest(Name, arguments), cancellationToken).ConfigureAwait(false);
        return response.Success
            ? ToolResult.CreateSuccess(response.Result switch
            {
                null => "",
                string text => text,
                var other => JsonSerializer.Serialize(other, ToolBridgeProtocol.JsonOptions),
            })
            : ToolResult.CreateError(response.Error ?? $"{Name} failed on the tool bridge.");
    }

    /// <inheritdoc />
    public bool ValidateInput(string input) => Inner.ValidateInput(input);

    private static ProtocolToolCallResponse Failure(string reason) => new(false, null, reason);

    /// <summary>
    /// The id of the caller's run, whose scope on the bridge a lease in the run's scope ends; null outside any run, or
    /// in a run that has ended.
    /// </summary>
    private string? JoinRun()
    {
        if (AgentRunScope.Current is not { IsEnded: false } run)
            return null;
        try
        {
            run.GetOrAdd(_client, () => new ToolBridgeRunLease(_client, run.RunId));
            return run.RunId;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The caller's files the arguments name: each string argument, at any depth, that is a virtual path under a mount and an existing file this run may
    /// read. Null when they exceed the size limit.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "A string that is no path of the caller fails in many ways (format, rights, IO); each means: not a file to send.")]
    private async System.Threading.Tasks.Task<ImmutableList<BridgeFile>?> FilesOfAsync(
        IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var files = ImmutableList.CreateBuilder<BridgeFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        using var _ = AgentRunScope.EnterToolCall();
        foreach (var candidate in Strings(arguments.Values))
        {
            if (candidate.Length > MaxPathLength || candidate.Contains("://", StringComparison.Ordinal) || candidate.Any(char.IsControl)
                || !UnderAMount(candidate))
                continue;
            byte[]? content;
            string path;
            try
            {
                var check = _fileSystem.ResolveAndValidate(candidate, FileAccessRights.Read);
                if (!check.IsAllowed || check.ResolvedPath is null)
                    continue;
                path = _fileSystem.ToVirtualPath(check.ResolvedPath) ?? candidate;
                if (!seen.Add(path) || await _fileSystem.GetEntryKindAsync(path, cancellationToken).ConfigureAwait(false) != VirtualEntryKind.File)
                    continue;
                content = await _fileSystem.TryReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                continue;
            }

            if (content is null)
                continue;
            total += content.Length;
            if (total > _maxFileBytes)
                return null;
            files.Add(new BridgeFile { Path = path, Content = content });
        }

        return files.ToImmutable();
    }

    // Whether candidate is a virtual path under a mount of the caller: a word such as an action name, or a relative
    // path, is not asked of the file system, whose refusal would log a warning for each.
    private bool UnderAMount(string candidate) =>
        candidate.StartsWith('/') && _fileSystem.GetAvailableMounts().Any(mount =>
            string.Equals(candidate, mount.VirtualPath, StringComparison.Ordinal)
            || candidate.StartsWith(mount.VirtualPath.TrimEnd('/') + "/", StringComparison.Ordinal));

    private static IEnumerable<string> Strings(IEnumerable<object?> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case string text:
                    yield return text;
                    break;
                case IReadOnlyDictionary<string, object?> map:
                    foreach (var inner in Strings(map.Values))
                        yield return inner;
                    break;
                case System.Collections.IEnumerable list:
                    foreach (var inner in Strings(list.Cast<object?>()))
                        yield return inner;
                    break;
            }
        }
    }

    /// <summary>
    /// Writes the files the tool wrote on the bridge through the caller's file system, under the checks of its run; the
    /// reason of the first refusal, or null when every file was written.
    /// </summary>
    private async System.Threading.Tasks.Task<string?> WriteBackAsync(ImmutableList<BridgeFile>? files, CancellationToken cancellationToken)
    {
        if (files is not { Count: > 0 })
            return null;
        if (!_carriesFiles)
            return "it sent back files this Orkeon did not ask for.";
        if (files.Sum(f => (long)f.Content.Length) > _maxFileBytes)
            return $"the files it wrote exceed {ToolBridgeClientOptions.SectionName}:MaxFileBytes ({ToolBridgeProtocol.Describe(_maxFileBytes)}).";
        using var _ = AgentRunScope.EnterToolCall();
        foreach (var file in files)
        {
            var check = _fileSystem.ResolveAndValidate(file.Path, FileAccessRights.Write);
            if (!check.IsAllowed)
                return $"the file '{file.Path}' it wrote is refused here: {check.DenialReason ?? "the path is refused by the file system policy"}.";
            try
            {
                await _fileSystem.WriteAllBytesAsync(file.Path, file.Content.ToArray(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
            {
                return $"the file '{file.Path}' it wrote could not be written here: {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>
    /// The wire's response as the caller's pipeline reads it: a JSON string becomes a string, an object a dictionary,
    /// an array a list, so the pipeline renders them as the tool's own result.
    /// </summary>
    /// <param name="response">The bridge's answer.</param>
    /// <returns>The tool's response.</returns>
    internal static ProtocolToolCallResponse FromWire(BridgeCallResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var metadata = response.Metadata?.ToDictionary(p => p.Key, p => ToolBridgeProtocol.ToPlain(p.Value), StringComparer.Ordinal);
        var result = response.Result is { } element ? ToolBridgeProtocol.ToPlain(element) : null;
        return new ProtocolToolCallResponse(response.Success, result, response.Error, metadata)
        {
            AlreadyBounded = response.AlreadyBounded,
        };
    }
}

/// <summary>
/// The caller's run as the bridge knows it (#445): registered in the caller's <see cref="AgentRunScope"/> at the run's
/// first bridged call, it keeps the heartbeat going while the run lives and, when the run ends, ends the bridge's scope
/// of it (its browser with it).
/// </summary>
internal sealed class ToolBridgeRunLease : IAsyncDisposable
{
    private readonly ToolBridgeClient _client;
    private readonly string _runId;

    /// <summary>Records the run on the client; cheap, as <see cref="AgentRunScope.GetOrAdd{T}"/> requires.</summary>
    /// <param name="client">The bridge.</param>
    /// <param name="runId">The caller's run.</param>
    public ToolBridgeRunLease(ToolBridgeClient client, string runId)
    {
        _client = client;
        _runId = runId;
        client.BeginRun(runId);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _client.EndRunAsync(_runId).ConfigureAwait(false);
}
