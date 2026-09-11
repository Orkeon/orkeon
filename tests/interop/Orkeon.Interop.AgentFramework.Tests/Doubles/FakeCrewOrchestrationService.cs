using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;

namespace Orkeon.Interop.AgentFramework.Tests.Doubles;

/// <summary>Records every kickoff and answers with a scripted output.</summary>
public sealed class FakeCrewOrchestrationService : ICrewOrchestrationService
{
    public List<(CrewId CrewId, CrewInput Input)> Kickoffs { get; } = [];

    public string FinalOutput { get; set; } = "crew output";

    public TokenUsage? TokensUsed { get; set; }

    public bool Succeeded { get; set; } = true;

    public Task<CrewOutput> KickoffAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default)
    {
        Kickoffs.Add((crewId, input));
        return Task.FromResult(new CrewOutput(FinalOutput, [], TimeSpan.FromMilliseconds(5), TokensUsed) { Succeeded = Succeeded });
    }

    public Task<BatchOutput> KickoffForEachAsync(CrewId crewId, IEnumerable<CrewInput> inputs, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<CrewExecutionId> KickoffAsyncNoWait(CrewId crewId, CrewInput input) =>
        throw new NotSupportedException();

    public Task<CrewExecutionStatus> GetExecutionStatusAsync(CrewExecutionId executionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
