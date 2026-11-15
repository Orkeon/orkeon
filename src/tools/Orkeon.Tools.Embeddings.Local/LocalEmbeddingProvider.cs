using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.FileSystem;
using SmartComponents.LocalEmbeddings;

namespace Orkeon.Tools.Embeddings.Local;

/// <summary>
/// On-device embedding provider backed by <see cref="LocalEmbedder"/>
/// (BGE-micro-v2 ONNX, 384 dimensions, CPU). Zero network, zero API key.
/// </summary>
/// <remarks>
/// <para>
/// Two model-loading modes are supported:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Zero-config (default) — <see cref="LocalEmbeddingOptions.ModelPath"/> is <see langword="null"/>
///     or empty: the embedded BGE-micro-v2 model shipped by <c>SmartComponents.LocalEmbeddings</c>
///     is used.
///   </description></item>
///   <item><description>
///     Custom model — <see cref="LocalEmbeddingOptions.ModelPath"/> is a <b>virtual</b> path
///     (e.g. <c>"/models/my-custom.onnx"</c>) and an <see cref="IFileSystemService"/> is provided.
///     The path is resolved through the VFS (mandatory routing — see
///     <c>docs/architecture/vfs-compliance.md</c>); physical paths must go through a mount.
///   </description></item>
/// </list>
/// </remarks>
public sealed class LocalEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    /// <summary>The name of the model embedded in the package.</summary>
    public const string EmbeddedModelName = "bge-micro-v2";

    private const int EmbeddedModelDimensions = 384;

    private readonly Func<LocalEmbedder> _embedderFactory;
    private readonly object _embedderLock = new();
    private Lazy<LocalEmbedder>? _attempt;
    private readonly LocalEmbeddingOptions _options;
    private readonly Lazy<int> _dimensions;
    private int _disposed;

    /// <summary>
    /// Creates a new local embedding provider.
    /// </summary>
    /// <param name="options">
    /// Optional configuration. Defaults to <c>new LocalEmbeddingOptions()</c> which yields
    /// a working zero-config setup against the embedded BGE-micro-v2 model.
    /// </param>
    /// <param name="logger">Optional logger. Defaults to <see cref="Microsoft.Extensions.Logging.Abstractions.NullLogger{T}"/>.</param>
    /// <param name="fileSystem">
    /// VFS service used to resolve <see cref="LocalEmbeddingOptions.ModelPath"/> (when set)
    /// against the configured mounts.
    /// </param>
    /// <exception cref="FileAccessDeniedException">
    /// Propagated from <see cref="IFileSystemService"/> when the configured
    /// <see cref="LocalEmbeddingOptions.ModelPath"/> is outside any mount or lacks Read rights.
    /// </exception>
    public LocalEmbeddingProvider(
        IFileSystemService fileSystem,
        LocalEmbeddingOptions? options = null,
        ILogger<LocalEmbeddingProvider>? logger = null)
        : this(fileSystem, options, logger, CreateRealEmbedder)
    {
    }

    internal LocalEmbeddingProvider(
        IFileSystemService fileSystem,
        LocalEmbeddingOptions? options,
        ILogger<LocalEmbeddingProvider>? logger,
        Func<string?, LocalEmbedder> embedderFactory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(embedderFactory);
        _options = options ?? new LocalEmbeddingOptions();
        // VFS validation stays eager; only the model load is deferred to the first embedding.
        var resolvedPath = ResolveModelPath(_options, fileSystem);
        _embedderFactory = () => embedderFactory(resolvedPath);
        _dimensions = new Lazy<int>(ProbeDimensions, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Resolves <see cref="LocalEmbeddingOptions.ModelPath"/> through the VFS (when set) and
    /// instantiates the underlying <see cref="LocalEmbedder"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Upstream API choice</b>. Reflection over
    /// <c>SmartComponents.LocalEmbeddings 0.1.0-preview10148</c> (the version pinned by this
    /// project) shows that <see cref="LocalEmbedder"/> exposes a single public constructor:
    /// <c>(string modelName = default, bool caseSensitive = false)</c>. There is <b>no</b>
    /// <see cref="System.IO.Stream"/> or <c>byte[]</c> overload. Therefore the SPEC's preferred
    /// path (<c>fs.ReadAllBytes(...)</c> + <c>new LocalEmbedder(stream)</c>) is not feasible
    /// without a fork of the upstream library.
    /// </para>
    /// <para>
    /// We use the SPEC §6.2 documented fallback: resolve the virtual path with
    /// <see cref="IFileSystemService.ResolveAndValidate"/> (the explicit boundary API for
    /// libraries that demand a physical handle) and forward the resolved string to
    /// <see cref="LocalEmbedder"/>. This stays VFS-compliant — no direct
    /// <c>System.IO</c> call escapes the provider, and the FS service enforces both
    /// mount membership and <see cref="FileAccessRights.Read"/> rights.
    /// </para>
    /// </remarks>
    private static string? ResolveModelPath(
        LocalEmbeddingOptions options,
        IFileSystemService fs)
    {
        // Embedded BGE-micro-v2 (384 dims) when no path is set.
        if (string.IsNullOrEmpty(options.ModelPath)) return null;

        // VFS routing: validate mount + Read rights before handing anything to the upstream lib.
        // ResolveAndValidate is the explicit boundary API for boundaries requiring a physical handle.
        var validation = fs.ResolveAndValidate(options.ModelPath, FileAccessRights.Read);
        if (!validation.IsAllowed || validation.ResolvedPath is null)
        {
            throw new FileAccessDeniedException(
                message: $"LocalEmbeddingProvider: VFS rejected ModelPath '{options.ModelPath}': "
                    + (validation.DenialReason ?? "access denied"),
                virtualPath: options.ModelPath,
                requiredRight: FileAccessRights.Read);
        }

        return validation.ResolvedPath;
    }

    private static LocalEmbedder CreateRealEmbedder(string? resolvedPath)
    {
        if (resolvedPath is not null) return new LocalEmbedder(resolvedPath);

        // The upstream loads the model files on the first Embed, not in the constructor: probe here
        // so a missing model fails inside GetEmbedder's wrapper, which adds the hint.
        var embedder = new LocalEmbedder();
        try
        {
            embedder.Embed("probe");
        }
        catch
        {
            embedder.Dispose();
            throw;
        }

        return embedder;
    }

    private LocalEmbedder GetEmbedder()
    {
        Lazy<LocalEmbedder> attempt;
        lock (_embedderLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            // Single flight: concurrent first calls share one load and, if it fails, its failure.
            attempt = _attempt ??= new Lazy<LocalEmbedder>(
                _embedderFactory, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        LocalEmbedder embedder;
        try
        {
            embedder = attempt.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_embedderLock)
            {
                // A failed load is not cached: the next call starts a new attempt.
                if (ReferenceEquals(_attempt, attempt)) _attempt = null;
            }

            var what = string.IsNullOrEmpty(_options.ModelPath)
                ? "the embedded model (LocalEmbeddingsModel/default/model.onnx). The build may have "
                    + "excluded it (ExcludeAssets=\"build\" on the SmartComponents.LocalEmbeddings "
                    + "reference): drop that exclusion to get the model back, or avoid the local "
                    + "provider by configuring a remote one (Orkeon:Embeddings) or setting "
                    + "RaggableTree:Embedding:Provider=None."
                : $"the model '{_options.ModelPath}'.";
            throw new InvalidOperationException(
                "LocalEmbeddingProvider could not load " + what, ex);
        }

        lock (_embedderLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                // Disposed while this call was loading the model: Dispose saw no embedder yet, so the
                // load that finished owns it. An attempt Dispose already released is no longer _attempt.
                if (ReferenceEquals(_attempt, attempt))
                {
                    _attempt = null;
                    embedder.Dispose();
                }

                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        return embedder;
    }

    /// <summary>
    /// Embedding vector dimension. Probed lazily on first read by embedding the literal
    /// <c>"probe"</c> and caching the result. Subsequent reads return the cached value.
    /// For BGE-micro-v2 this is <c>384</c>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public int Dimensions
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return string.IsNullOrEmpty(_options.ModelPath) ? EmbeddedModelDimensions : _dimensions.Value;
        }
    }

    /// <summary>
    /// <c>bge-micro-v2</c> for the embedded model; the virtual path of
    /// <see cref="LocalEmbeddingOptions.ModelPath"/> when one is set.
    /// </summary>
    public string ModelName => string.IsNullOrEmpty(_options.ModelPath) ? EmbeddedModelName : _options.ModelPath;

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct)
    {
        // Mandatory before anything reaches _embedder: the wrapped LocalEmbedder owns a
        // native ONNX session, and calling into it after Dispose() dereferences freed
        // memory. That is undefined behaviour, not an exception -- observed as a bogus
        // OnnxRuntimeException on a warm heap and as a process-killing SIGSEGV on a
        // dirty one.
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([]);

        ct.ThrowIfCancellationRequested();

        var embedder = GetEmbedder();
        var truncated = ApplyTruncation(texts);
        var results = new ReadOnlyMemory<float>[truncated.Count];

        var po = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(1, _options.MaxConcurrency),
        };

        // SmartComponents.LocalEmbeddings.LocalEmbedder is documented as thread-safe
        // for parallel reads (SPEC §6.2). The .ToArray() copy below is mandatory:
        // EmbeddingF32.Values is a ReadOnlyMemory<float> over an internal buffer that
        // may be reused across calls — without the defensive copy, parallel writers
        // could observe each other's bytes.
        Parallel.For(0, truncated.Count, po, i =>
        {
            var emb = embedder.Embed(truncated[i]);
            results[i] = emb.Values.ToArray();
        });

        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(results);
    }

    private IReadOnlyList<string> ApplyTruncation(IReadOnlyList<string> texts)
    {
        if (_options.MaxTextChars is not { } cap || cap <= 0) return texts;

        var truncated = new string[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            var t = texts[i] ?? string.Empty;
            truncated[i] = t.Length > cap ? t[..cap] : t;
        }
        return truncated;
    }

    private int ProbeDimensions()
    {
        var probe = GetEmbedder().Embed("probe");
        return probe.Values.Length;
    }

    /// <summary>
    /// Releases the underlying ONNX session held by the wrapped <see cref="LocalEmbedder"/>.
    /// Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_embedderLock)
        {
            // An attempt still loading is released by the call that loads it (GetEmbedder).
            if (_attempt is { IsValueCreated: true })
            {
                _attempt.Value.Dispose();
                _attempt = null;
            }
        }
    }
}
