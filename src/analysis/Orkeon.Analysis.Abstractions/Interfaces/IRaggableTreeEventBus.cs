using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.Interfaces;

public interface IRaggableTreeEventBus
{
    void Publish(RaggableTreeUpdated evt);
    IDisposable Subscribe(Func<RaggableTreeUpdated, CancellationToken, Task> handler);
}

public sealed record RaggableTreeUpdated(
    string PreviousIndexId,
    string NewIndexId,
    ImmutableArray<string> AddedFqns,
    ImmutableArray<string> RemovedFqns,
    ImmutableArray<string> ModifiedFqns);
