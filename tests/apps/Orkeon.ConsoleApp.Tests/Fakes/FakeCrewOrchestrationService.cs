using CrewId = Orkeon.Domain.Common.CrewId;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.ConsoleApp.Tests.Fakes;

public sealed class FakeCrewOrchestrationService : ICrewOrchestrationService
{
    public List<(CrewId CrewId, CrewInput Input)> Calls { get; } = new();
    public Func<CrewInput, CrewOutput>? OutputFactory { get; set; }
    public Exception? ThrowOnKickoff { get; set; }

    public System.Threading.Tasks.Task<CrewOutput> KickoffAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default)
    {
        Calls.Add((crewId, input));
        if (ThrowOnKickoff is not null) throw ThrowOnKickoff;
        var output = OutputFactory?.Invoke(input)
            ?? new CrewOutput(
                FinalOutput: $"answer to: {input.InitialContext}",
                TaskOutputs: Array.Empty<Orkeon.Application.Execution.TaskOutput>(),
                Duration: TimeSpan.FromSeconds(0.5),
                TokensUsed: new TokenUsage(10, 20, 30));
        return System.Threading.Tasks.Task.FromResult(output);
    }

    public System.Threading.Tasks.Task<BatchOutput> KickoffForEachAsync(CrewId crewId, IEnumerable<CrewInput> inputs, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public System.Threading.Tasks.Task<CrewExecutionId> KickoffAsyncNoWait(CrewId crewId, CrewInput input) => throw new NotImplementedException();
    public System.Threading.Tasks.Task<CrewExecutionStatus> GetExecutionStatusAsync(CrewExecutionId executionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
