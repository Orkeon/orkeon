using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Application.Services.Security;

/// <summary>An <see cref="IToolApprovalChannel"/> decorator that lets one prompt through at a time.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The semaphore never exposes its wait handle, so it holds no unmanaged resource; the channel lives as long as the host.")]
public sealed class SerialisingToolApprovalChannel : IToolApprovalChannel
{
    private readonly IToolApprovalChannel _inner;
    private readonly SemaphoreSlim _turn = new(1, 1);

    /// <summary>Initializes a new instance of <see cref="SerialisingToolApprovalChannel"/>.</summary>
    /// <param name="inner">The channel the requests are forwarded to.</param>
    public SerialisingToolApprovalChannel(IToolApprovalChannel inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<ToolApprovalDecision> RequestAsync(
        ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        await _turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _inner.RequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }
}
