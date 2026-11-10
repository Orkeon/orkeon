using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Runs;
using Orkeon.Infrastructure.Checkpointing;

namespace Orkeon.Host.Api;

/// <summary>
/// The services <see cref="HostApiServer"/> uses when the host registers them: each one is optional, and the container
/// passes the default for a service it does not hold. Kept apart so the server's constructor names its required
/// dependencies alone.
/// </summary>
internal sealed class HostApiOptionalServices
{
    public HostApiOptionalServices(
        IRunHistoryReader? history = null,
        IEnumerable<IAuthenticationProvider>? bearerProviders = null,
        ISecretProvider? secrets = null,
        TimeProvider? time = null,
        ICheckpointManager? checkpoints = null,
        IPendingInputBroker? broker = null,
        IOptions<CheckpointingOptions>? checkpointing = null)
    {
        History = history;
        BearerProviders = bearerProviders?.ToList() ?? [];
        Secrets = secrets;
        Time = time ?? TimeProvider.System;
        Broker = broker;
        Input = checkpointing?.Value.Enabled == true ? checkpoints as IPendingInputStore : null;
    }

    public IRunHistoryReader? History { get; }

    public IReadOnlyList<IAuthenticationProvider> BearerProviders { get; }

    public ISecretProvider? Secrets { get; }

    public TimeProvider Time { get; }

    public IPendingInputBroker? Broker { get; }

    /// <summary>
    /// Where a run's questions are persisted: only with Orkeon:Checkpointing:Enabled (the durable human input engages on
    /// it). The checkpoint manager is registered either way, so its presence alone says nothing.
    /// </summary>
    public IPendingInputStore? Input { get; }
}
