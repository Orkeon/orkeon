namespace Orkeon.Application.Memory;

/// <summary>
/// Adapter to convert Application IEmbeddingService to Domain IEmbeddingService.
/// </summary>
public sealed class DomainEmbeddingServiceAdapter : Domain.Memory.IEmbeddingService
{
    private readonly Interfaces.Ports.IEmbeddingService _applicationService;

    /// <summary>
    /// Initializes a new instance of <see cref="DomainEmbeddingServiceAdapter"/>.
    /// </summary>
    public DomainEmbeddingServiceAdapter(Interfaces.Ports.IEmbeddingService applicationService)
    {
        ArgumentNullException.ThrowIfNull(applicationService);
        _applicationService = applicationService;
    }

    /// <summary>
    /// Get Embedding Async.
    /// </summary>
    public async System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        // Application service doesn't take cancellation token, so we just call it
        return await _applicationService.GetEmbeddingAsync(text).ConfigureAwait(false);
    }
}
