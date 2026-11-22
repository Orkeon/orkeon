using System.Collections.Concurrent;
using Orkeon.Application.Crew.Routing;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary>An <see cref="ISystemOnePicker"/> that answers every pick with <see cref="Answer"/> and keeps the requests and tokens.</summary>
internal sealed class StubSystemOnePicker : ISystemOnePicker
{
    /// <summary>The answer given to every pick.</summary>
    public SystemOnePickResponse Answer { get; set; } = new()
    {
        Effort = new SystemOnePickAnswer { Value = "low", Reason = SystemOnePickReasons.Model, Picked = "low", Probability = 0.75 },
    };

    /// <summary>The requests received, oldest first.</summary>
    public ConcurrentQueue<SystemOnePickRequest> Requests { get; } = new();

    /// <summary>The tokens the picks followed, oldest first.</summary>
    public ConcurrentQueue<CancellationToken> Tokens { get; } = new();

    /// <inheritdoc />
    public System.Threading.Tasks.Task<SystemOnePickResponse> PickAsync(SystemOnePickRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        Tokens.Enqueue(cancellationToken);
        return System.Threading.Tasks.Task.FromResult(Answer);
    }
}
