using System.Collections.Concurrent;
using Orkeon.Application.Crew.Routing;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>An <see cref="ISystemOnePicker"/> that answers every pick with the effort <c>low</c> and keeps the requests.</summary>
internal sealed class StubSystemOnePicker : ISystemOnePicker
{
    /// <summary>The requests received, oldest first.</summary>
    public ConcurrentQueue<SystemOnePickRequest> Requests { get; } = new();

    /// <inheritdoc />
    public System.Threading.Tasks.Task<SystemOnePickResponse> PickAsync(SystemOnePickRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        return System.Threading.Tasks.Task.FromResult(new SystemOnePickResponse
        {
            Effort = new SystemOnePickAnswer { Value = "low", Reason = SystemOnePickReasons.Model },
        });
    }
}
