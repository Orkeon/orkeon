using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>What the stored vectors came from: provider, model and dimensions.</summary>
internal sealed record EmbeddingFingerprint(string Provider, string Model, int Dimensions);

/// <summary>Remembers the <see cref="EmbeddingFingerprint"/> between runs.</summary>
internal interface IEmbeddingFingerprintStore
{
    /// <summary>Loads the recorded fingerprint, or null when none was recorded.</summary>
    System.Threading.Tasks.Task<EmbeddingFingerprint?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Records a fingerprint.</summary>
    System.Threading.Tasks.Task SaveAsync(EmbeddingFingerprint fingerprint, CancellationToken cancellationToken);
}

/// <summary>The embedding model changed since vectors were stored.</summary>
internal sealed class EmbeddingModelChangedException : InvalidOperationException
{
    /// <summary>Initializes the exception.</summary>
    public EmbeddingModelChangedException(string message) : base(message) { }

    /// <summary>Initializes the exception.</summary>
    public EmbeddingModelChangedException() { }

    /// <summary>Initializes the exception.</summary>
    public EmbeddingModelChangedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The startup guard of the embedding model: vectors stored under one model mean nothing under another,
/// so a change of provider, model or dimensions is refused, never migrated silently.
/// </summary>
internal static class EmbeddingDimensionGuard
{
    /// <summary>
    /// Records the provider's fingerprint when none exists, passes when it matches, and throws
    /// <see cref="EmbeddingModelChangedException"/> when it differs. A host with no provider is skipped.
    /// </summary>
    public static async System.Threading.Tasks.Task CheckAsync(
        IEmbeddingProvider provider, IEmbeddingFingerprintStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(store);
        if (provider.Dimensions <= 0)
            return;

        var current = new EmbeddingFingerprint(provider.Name, provider.Model, provider.Dimensions);
        var stored = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            await store.SaveAsync(current, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (stored == current)
            return;

        var before = stored.Dimensions.ToString(CultureInfo.InvariantCulture);
        var after = current.Dimensions.ToString(CultureInfo.InvariantCulture);
        throw new EmbeddingModelChangedException(
            $"The embedding model changed: vectors were stored with {stored.Provider} '{stored.Model}' " +
            $"({before} dimensions) and Orkeon:Embeddings now resolves to {current.Provider} " +
            $"'{current.Model}' ({after} dimensions). Stored vectors are not comparable across models: " +
            "restore the previous Orkeon:Embeddings settings, or re-embed and clear the stored vectors, then delete " +
            "the fingerprint file (Orkeon:Embeddings:FingerprintPath) so the new model is recorded.");
    }
}

/// <summary>An <see cref="IEmbeddingFingerprintStore"/> kept in one JSON file of the virtual file system.</summary>
internal sealed class FileEmbeddingFingerprintStore : IEmbeddingFingerprintStore
{
    private readonly IFileSystemService _fileSystem;
    private readonly string _path;

    /// <summary>Initializes the store.</summary>
    public FileEmbeddingFingerprintStore(IFileSystemService fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _fileSystem = fileSystem;
        _path = path;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<EmbeddingFingerprint?> LoadAsync(CancellationToken cancellationToken)
    {
        var text = await _fileSystem.TryReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<EmbeddingFingerprint>(text);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task SaveAsync(EmbeddingFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var slash = _path.LastIndexOf('/');
        if (slash > 0)
            await _fileSystem.CreateDirectoryAsync(_path[..slash], cancellationToken).ConfigureAwait(false);
        await _fileSystem.WriteAllTextAsync(_path, JsonSerializer.Serialize(fingerprint), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Runs the embedding model guard (FWK-25) for a host whose entry point does not start the host: the
/// runners (<c>orkeon run</c>, the other commands) and the REPL build a host and never start its hosted
/// services, so each calls this once, right after it built the host.
/// </summary>
public static class EmbeddingModelGuard
{
    /// <summary>
    /// Checks the embedding model against the fingerprint recorded for the host's vectors, and records it
    /// when none exists. A host that wrote no <c>Orkeon:Embeddings</c> section is not guarded.
    /// </summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The refusal to show when the model changed since vectors were stored; null when the host may go on.</returns>
    public static async System.Threading.Tasks.Task<string?> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        try
        {
            await EmbeddingDimensionGuardRunner.RunAsync(services, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (EmbeddingModelChangedException ex)
        {
            return ex.Message;
        }
    }
}

/// <summary>The body of the guard, shared by the hosted service and <see cref="EmbeddingModelGuard"/>.</summary>
internal static partial class EmbeddingDimensionGuardRunner
{
    public static async System.Threading.Tasks.Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(EmbeddingDimensionGuardRunner).FullName!);
        var store = services.GetService<IEmbeddingFingerprintStore>() ?? TryCreateFileStore(services);
        if (store is null)
            return;

        try
        {
            var provider = services.GetRequiredService<IEmbeddingProvider>();
            await EmbeddingDimensionGuard.CheckAsync(provider, store, cancellationToken).ConfigureAwait(false);
        }
        catch (EmbeddingModelChangedException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or NotSupportedException or FileAccessDeniedException)
        {
            if (logger is not null)
                LogGuardSkipped(logger, ex.Message);
        }
    }

    private static FileEmbeddingFingerprintStore? TryCreateFileStore(IServiceProvider services)
    {
        // Only a host that wrote an Orkeon:Embeddings section chose its embedding model: the others
        // keep the on-device default and get no file written into their workspace.
        var section = services.GetService<IConfiguration>()?.GetSection(DefaultEmbeddingProviderResolver.EmbeddingsSectionKey);
        if (section is null || !section.Exists())
            return null;

        var path = services.GetService<IOptions<EmbeddingOptions>>()?.Value.FingerprintPath;
        var fileSystem = services.GetService<IFileSystemService>();
        return string.IsNullOrWhiteSpace(path) || fileSystem is null
            ? null
            : new FileEmbeddingFingerprintStore(fileSystem, path);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "The embedding model guard could not read or write its fingerprint, the check is skipped: {Reason}")]
    private static partial void LogGuardSkipped(ILogger logger, string reason);
}

/// <summary>Runs <see cref="EmbeddingDimensionGuard"/> when a host that starts its hosted services starts.</summary>
internal sealed class EmbeddingDimensionGuardHostedService : IHostedService
{
    private readonly IServiceProvider _services;

    public EmbeddingDimensionGuardHostedService(IServiceProvider services)
    {
        _services = services;
    }

    public System.Threading.Tasks.Task StartAsync(CancellationToken cancellationToken) =>
        EmbeddingDimensionGuardRunner.RunAsync(_services, cancellationToken);

    public System.Threading.Tasks.Task StopAsync(CancellationToken cancellationToken) =>
        System.Threading.Tasks.Task.CompletedTask;
}
