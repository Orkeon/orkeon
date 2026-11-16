using Orkeon.Application.Crew.DeliverableResolvers;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// <see cref="IDeliverableResolverFactory"/> that hands out one resolver for every framework-persisted
/// source (<c>final_message</c>, <c>structured_output</c>) and none for <c>tool_call</c>, as the
/// production factory does. The resolver answers <see cref="Persisted"/> at the task's path.
/// </summary>
internal sealed class StubDeliverableResolverFactory : IDeliverableResolverFactory
{
    /// <summary>Whether the resolver reports the deliverable as written.</summary>
    public bool Persisted { get; set; } = true;

    /// <summary>How many times the resolver ran.</summary>
    public int Calls { get; private set; }

    public IDeliverableResolver? GetFor(DeliverableSource source) =>
        source is DeliverableSource.FinalMessage or DeliverableSource.StructuredOutput ? new Resolver(this, source) : null;

    private sealed class Resolver(StubDeliverableResolverFactory owner, DeliverableSource source) : IDeliverableResolver
    {
        public DeliverableSource SupportedSource => source;

        public System.Threading.Tasks.Task<DeliverableResolutionResult> ResolveAsync(
            CrewTask task, string finalAssistantMessage, CancellationToken ct)
        {
            owner.Calls++;
            return System.Threading.Tasks.Task.FromResult(new DeliverableResolutionResult(
                owner.Persisted, task.Deliverable?.Path, finalAssistantMessage.Length, source,
                owner.Persisted ? null : "stub refused"));
        }
    }
}
