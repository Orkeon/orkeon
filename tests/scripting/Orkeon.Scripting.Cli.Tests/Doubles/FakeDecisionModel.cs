using System.Collections.Immutable;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>A decision model that is never asked: its registration alone arms the circuit.</summary>
internal sealed class FakeDecisionModel : IDecisionModel
{
    public string Name => "fake";

    public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
