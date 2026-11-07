using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Hosting;

/// <summary>
/// The identity a command gave its run before the host kicked the crew off: <c>orkeon run --events</c>
/// opens the run's <see cref="IRunScope"/> before <c>run.started</c>, so that every event of the stream,
/// the lifecycle ones included, carries one <c>correlationId</c>. The kickoff must adopt that identifier,
/// not mint a second one.
/// </summary>
internal static class RunnerRunIdentity
{
    /// <summary>The kickoff options carrying the ambient run's identifier, or null when no command opened a run.</summary>
    internal static KickoffOptions? Ambient(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetService<IRunScope>()?.Current is { } run
            ? new KickoffOptions { ExecutionId = run.ExecutionId, Resume = run.Resume }
            : null;
    }
}
