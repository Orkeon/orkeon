using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Infrastructure.HumanInput;

/// <summary>
/// The approval of a tool call, made durable (FWK-23): the permission gate's question is persisted with the run's
/// checkpoint session like any question to a human, and a run with nobody to answer it suspends instead of refusing.
/// It decorates the host's own <see cref="IToolApprovalChannel"/> when it has one (a terminal asks, the answer is recorded)
/// and stands alone when it has none. A call that is not inside a task of a checkpointed run goes to the inner channel, or
/// is denied: the gate fails closed.
/// </summary>
public sealed class DurableToolApprovalChannel : IToolApprovalChannel
{
    private const string Allow = "allow";
    private const string AllowForSession = "allow_for_session";
    private const string Deny = "deny";

    private readonly IToolApprovalChannel? _inner;
    private readonly DurableHumanInputProvider _asker;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="inner">The channel that asks a person, when the host has one.</param>
    /// <param name="runScope">The ambient run.</param>
    /// <param name="store">Where the question is kept.</param>
    /// <param name="broker">Where a held run meets its answer.</param>
    /// <param name="options">The pause policy.</param>
    /// <param name="time">The clock of the hold timeout.</param>
    /// <param name="logger">The logger.</param>
    public DurableToolApprovalChannel(
        IToolApprovalChannel? inner,
        IRunScope runScope,
        IPendingInputStore store,
        IPendingInputBroker broker,
        IOptions<HumanInputPauseOptions> options,
        TimeProvider time,
        ILogger<DurableHumanInputProvider> logger)
    {
        _inner = inner;
        _asker = new DurableHumanInputProvider(new NoPerson(inner is not null), runScope, store, broker, options, time, logger);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<ToolApprovalDecision> RequestAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = await _asker.AskForApprovalAsync(
            $"Allow tool '{request.ToolName}'? {request.Reason}",
            request.ToolName,
            ArgumentsHash.Of(request.Arguments),
            [Allow, AllowForSession, Deny],
            async (_, t) => Wire(await AskInnerAsync(request, t).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);

        return answer?.Trim().ToUpperInvariant() switch
        {
            "ALLOW" or "Y" or "YES" or "OUI" or "TRUE" => ToolApprovalDecision.AllowOnce,
            "ALLOW_FOR_SESSION" => ToolApprovalDecision.AllowForSession,
            _ => ToolApprovalDecision.Deny,
        };
    }

    private System.Threading.Tasks.Task<ToolApprovalDecision> AskInnerAsync(ToolApprovalRequest request, CancellationToken ct) =>
        _inner is null ? System.Threading.Tasks.Task.FromResult(ToolApprovalDecision.Deny) : _inner.RequestAsync(request, ct);

    private static string Wire(ToolApprovalDecision decision) => decision switch
    {
        ToolApprovalDecision.AllowOnce => Allow,
        ToolApprovalDecision.AllowForSession => AllowForSession,
        _ => Deny,
    };
}
