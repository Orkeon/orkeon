using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
/// this caller's file system and under the checks of its run. A file comes back only at a path, or under a directory,
/// that an argument of a path-like parameter named (<see cref="ToolBridgePathArguments"/>), only for a call the gate
/// classed as a write (<see cref="ToolAccess.Edit"/> or <see cref="ToolAccess.Execute"/>), and only for a call that
/// succeeded: any other file of the answer is refused with a warning, and the call still returns its result. A path
/// that is the working directory, a mount's root, <c>/</c> or a folder above one of them grants nothing. A folder a
/// path-like argument names is sent with its files, bounded (<see cref="MaxDirectoryFiles"/>, the size limit); the
/// files that come back are written all or none. Any failure of the link is a failed call marked
/// <see cref="BridgeFailureKey"/>, never an exception and never a local run.
/// </para>
/// <para>
/// The bridge's word that a result is already bounded is never taken: the caller's pipeline applies its one truncation
/// to every bridged result.
/// </para>
/// </summary>
internal sealed partial class ToolBridgeProxy : IBaseTool, IToolWrapper
{
    /// <summary>The metadata key a failure of the bridge itself sets, so a reader tells it from the tool's own failure.</summary>
    public const string BridgeFailureKey = "bridge_failure";

    // How long the cancellation of an abandoned call may take.
    private static readonly TimeSpan CancelBound = TimeSpan.FromSeconds(5);

    // The longest string argument taken for a path.
    private const int MaxPathLength = 1024;

    /// <summary>The most files a bridged call sends from one directory an argument names.</summary>
    public const int MaxDirectoryFiles = 256;

    private readonly ToolBridgeClient _client;
    private readonly IFileSystemService _fileSystem;
    private readonly long _maxFileBytes;
    private readonly bool _carriesFiles;
    private readonly ILogger _operatorLogger;

