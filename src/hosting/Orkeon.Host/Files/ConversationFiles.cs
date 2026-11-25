using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Host.Api;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tools.Email.Mime;

namespace Orkeon.Host.Files;

/// <summary>A file to keep in a conversation.</summary>
/// <param name="Name">The name it was given. Untrusted.</param>
/// <param name="OpenAsync">Opens its content, only once the file is admitted; the stream is disposed here.</param>
internal sealed record IncomingFile(string Name, Func<CancellationToken, Task<Stream>> OpenAsync)
{
    /// <summary>The channel's identifier of it: a file still kept under this identifier is not kept twice.</summary>
    public string? Id { get; init; }

    /// <summary>The announced size in bytes, when there is one. The bytes read are what counts.</summary>
    public long? Size { get; init; }
}

/// <summary>What became of a file to keep: its virtual path and size, or why it was refused.</summary>
internal sealed record StoredFile(string? Path, long Size, RefusedFile? Refused);

/// <summary>
/// The folders of the chat conversations, each conversation's its own: <c>&lt;Root&gt;/&lt;key&gt;/in</c>, <c>/out</c>,
/// <c>/part</c> and <c>meta.json</c>.
/// <para>
/// Two mount sets read them. The agents' (<see cref="ConversationRun.Registry"/>) has <c>/inbox</c> read-only and
/// <c>/outbox</c> read-write; the host's (<see cref="UseAsync"/>), by which it receives, lists, sends and deletes, has
/// both read-write and the conversation's own folder. Each is entered inside the method that uses it and left before the
/// other is entered: the ambient scope flows down the awaits, and to nobody else.
/// </para>
/// <para>One lock per conversation covers reception, the host's own accesses, a run's start and end, and retention.</para>
/// </summary>
internal sealed partial class ConversationFiles : IDisposable
{
    /// <summary>
    /// The conversation's own folder in the host's mount set. Under <c>/inbox</c>, which no other mount may claim, so it
    /// clashes with none; the agents' mount set does not hold it.
    /// </summary>
    private const string Own = RunnerVirtualRoots.Inbox + "/.conversation";
    private const string Parts = Own + "/part";
    private static readonly string[] s_contents = [RunnerVirtualRoots.Inbox, RunnerVirtualRoots.Outbox, Parts];

    private const string MetaPath = Own + "/meta.json";
    private const int BlockBytes = 64 * 1024;
    private const long GrowthBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly HostFilesOptions _options;
    private readonly IFileSystemService _fs;
    private readonly IFileSystemScope _scope;
    private readonly FileSystemRegistry _boot;
    private readonly HostApiOptionalServices _optional;
    private readonly ILogger<ConversationFiles> _logger;
    private readonly ConversationStore _store;

    private readonly Lock _gatesLock = new();
    private readonly Dictionary<string, Gate> _gates = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _budget = new(1, 1);
    private readonly Dictionary<string, long> _reserved = new(StringComparer.Ordinal);

    public ConversationFiles(
        IOptions<OrkeonHostOptions> options,
        IFileSystemService fileSystem,
        IFileSystemScope scope,
        FileSystemRegistry boot,
        HostApiOptionalServices optional,
        ILogger<ConversationFiles> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.Files;
        _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _boot = boot ?? throw new ArgumentNullException(nameof(boot));
        _optional = optional ?? throw new ArgumentNullException(nameof(optional));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _store = new ConversationStore(_options.Root);
        _boot.ReservedFolder = _store.Root;
    }

    /// <summary>The settings the conversations' files obey.</summary>
    public HostFilesOptions Options => _options;

    /// <inheritdoc />
    public void Dispose() => _budget.Dispose();

    /// <summary>
    /// Opens a run: its folders exist, and retention leaves them alone until the result is disposed. A run that belongs to
    /// no conversation gets two empty folders, deleted when it ends.
    /// </summary>
    /// <exception cref="ConversationPreparationException">The files already exceed what the settings allow.</exception>
    public async Task<ConversationRun> OpenRunAsync(HostedConversation? conversation, string runId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (conversation is null)
        {
            var name = ConversationStore.NameOfRun(runId);
            var temporary = Acquire(name);
            try
            {
                _store.CreateFolders(name);
                return new ConversationRun(this, temporary, reference: null, runId);
            }
            catch
            {
                temporary.Dispose();
                throw;
            }
        }

        var reference = new ConversationRef(conversation.Channel, conversation.ConversationId);
        var held = Acquire(reference.Key);
        try
        {
            await RefuseWhenFullAsync(reference.Key, ct).ConfigureAwait(false);
            // Marked before it runs: a host that is killed leaves the mark, and retention asks whether the run waits.
            await UseAsync(
                reference,
                crew: null,
                async (_, token) =>
                {
                    await ClearPartsAsync(token).ConfigureAwait(false);
                    return await TouchAsync(reference, meta => meta with { SuspendedRuns = [.. meta.SuspendedRuns.Where(r => r != runId), runId] }, token).ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
            return new ConversationRun(this, held, reference, runId);
        }
        catch
        {
            held.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> in the host's mount set of a conversation, under its lock: <c>/inbox</c> and
    /// <c>/outbox</c> read-write, and what <paramref name="crew"/> is granted. The work calls nothing else of this class.
    /// </summary>
    public async Task<T> UseAsync<T>(
        ConversationRef conversation, HostedCrewOptions? crew, Func<IFileSystemService, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        using var held = Acquire(conversation.Key);
        await held.Gate.Lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _store.CreateFolders(conversation.Key);
            return await InHostNamespaceAsync(conversation.Key, crew, work, ct).ConfigureAwait(false);
        }
        finally
        {
            held.Gate.Lock.Release();
        }
    }

    /// <summary>
    /// Keeps a file in a conversation, under <paramref name="folder"/> and a name no file there has: what is already kept
    /// is never written over. A file over a limit, of an extension not taken, or that cannot be read, is refused.
    /// </summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="crew">The crew whose granted folders <paramref name="folder"/> may name.</param>
    /// <param name="file">The file.</param>
    /// <param name="index">Its place among those received with it, from zero: what names it when its own name cannot.</param>
    /// <param name="folder">The virtual folder; <c>/inbox</c> by default.</param>
    /// <param name="ct">Stops the reception; it throws.</param>
    public Task<StoredFile> StoreAsync(
        ConversationRef conversation,
        HostedCrewOptions? crew,
        IncomingFile file,
        int index,
        string folder = RunnerVirtualRoots.Inbox,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        return UseAsync(conversation, crew, (_, token) => StoreLockedAsync(conversation, file, index, crew, folder, token), ct);
    }

    /// <summary>
    /// Deletes the conversations with no activity since <see cref="HostFilesOptions.Retention"/>, but those with a run in
    /// progress or suspended, and what runs without a conversation left behind.
    /// </summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        foreach (var key in _store.Conversations())
        {
            ct.ThrowIfCancellationRequested();
            await SweepOneAsync(key, KeptAsync, ct).ConfigureAwait(false);
        }

        // An age against the retention: no date far enough back exists for a long one.
        var now = _optional.Time.GetUtcNow();
        foreach (var (name, written) in _store.RunFolders())
        {
            ct.ThrowIfCancellationRequested();
            if (now - written > _options.Retention)
                await SweepOneAsync(name, (_, _) => Task.FromResult(false), ct).ConfigureAwait(false);
        }
    }

    /// <summary>The mount set of a run's agents.</summary>
    /// <param name="name">The folder of the run or of its conversation.</param>
    /// <param name="crew">The crew, whose granted folders are added.</param>
    /// <param name="ofConversation">Whether the folder is a conversation's: what it holds is kept out of the stores other runs read.</param>
    /// <exception cref="ConversationPreparationException">A mount leads to the conversations' folder.</exception>
    internal FileSystemRegistry RunRegistry(string name, HostedCrewOptions crew, bool ofConversation) => Compose(
        crew,
        [
            OwnMount(_store.InboxOf(name), RunnerVirtualRoots.Inbox, FileAccessRights.Read) with { IsPrivate = ofConversation, KeepsRoot = true },
            OwnMount(_store.OutboxOf(name), RunnerVirtualRoots.Outbox, FileAccessRights.ReadWrite) with { IsPrivate = ofConversation, KeepsRoot = true },
        ]);

    private static FileSystemMount OwnMount(string folder, string virtualPath, FileAccessRights rights) =>
        new(folder, virtualPath, rights) { InReservedFolder = true };

    // A folder the crew is granted may have been replaced by a link since the start, by a crew that writes in its parent.
    // The registry refuses each access that follows; saying it here ends the run before it does anything.
    private FileSystemRegistry Compose(HostedCrewOptions? crew, IReadOnlyList<FileSystemMount> own)
    {
        IReadOnlyList<FileSystemMount> granted = crew is null ? [] : [.. CrewRunner.GrantedMounts(crew)];
        if (crew is not null && HostFilesStartup.Reaching(_store.Root, [.. granted, .. _boot.GetMounts()]) is { } mount)
        {
            LogMountReaches(mount.VirtualPath, crew.Name);
            throw new ConversationPreparationException(
                $"The folder mounted on '{mount.VirtualPath}' leads to where the files of the conversations are kept. The operator must move it.");
        }

        return ScopedMountComposition.ForExecution(_boot, [.. own, .. granted]);
    }

    internal IDisposable Enter(FileSystemRegistry registry) => _scope.Enter(registry);

    /// <summary>
    /// The files of <c>/outbox</c> in the mount set in force, by path: no link, and nothing a dot hides. Empty, and logged,
    /// when the folder cannot be read.
    /// </summary>
    internal async Task<IReadOnlyDictionary<string, (long Size, DateTimeOffset Written)>> ListOutboxAsync()
    {
        var files = new Dictionary<string, (long, DateTimeOffset)>(StringComparer.Ordinal);
        try
        {
            await foreach (var entry in _fs.EnumerateFilesAsync(RunnerVirtualRoots.Outbox, new VirtualEnumerationOptions(), CancellationToken.None).ConfigureAwait(false))
            {
                if (entry.Kind == VirtualEntryKind.File && !IsHidden(entry.VirtualPath))
                    files[entry.VirtualPath] = (entry.SizeBytes, entry.LastModified);
            }
        }
#pragma warning disable CA1031 // The run has its answer: a folder that cannot be listed must not replace it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogOutboxUnreadable(ex);
        }

        return files;
    }

    /// <summary>The files of a conversation's <c>/inbox</c>, the latest first, nothing hidden; empty, and logged, when it cannot be read.</summary>
    internal Task<List<(string Path, long Size)>> ListInboxAsync(ConversationRef conversation, CancellationToken ct) => UseAsync(
        conversation,
        crew: null,
        async (fs, token) =>
        {
            var files = new List<(string Path, long Size, DateTimeOffset Written)>();
            try
            {
                await foreach (var entry in fs.EnumerateFilesAsync(RunnerVirtualRoots.Inbox, new VirtualEnumerationOptions(), token).ConfigureAwait(false))
                {
                    if (entry.Kind == VirtualEntryKind.File && !IsHidden(entry.VirtualPath))
                        files.Add((entry.VirtualPath, entry.SizeBytes, entry.LastModified));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
            {
                LogInboxUnreadable(ex);
            }

            return files.OrderByDescending(f => f.Written).ThenBy(f => f.Path, StringComparer.Ordinal).Select(f => (f.Path, f.Size)).ToList();
        },
        ct);

    /// <summary>Ends a run: the conversation's activity, and the run it waits on when that one is suspended.</summary>
    internal async Task CloseRunAsync(ConversationRef? conversation, Hold held, string runId, bool suspended)
    {
        try
        {
            if (conversation is { } reference)
            {
                await UseAsync(
                    reference,
                    crew: null,
                    (_, token) => TouchAsync(reference, meta => suspended ? meta : meta with { SuspendedRuns = [.. meta.SuspendedRuns.Where(r => r != runId)] }, token),
                    CancellationToken.None).ConfigureAwait(false);
                if (_store.SizeOf(reference.Key) is not { } size)
                    LogUnmeasured(reference.Channel);
                else if (size > _options.MaxConversationBytes)
                    LogOverLimit(reference.Channel, size, _options.MaxConversationBytes);
            }
            else
            {
                await InHostNamespaceAsync(held.Name, crew: null, (fs, token) => DeleteAllAsync(held.Name, fs, token), CancellationToken.None).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // The run is over and has its answer: what its folders could not do is the log's matter.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCloseFailed(ex, held.Name);
        }
        finally
        {
            held.Dispose();
        }
    }

    private async Task<StoredFile> StoreLockedAsync(ConversationRef conversation, IncomingFile file, int index, HostedCrewOptions? crew, string folder, CancellationToken ct)
    {
        var name = SafeFileNames.Sanitize(file.Name, index);
        StoredFile Refuse(string reason) => new(null, 0, new RefusedFile(name, reason));

        // The conversation's own folders are one for every crew; a granted one is each crew's own.
        var owner = folder.TrimEnd('/') is RunnerVirtualRoots.Inbox or RunnerVirtualRoots.Outbox ? null : crew?.Name;
        bool IsSame(StoredAttachment a) => a.Id == file.Id && a.Crew == owner && FolderIs(a.Path, folder);

        if (file.Id is not null)
        {
            try
            {
                var meta = await ReadMetaAsync(conversation, ct).ConfigureAwait(false);
                if (meta.Attachments.FirstOrDefault(IsSame) is { } known
                    && await _fs.TryGetEntryAsync(known.Path, ct).ConfigureAwait(false) is { Kind: VirtualEntryKind.File } kept)
                {
                    return new StoredFile(known.Path, kept.SizeBytes, null);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
            {
                LogRefused(ex, conversation.Channel);
                return Refuse("it could not be read");
            }
        }

        if (!_options.Takes(name))
            return Refuse("files of this type are not taken");
        if (file.Size > _options.MaxFileBytes)
            return Refuse(TooLarge());
        // Judged by its name before a byte of it is downloaded.
        if (!_fs.ResolveAndValidate($"{folder.TrimEnd('/')}/{name}", FileAccessRights.Write | FileAccessRights.Create).IsAllowed)
            return Refuse("the host does not keep a file of this name");

        using var reservation = new Reservation(this, conversation.Key);
        if (await reservation.GrowToAsync(Math.Max(file.Size ?? 0, 1), ct).ConfigureAwait(false) is { } full)
            return Refuse(full);

        var part = string.Create(CultureInfo.InvariantCulture, $"{Parts}/{Guid.NewGuid():N}.part");
        try
        {
            var (size, refusal) = await ReceiveAsync(file, part, reservation, ct).ConfigureAwait(false);
            if (refusal is not null)
                return Refuse(refusal);

            var path = await PlaceAsync(part, folder, name, reservation, ct).ConfigureAwait(false);
            await TouchAsync(
                conversation,
                m => file.Id is null ? m : m with { Attachments = [.. m.Attachments.Where(a => !IsSame(a)), new StoredAttachment(file.Id, path, owner)] },
                CancellationToken.None).ConfigureAwait(false);
            return new StoredFile(path, size, null);
        }
        catch (FileAccessDeniedException ex)
        {
            LogRefused(ex, conversation.Channel);
            return Refuse("the host does not keep a file of this name");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A file that cannot be read or kept is refused, never the message it came with.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRefused(ex, conversation.Channel);
            return Refuse("it could not be read");
        }
        finally
        {
            await DeletePartAsync(part).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the file into the partial folder: the bytes counted as they come, each one reserved before it is written.</summary>
    private async Task<(long Size, string? Refusal)> ReceiveAsync(IncomingFile file, string part, Reservation reservation, CancellationToken ct)
    {
        var source = await file.OpenAsync(ct).ConfigureAwait(false);
        await using var sourceLease = source.ConfigureAwait(false);
        var target = await _fs.OpenWriteStreamAsync(part, ct).ConfigureAwait(false);
        await using var targetLease = target.ConfigureAwait(false);

        var block = new byte[BlockBytes];
        long size = 0;
        int read;
        while ((read = await source.ReadAsync(block, ct).ConfigureAwait(false)) > 0)
        {
            size += read;
            if (size > _options.MaxFileBytes)
                return (size, TooLarge());
            if (size > reservation.Bytes
                && await reservation.GrowToAsync(Math.Min(size + GrowthBytes, _options.MaxFileBytes), ct).ConfigureAwait(false) is not null
                && await reservation.GrowToAsync(size, ct).ConfigureAwait(false) is { } full)
            {
                return (size, full);
            }

            await target.WriteAsync(block.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return (size, null);
    }

    /// <summary>
    /// Copies the received file under a free name. The copy creates or fails, so a name taken meanwhile (the agents write
    /// in <c>/outbox</c>) refuses the file. The bytes stop being reserved as they become visible, under one lock.
    /// </summary>
    private async Task<string> PlaceAsync(string part, string folder, string name, Reservation reservation, CancellationToken ct)
    {
        var root = folder.TrimEnd('/');
        var taken = await NamesInAsync(root, ct).ConfigureAwait(false);
        await _budget.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string path;
            do
                path = $"{root}/{SafeFileNames.Unique(name, taken)}";
            while (await _fs.ExistsAsync(path, ct).ConfigureAwait(false));

            try
            {
                await _fs.CopyAsync(part, path, overwrite: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException) when (root == RunnerVirtualRoots.Inbox)
            {
                // Nobody but the host, under this conversation's lock, writes in the inbox, and the name was free: what is
                // there is the half of this copy. Elsewhere it may be an agent's file, and it is left alone.
                await _fs.DeleteAsync(path, recursive: false, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            reservation.ReleaseLocked();
            return path;
        }
        finally
        {
            _budget.Release();
        }
    }

    private async Task<HashSet<string>> NamesInAsync(string root, CancellationToken ct)
    {
        // Case ignored: on a file system that ignores it, two spellings are one file.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var entry in _fs.EnumerateFilesAsync(root, new VirtualEnumerationOptions(Recursive: false), ct).ConfigureAwait(false))
            names.Add(entry.VirtualPath[(entry.VirtualPath.LastIndexOf('/') + 1)..]);
        return names;
    }

    private async Task DeletePartAsync(string part)
    {
        try
        {
            await _fs.DeleteAsync(part, recursive: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
        {
            LogPartLeft(ex);
        }
    }

    private string TooLarge() =>
        string.Create(CultureInfo.InvariantCulture, $"it is larger than {_options.MaxFileBytes} bytes");

    private static bool FolderIs(string path, string folder) =>
        string.Equals(path[..path.LastIndexOf('/')], folder.TrimEnd('/'), StringComparison.Ordinal);

    private static bool IsHidden(string path) => path.Split('/').Any(s => s.StartsWith('.'));

    private async Task RefuseWhenFullAsync(string key, CancellationToken ct)
    {
        await _budget.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_store.SizeOf(key) is not { } size || size > _options.MaxConversationBytes)
                throw new ConversationPreparationException("The files of this conversation exceed their limit. Open a new thread.");
            if (_store.TotalSize(_options.MaxConversationBytes) > _options.MaxTotalBytes)
                throw new ConversationPreparationException("The files of the conversations exceed the limit of this host. Its operator must make room.");
        }
        finally
        {
            _budget.Release();
        }
    }

    private async Task<T> InHostNamespaceAsync<T>(
        string name, HostedCrewOptions? crew, Func<IFileSystemService, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        using var registry = Compose(
            crew,
            [
                OwnMount(_store.InboxOf(name), RunnerVirtualRoots.Inbox, FileAccessRights.ReadWrite),
                OwnMount(_store.OutboxOf(name), RunnerVirtualRoots.Outbox, FileAccessRights.ReadWrite),
                OwnMount(_store.FolderOf(name), Own, FileAccessRights.ReadWrite),
            ]);
        using var entered = _scope.Enter(registry);
        return await work(_fs, ct).ConfigureAwait(false);
    }

    // ----- meta.json -----

    private sealed record ConversationMeta
    {
        public string Channel { get; init; } = string.Empty;

        public string ConversationId { get; init; } = string.Empty;

        public DateTimeOffset LastActivity { get; init; }

        public IReadOnlyList<StoredAttachment> Attachments { get; init; } = [];

        public IReadOnlyList<string> SuspendedRuns { get; init; } = [];
    }

    private sealed record StoredAttachment(string Id, string Path, string? Crew = null);

    /// <summary>The conversation's record; a new one when it has none, or one that cannot be read.</summary>
    private async Task<ConversationMeta> ReadMetaAsync(ConversationRef conversation, CancellationToken ct) =>
        await TryReadMetaAsync(ct).ConfigureAwait(false)
        ?? new ConversationMeta { Channel = conversation.Channel, ConversationId = conversation.ConversationId };

    private async Task<ConversationMeta?> TryReadMetaAsync(CancellationToken ct)
    {
        var text = await _fs.TryReadAllTextAsync(MetaPath, ct).ConfigureAwait(false);
        if (text is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<ConversationMeta>(text, Json) is { Attachments: not null, SuspendedRuns: not null } meta ? meta : null;
        }
        catch (JsonException ex)
        {
            LogMetaUnreadable(ex);
            return null;
        }
    }

    private async Task<bool> TouchAsync(ConversationRef conversation, Func<ConversationMeta, ConversationMeta> change, CancellationToken ct)
    {
        var meta = await ReadMetaAsync(conversation, ct).ConfigureAwait(false);
        await WriteMetaAsync(change(meta), ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Writes the record whole beside the old one, then in its place: a reader sees one or the other.</summary>
    private async Task WriteMetaAsync(ConversationMeta meta, CancellationToken ct)
    {
        const string written = MetaPath + ".new";
        await _fs.WriteAllTextAsync(written, JsonSerializer.Serialize(meta with { LastActivity = _optional.Time.GetUtcNow() }, Json), ct).ConfigureAwait(false);
        await _fs.MoveAsync(written, MetaPath, ct).ConfigureAwait(false);
    }

    // ----- retention -----

    private async Task SweepOneAsync(string name, Func<string, CancellationToken, Task<bool>> keptAsync, CancellationToken ct)
    {
        // Held, so it counts as one use: any other, a run in progress included, leaves the folder alone.
        using var held = Acquire(name);
        if (!await held.Gate.Lock.WaitAsync(0, ct).ConfigureAwait(false))
            return;
        try
        {
            if (!IsOnlyUse(held.Gate) || !_store.Exists(name))
                return;

            await InHostNamespaceAsync(name, crew: null, async (fs, token) =>
            {
                if (await keptAsync(name, token).ConfigureAwait(false))
                    return false;
                return await DeleteAllAsync(name, fs, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // One conversation that cannot be deleted must not keep the others; it is tried again at the next pass.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSweepFailed(ex, name);
        }
        finally
        {
            held.Gate.Lock.Release();
        }
    }

    /// <summary>
    /// Deletes a folder, what the agents wrote first and the record last: a delete that fails half-way leaves the record, so
    /// the next pass goes on from there. What an agent took the rights away from is given them back.
    /// </summary>
    private async Task<bool> DeleteAllAsync(string name, IFileSystemService fs, CancellationToken ct)
    {
        _store.MakeAccessible(name);
        foreach (var folder in s_contents)
            await fs.DeleteAsync(folder, recursive: true, ct).ConfigureAwait(false);
        await fs.DeleteAsync(Own, recursive: true, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Deletes what a reception cut short left behind. Under the conversation's lock no reception is in progress.</summary>
    private async Task ClearPartsAsync(CancellationToken ct)
    {
        try
        {
            var left = new List<string>();
            await foreach (var entry in _fs.EnumerateFilesAsync(Parts, new VirtualEnumerationOptions(Recursive: false), ct).ConfigureAwait(false))
                left.Add(entry.VirtualPath);
            foreach (var part in left)
                await _fs.DeleteAsync(part, recursive: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
        {
            LogPartLeft(ex);
        }
    }

    /// <summary>Whether a conversation is kept: it was active, or a run of it waits for an answer.</summary>
    private async Task<bool> KeptAsync(string name, CancellationToken ct)
    {
        if (await TryReadMetaAsync(ct).ConfigureAwait(false) is not { } meta)
        {
            // Nothing says when it was last active: its time starts now, and what it holds is kept until it ends.
            await ClearPartsAsync(ct).ConfigureAwait(false);
            await WriteMetaAsync(new ConversationMeta(), ct).ConfigureAwait(false);
            return true;
        }

        await ClearPartsAsync(ct).ConfigureAwait(false);
        if (_optional.Time.GetUtcNow() - meta.LastActivity < _options.Retention)
            return true;

        foreach (var run in meta.SuspendedRuns)
        {
            // Without a store no run can be resumed. A store that cannot answer throws, and the conversation is kept.
            if (_optional.Input is { } input && await input.GetPendingAsync(run, ct).ConfigureAwait(false) is not null)
                return true;
        }

        return false;
    }

    // ----- locks and uses -----

    internal sealed class Gate
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public int Uses { get; set; }
    }

    /// <summary>One use of a folder: its lock exists, and retention leaves it alone, until it is disposed.</summary>
    internal sealed class Hold(ConversationFiles files, string name, Gate gate) : IDisposable
    {
        private bool _disposed;

        public string Name => name;

        public Gate Gate => gate;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            files.Release(name, gate);
        }
    }

    private Hold Acquire(string name)
    {
        lock (_gatesLock)
        {
            if (!_gates.TryGetValue(name, out var gate))
                _gates[name] = gate = new Gate();
            gate.Uses++;
            return new Hold(this, name, gate);
        }
    }

    private bool IsOnlyUse(Gate gate)
    {
        lock (_gatesLock)
            return gate.Uses == 1;
    }

    private void Release(string name, Gate gate)
    {
        lock (_gatesLock)
        {
            if (--gate.Uses > 0)
                return;
            _gates.Remove(name);
        }

        gate.Lock.Dispose();
    }

    // ----- limits -----

    /// <summary>Bytes promised to a file being received: no other reception may count on them.</summary>
    private sealed class Reservation(ConversationFiles files, string key) : IDisposable
    {
        private bool _released;

        public long Bytes { get; private set; }

        /// <summary>Reserves <paramref name="bytes"/> in all; null when they are, else why they cannot be.</summary>
        public async Task<string?> GrowToAsync(long bytes, CancellationToken ct)
        {
            if (bytes <= Bytes)
                return null;
            await files._budget.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var more = bytes - Bytes;
                long ofConversation, inAll;
                lock (files._reserved)
                {
                    ofConversation = files._reserved.GetValueOrDefault(key);
                    inAll = files._reserved.Values.Sum();
                }

                if (files._store.SizeOf(key) is not { } size || more > files._options.MaxConversationBytes - size - ofConversation)
                    return "the files of this conversation have reached their limit";
                if (more > files._options.MaxTotalBytes - files._store.TotalSize(files._options.MaxConversationBytes) - inAll)
                    return "the files of the conversations have reached the limit of this host";

                lock (files._reserved)
                    files._reserved[key] = files._reserved.GetValueOrDefault(key) + more;
                Bytes = bytes;
                return null;
            }
            finally
            {
                files._budget.Release();
            }
        }

        /// <summary>Gives the bytes back: they are on the disk, or will not be.</summary>
        public void ReleaseLocked()
        {
            if (_released)
                return;
            _released = true;
            lock (files._reserved)
            {
                var left = files._reserved.GetValueOrDefault(key) - Bytes;
                if (left > 0)
                    files._reserved[key] = left;
                else
                    files._reserved.Remove(key);
            }
        }

        public void Dispose() => ReleaseLocked();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A file received on {Channel} was not kept")]
    private partial void LogRefused(Exception ex, string channel);

    [LoggerMessage(Level = LogLevel.Error, Message = "The folder mounted on {Mount} leads, through a link, to where the conversations' files are kept: crew {Crew} is refused")]
    private partial void LogMountReaches(string mount, string crew);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A partial file could not be deleted")]
    private partial void LogPartLeft(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The inbox of a conversation could not be listed")]
    private partial void LogInboxUnreadable(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The outbox of a conversation could not be listed")]
    private partial void LogOutboxUnreadable(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The record of a conversation's files cannot be read: its retention starts again")]
    private partial void LogMetaUnreadable(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A conversation on {Channel} holds {Size} bytes of files, over its limit of {Limit}: it starts no other run")]
    private partial void LogOverLimit(string channel, long size, long limit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A conversation on {Channel} holds a folder that cannot be listed, so its files cannot be measured: it starts no other run")]
    private partial void LogUnmeasured(string channel);

    [LoggerMessage(Level = LogLevel.Error, Message = "The files of {Folder} could not be closed")]
    private partial void LogCloseFailed(Exception ex, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The files of {Folder} could not be deleted; they are tried again at the next pass")]
    private partial void LogSweepFailed(Exception ex, string folder);
}

/// <summary>The folders of one run, from before its preparation to its end.</summary>
internal sealed class ConversationRun
{
    private readonly ConversationFiles _files;
    private readonly ConversationFiles.Hold _held;
    private readonly ConversationRef? _conversation;
    private readonly string _runId;

    internal ConversationRun(ConversationFiles files, ConversationFiles.Hold held, ConversationRef? reference, string runId)
    {
        _files = files;
        _held = held;
        _conversation = reference;
        _runId = runId;
    }

    /// <summary>The mount set of the run's agents. The caller enters it, in the method that uses it.</summary>
    public FileSystemRegistry Registry(HostedCrewOptions crew) => _files.RunRegistry(_held.Name, crew, _conversation is not null);

    /// <summary>Whether the folders are a conversation's: a run without one gets none of a person's files.</summary>
    public bool OfConversation => _conversation is not null;

    /// <summary>The outbox as the run's mount set, entered, shows it.</summary>
    public Task<IReadOnlyDictionary<string, (long Size, DateTimeOffset Written)>> ListOutboxAsync() => _files.ListOutboxAsync();

    /// <summary>Ends the run: the folders of a run without a conversation are deleted, a conversation's are kept.</summary>
    /// <param name="suspended">Whether it stopped on a question to a person.</param>
    public Task CloseAsync(bool suspended) => _files.CloseRunAsync(_conversation, _held, _runId, suspended);
}