    /// <summary>Wraps <paramref name="inner"/>, whose calls go to <paramref name="client"/>.</summary>
    /// <param name="inner">The caller's own instance of the tool.</param>
    /// <param name="client">The bridge.</param>
    /// <param name="fileSystem">The caller's file system, through which the files of a call cross.</param>
    /// <param name="maxFileBytes">The largest total of the files of one call, each way.</param>
    /// <param name="operatorLogger">The operator channel, for a file of the answer that is refused; none when null.</param>
    public ToolBridgeProxy(IBaseTool inner, ToolBridgeClient client, IFileSystemService fileSystem,
        long maxFileBytes = ToolBridgeServerOptions.DefaultMaxFileBytes, ILogger? operatorLogger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(fileSystem);
        Inner = inner;
        _client = client;
        _fileSystem = fileSystem;
        _maxFileBytes = maxFileBytes;
        _carriesFiles = inner.UsesFileSystem && !ToolContracts.ActsOnWorkspace(inner);
        _operatorLogger = operatorLogger ?? NullLogger.Instance;
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
    [SuppressMessage("Design", "CA1031",
        Justification = "A bridged call never throws to the caller's pipeline: any failure of the bridge, the host's shutdown included, is a failed call with its reason.")]
    public async Task<ProtocolToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = request.Parameters ?? new Dictionary<string, object?>();
        var callId = Guid.NewGuid().ToString("N");
        try
        {
            var call = new BridgeCallRequest
            {
                CallId = callId,
                Tool = Name,
                Arguments = JsonSerializer.SerializeToElement(arguments, ToolBridgeProtocol.JsonOptions),
                Context = request.Context,
                RunId = JoinRun(),
            };
            if (_carriesFiles)
            {
                var sent = await FilesOfAsync(arguments, cancellationToken).ConfigureAwait(false);
                if (sent.Problem is { } problem)
                    return Failure($"{Name} runs on the tool bridge, and {problem}");
                call = call with
                {
                    Mounts = [.. _fileSystem.GetAvailableMounts()
                        .Where(m => m.Visibility == MountVisibility.AgentFacing)
                        .Select(m => new BridgeMount { Path = m.VirtualPath, Writable = m.DefaultRights.HasFlag(FileAccessRights.Write) })],
                    WorkingDirectory = _fileSystem.WorkingDirectory,
                    WriteDirectory = _fileSystem.WriteDirectory,
                    Files = sent.Files.Count == 0 ? null : sent.Files,
                };
            }

            var answer = await _client.CallAsync(call, cancellationToken).ConfigureAwait(false);
            if (await WriteBackAsync(answer.Files, arguments, answer.Success, cancellationToken).ConfigureAwait(false) is { } refusal)
                return Failure($"{Name} ran on the tool bridge, but {refusal}");
            return FromWire(answer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var bound = new CancellationTokenSource(CancelBound);
            await _client.CancelAsync(callId, bound.Token).ConfigureAwait(false);
            return new ProtocolToolCallResponse(false, null, "Operation cancelled", new Dictionary<string, object?> { ["cancelled"] = true });
        }
        catch (ToolBridgeException ex)
        {
            return BridgeFailure(ex.Message, ex.Failure);
        }
        catch (ObjectDisposedException)
        {
            // The host is shutting down, or the link was taken down: the bridge is gone for this caller.
            return BridgeFailure($"the link to the tool bridge at {_client.Address} is closed (the host is shutting down).", ToolBridgeFailure.Unreachable);
        }
        catch (NotSupportedException ex)
        {
            return BridgeFailure($"its arguments cannot be sent to the tool bridge: {ex.Message}", ToolBridgeFailure.Refused);
        }
        catch (Exception ex)
        {
            return BridgeFailure($"the call to the tool bridge at {_client.Address} failed: {ex.Message}", ToolBridgeFailure.Unreachable);
        }
    }

    private ProtocolToolCallResponse BridgeFailure(string reason, ToolBridgeFailure failure) =>
        new(false, null, $"{Name} runs on the tool bridge, and {reason}",
            new Dictionary<string, object?> { [BridgeFailureKey] = failure.ToString() });

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
    /// The caller's files the arguments name: each string argument, at any depth, that is a virtual path under a mount
    /// and an existing file this run may read; and, for an argument of a path-like parameter
    /// (<see cref="ToolBridgePathArguments"/>) that names a directory, the files under it at any depth, links left out. A
    /// directory that is the working directory, a mount's root, <c>/</c> or a folder above one of them is not sent. The
    /// problem in place of the files when they exceed the size limit, or when one directory holds more than
    /// <see cref="MaxDirectoryFiles"/> files.
    /// </summary>
    private async System.Threading.Tasks.Task<(ImmutableList<BridgeFile> Files, string? Problem)> FilesOfAsync(
        IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var files = ImmutableList.CreateBuilder<BridgeFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        var tooLarge = $"the files its arguments name exceed {ToolBridgeClientOptions.SectionName}:MaxFileBytes ({ToolBridgeProtocol.Describe(_maxFileBytes)}).";
        var named = new HashSet<string>(ToolBridgePathArguments.Of(arguments, Inner.Schema), StringComparer.Ordinal);
        var roots = Roots();
        using var _ = AgentRunScope.EnterToolCall();
        foreach (var candidate in Strings(arguments.Values))
        {
            if (candidate.Length > MaxPathLength || candidate.Contains("://", StringComparison.Ordinal) || candidate.Any(char.IsControl)
                || !UnderAMount(candidate))
                continue;
            if (await ReadableAsync(candidate, cancellationToken).ConfigureAwait(false) is not { } entry)
                continue;
            if (entry.Kind == VirtualEntryKind.File)
            {
                if (!seen.Add(entry.Path) || await ReadAsync(entry.Path, cancellationToken).ConfigureAwait(false) is not { } content)
                    continue;
                total += content.Length;
                if (total > _maxFileBytes)
                    return ([], tooLarge);
                files.Add(new BridgeFile { Path = entry.Path, Content = content });
                continue;
            }

            if (entry.Kind != VirtualEntryKind.Directory || !named.Contains(candidate)
                || VirtualPaths.Normalize(entry.Path, "/") is not { } directory || roots.Any(root => VirtualPaths.IsAtOrUnder(root, directory)))
                continue;
            var count = 0;
            await foreach (var found in UnderAsync(directory, cancellationToken).ConfigureAwait(false))
            {
                if (++count > MaxDirectoryFiles)
                    return ([], $"the directory '{directory}' its arguments name holds more than {MaxDirectoryFiles} files, the most a bridged call sends from one directory.");
                if (!seen.Add(found) || await ReadableAsync(found, cancellationToken).ConfigureAwait(false) is not { Kind: VirtualEntryKind.File } file
                    || await ReadAsync(file.Path, cancellationToken).ConfigureAwait(false) is not { } content)
                    continue;
                total += content.Length;
                if (total > _maxFileBytes)
                    return ([], tooLarge);
                files.Add(new BridgeFile { Path = file.Path, Content = content });
            }
        }

        return (files.ToImmutable(), null);
    }

    /// <summary>The working directory, the mounts' roots and <c>/</c>, normalised: a folder at or above one names nothing.</summary>
    private List<string> Roots()
    {
        var working = _fileSystem.WorkingDirectory is { Length: > 0 } wd ? VirtualPaths.Normalize(wd, "/") ?? "/" : "/";
        return [.. _fileSystem.GetAvailableMounts()
            .Select(m => VirtualPaths.Normalize(m.VirtualPath, "/"))
            .OfType<string>()
            .Append(working)];
    }

    // The entry at candidate, by its virtual path, when this run may read it; null for a string that is no readable path.
    [SuppressMessage("Design", "CA1031",
        Justification = "A string that is no path of the caller fails in many ways (format, rights, IO); each means: not a file to send.")]
    private async System.Threading.Tasks.Task<(string Path, VirtualEntryKind Kind)?> ReadableAsync(string candidate, CancellationToken cancellationToken)
    {
        try
        {
            var check = _fileSystem.ResolveAndValidate(candidate, FileAccessRights.Read);
            if (!check.IsAllowed || check.ResolvedPath is null)
                return null;
            var path = _fileSystem.ToVirtualPath(check.ResolvedPath) ?? candidate;
            return (path, await _fileSystem.GetEntryKindAsync(path, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A file that cannot be read is not sent, as one that does not exist.")]
    private async System.Threading.Tasks.Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _fileSystem.TryReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The files under directory, at any depth, links left out; the listing stops where it can no longer be read.
    private async IAsyncEnumerable<string> UnderAsync(string directory, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var entries = _fileSystem.EnumerateFilesAsync(directory, new VirtualEnumerationOptions(Recursive: true, FollowSymlinks: false), cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                VirtualFileEntry entry;
                try
                {
                    if (!await entries.MoveNextAsync().ConfigureAwait(false))
                        yield break;
                    entry = entries.Current;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
                {
                    yield break;
                }

                if (entry.Kind == VirtualEntryKind.File && entry.LinkTarget is null)
                    yield return entry.VirtualPath;
            }
        }
        finally
        {
            await entries.DisposeAsync().ConfigureAwait(false);
        }
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
    /// Writes the files the tool wrote on the bridge through the caller's file system, under the checks of its run, all
    /// or none: every file asked for is checked (the size limit, then the file system's policy) before the first one is
    /// written, so a refusal leaves no file written. The reason of a refusal, which names the files already written when
    /// a write failed midway; null when every file asked for was written. A file at a path the arguments did not name,
    /// or returned for a call the gate classed as a read or for a call that failed, is left out with a warning.
    /// </summary>
    private async System.Threading.Tasks.Task<string?> WriteBackAsync(
        ImmutableList<BridgeFile>? files, IReadOnlyDictionary<string, object?> arguments, bool succeeded, CancellationToken cancellationToken)
    {
        if (files is not { Count: > 0 })
            return null;
        var asked = AskedFiles(files, arguments, succeeded);
        if (asked.Count == 0)
            return null;
        if (asked.Sum(f => (long)f.Content.Length) > _maxFileBytes)
            return $"the files it wrote exceed {ToolBridgeClientOptions.SectionName}:MaxFileBytes ({ToolBridgeProtocol.Describe(_maxFileBytes)}): none of them was written here.";
        using var _ = AgentRunScope.EnterToolCall();
        foreach (var file in asked)
        {
            var check = _fileSystem.ResolveAndValidate(file.Path, FileAccessRights.Write);
            if (!check.IsAllowed)
                return $"the file '{file.Path}' it wrote is refused here: {check.DenialReason ?? "the path is refused by the file system policy"}; none of its files was written here.";
        }

        var written = new List<string>();
        foreach (var file in asked)
        {
            try
            {
                await _fileSystem.WriteAllBytesAsync(file.Path, file.Content.ToArray(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
            {
                var done = written.Count == 0
                    ? "none of its files was written here"
                    : "already written here: " + string.Join(", ", written.Select(p => $"'{p}'"));
                return $"the file '{file.Path}' it wrote could not be written here: {ex.Message} ({done}).";
            }

            written.Add(file.Path);
        }

        return null;
    }

    /// <summary>
    /// The files of the answer this caller writes, at their normalised virtual paths: those of a tool that carries files,
    /// on a call of a write class that succeeded, at a path or under a directory an argument of a path-like parameter
    /// named. Each other file is logged and left out.
    /// </summary>
    private List<BridgeFile> AskedFiles(ImmutableList<BridgeFile> files, IReadOnlyDictionary<string, object?> arguments, bool succeeded)
    {
        var access = Inner.AccessFor(arguments);
        var writes = access is ToolAccess.Edit or ToolAccess.Execute;
        var named = writes && succeeded && _carriesFiles ? NamedPaths(arguments) : [];
        var asked = new List<BridgeFile>();
        foreach (var file in files)
        {
            var path = file?.Path is { } raw ? VirtualPaths.Normalize(raw, "/") : null;
            var reason = !_carriesFiles ? "this tool carries no files"
                : !succeeded ? "the call failed"
                : !writes ? $"the call was classed {access}, not a write"
                : path is null || !named.Any(n => VirtualPaths.IsAtOrUnder(path, n)) ? "no argument of the call names that path"
                : null;
            if (reason is null)
                asked.Add(file! with { Path = path! });
            else
                LogRefusedFile(_operatorLogger, Name, file?.Path ?? "(none)", reason);
        }

        return asked;
    }

    /// <summary>
    /// The paths the arguments of path-like parameters name (<see cref="ToolBridgePathArguments"/>), absolute or relative
    /// to the caller's working directory, normalised. A path that is the working directory, a mount's root, <c>/</c>, or
    /// a folder above any of them names nothing: it would open a whole tree to the bridge's answer.
    /// </summary>
    private HashSet<string> NamedPaths(IReadOnlyDictionary<string, object?> arguments)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        var working = _fileSystem.WorkingDirectory is { Length: > 0 } wd ? VirtualPaths.Normalize(wd, "/") ?? "/" : "/";
        var roots = Roots();
        foreach (var candidate in ToolBridgePathArguments.Of(arguments, Inner.Schema))
        {
            if (candidate.Length == 0 || candidate.Length > MaxPathLength || candidate.Contains("://", StringComparison.Ordinal) || candidate.Any(char.IsControl))
                continue;
            if (VirtualPaths.Normalize(candidate, working) is not { } path || roots.Any(root => VirtualPaths.IsAtOrUnder(root, path)))
                continue;
            named.Add(path);
        }

        return named;
    }

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning,
        Message = "{Tool} ran on the tool bridge, which sent back the file '{Path}': refused, {Reason}.")]
    private static partial void LogRefusedFile(ILogger logger, string tool, string path, string reason);

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
        // AlreadyBounded stays false whatever the bridge says: the caller's pipeline truncates every bridged result.
        return new ProtocolToolCallResponse(response.Success, result, response.Error, metadata);
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

/// <summary>
/// The arguments of a bridged call that may open the write-back of a file (#445): the strings, at any depth, of a
/// parameter that names a path. A parameter names a path when the tool's schema gives it, or the items of its list, the
/// format <c>path</c>; otherwise by its name, split into words on <c>_</c>, <c>-</c> and case changes: the whole name is
/// one of <see cref="WholeNames"/>, or its last word is one of <see cref="LastWords"/> (<c>save_to_path</c>,
/// <c>output_file</c>, <c>target_dir</c>). Any other argument, a text typed into a page or a query, never names a path.
/// </summary>
internal static class ToolBridgePathArguments
{
    /// <summary>The names that are a path-like parameter as a whole.</summary>
    public static readonly ImmutableHashSet<string> WholeNames = ImmutableHashSet.Create(StringComparer.Ordinal,
        "path", "paths", "file", "files", "filename", "filenames", "filepath", "dir", "dirs", "directory", "directories",
        "folder", "folders", "output", "destination", "dest", "target");

    /// <summary>The last words that make a parameter's name path-like.</summary>
    public static readonly ImmutableHashSet<string> LastWords = ImmutableHashSet.Create(StringComparer.Ordinal,
        "path", "paths", "file", "files", "filename", "filenames", "filepath", "dir", "dirs", "directory", "directories",
        "folder", "folders");

    /// <summary>Whether the parameter <paramref name="name"/> names a path.</summary>
    /// <param name="name">The parameter's name.</param>
    /// <param name="schema">Its schema; none when null.</param>
    /// <returns>True for a path-like parameter.</returns>
    public static bool IsPathLike(string name, Orkeon.Domain.Tools.Protocol.ParameterSchema? schema)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (schema is not null && (IsPathFormat(schema.Format) || IsPathFormat(schema.ItemsFormat)))
            return true;
        var words = Words(name);
        return words.Count > 0 && (WholeNames.Contains(string.Concat(words)) || WholeNames.Contains(string.Join('_', words)) || LastWords.Contains(words[^1]));
    }

    /// <summary>The strings the path-like parameters of <paramref name="arguments"/> hold, at any depth.</summary>
    /// <param name="arguments">The call's arguments.</param>
    /// <param name="schema">The tool's schema, read for the formats of its parameters.</param>
    /// <returns>Each string, in order.</returns>
    public static IEnumerable<string> Of(IReadOnlyDictionary<string, object?> arguments, ToolSchema? schema)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        foreach (var (name, value) in arguments)
        {
            var parameter = schema?.Parameters is { } parameters && parameters.TryGetValue(name, out var found) ? found : null;
            foreach (var text in Strings(value, IsPathLike(name, parameter)))
                yield return text;
        }
    }

    private static IEnumerable<string> Strings(object? value, bool pathLike)
    {
        switch (value)
        {
            case string text when pathLike:
                yield return text;
                break;
            case IReadOnlyDictionary<string, object?> map:
                foreach (var (name, inner) in map)
                {
                    foreach (var text in Strings(inner, IsPathLike(name, null)))
                        yield return text;
                }

                break;
            case System.Collections.IEnumerable list when value is not string:
                foreach (var item in list)
                {
                    foreach (var text in Strings(item, pathLike))
                        yield return text;
                }

                break;
        }
    }

    private static bool IsPathFormat(string? format) => string.Equals(format, "path", StringComparison.OrdinalIgnoreCase);

    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '_' or '-' or '.' or ' ')
            {
                Flush();
                continue;
            }

            if (char.IsUpper(c) && current.Length > 0 && !char.IsUpper(name[i - 1]))
                Flush();
            current.Append(char.ToLowerInvariant(c));
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0)
                words.Add(current.ToString());
            current.Clear();
        }
    }
}

/// <summary>The virtual paths of a bridged call, as the caller compares them (#445).</summary>
internal static class VirtualPaths
{
    /// <summary>
    /// <paramref name="path"/> made absolute against <paramref name="working"/>, its <c>.</c> and <c>..</c> resolved and
    /// its separators single; null when it climbs above the root.
    /// </summary>
    /// <param name="path">A virtual path, absolute or relative.</param>
    /// <param name="working">The directory a relative path resolves against.</param>
    /// <returns>The normalised path.</returns>
    public static string? Normalize(string path, string working)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(working);
        var full = path;
        if (!full.StartsWith('/'))
            full = working.TrimEnd('/') + "/" + full;
        var segments = new List<string>();
        foreach (var segment in full.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return "/" + string.Join('/', segments);
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="named"/> or lies under it.</summary>
    /// <param name="path">A normalised path.</param>
    /// <param name="named">A normalised path an argument named.</param>
    /// <returns>True at or under it.</returns>
    public static bool IsAtOrUnder(string path, string named) =>
        string.Equals(path, named, StringComparison.Ordinal) || path.StartsWith(named + "/", StringComparison.Ordinal);
}
