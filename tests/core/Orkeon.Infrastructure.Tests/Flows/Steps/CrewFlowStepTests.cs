using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Flows;
using Orkeon.Domain.Flows.ValueObjects;
using Orkeon.Infrastructure.Flows.Steps;

namespace Orkeon.Infrastructure.Tests.Flows.Steps;

/// <summary>
/// SONAR-14: pins the crew-executing flow step — parameter validation, the kickoff
/// round-trip with output/duration mapping, and the crew-config resolution order
/// (step parameters first, then the typed input).
/// </summary>
public class CrewFlowStepTests
{
    /// <summary>Scripted orchestration service recording the kickoff it receives.</summary>
    private sealed class ScriptedCrewOrchestrationService : ICrewOrchestrationService
    {
        public CrewOutput Output { get; set; } =
            new("crew says hi", [], TimeSpan.FromSeconds(2.5), TokensUsed: null);

        public CrewId? ReceivedCrewId { get; private set; }
        public CrewInput? ReceivedInput { get; private set; }

        public System.Threading.Tasks.Task<CrewOutput> KickoffAsync(
            CrewId crewId, CrewInput input, CancellationToken cancellationToken = default)
        {
            ReceivedCrewId = crewId;
            ReceivedInput = input;
            return System.Threading.Tasks.Task.FromResult(Output);
        }

        public System.Threading.Tasks.Task<BatchOutput> KickoffForEachAsync(
            CrewId crewId, IEnumerable<CrewInput> inputs, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public System.Threading.Tasks.Task<CrewExecutionId> KickoffAsyncNoWait(
            CrewId crewId, CrewInput input) =>
            throw new NotSupportedException();

        public System.Threading.Tasks.Task<CrewExecutionStatus> GetExecutionStatusAsync(
            CrewExecutionId executionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(
            CrewId crewId, CrewInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static CrewFlowStep BuildStep(
        ScriptedCrewOrchestrationService service,
        Dictionary<string, object>? parameters = null,
        string name = "run_crew") =>
        new(name, FlowStepParameters.FromDictionary(parameters), service);

    [Fact]
    public async Task ExecutesTheCrew_AndMapsOutputAndDuration()
    {
        var service = new ScriptedCrewOrchestrationService();
        var step = BuildStep(service, new Dictionary<string, object> { ["crew_config"] = "research-crew" });

        var result = await step.ExecuteAsync(FlowState.Empty, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var output = Assert.IsType<CrewFlowStepOutput>(result.Output);
        Assert.Equal("crew says hi", output.CrewOutput);
        Assert.Equal(2.5, output.CrewDuration);
        Assert.Equal("research-crew", service.ReceivedInput!.InitialContext);
        Assert.NotNull(service.ReceivedCrewId);
    }

    [Fact]
    public async Task UsesAParsableCrewConfig_AsTheCrewId()
    {
        var guid = Guid.NewGuid();
        var service = new ScriptedCrewOrchestrationService();
        var step = BuildStep(service, new Dictionary<string, object> { ["crew_config"] = guid.ToString() });

        await step.ExecuteAsync(FlowState.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(CrewId.From(guid), service.ReceivedCrewId);
    }

    [Fact]
    public async Task FallsBackToTheTypedInput_WhenTheParameterIsAbsent()
    {
        var service = new ScriptedCrewOrchestrationService();
        var step = BuildStep(service, parameters: null);
        var state = FlowState.Empty.Set("crew_config", "from-state");

        var result = await step.ExecuteAsync(state, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("from-state", service.ReceivedInput!.InitialContext);
        // The variable is threaded through to the crew input as well.
        Assert.Equal("from-state", service.ReceivedInput.Variables["crew_config"]);
    }

    [Fact]
    public async Task FailsValidation_WhenNoCrewConfigIsAvailable()
    {
        var service = new ScriptedCrewOrchestrationService();
        var step = BuildStep(service, parameters: null);

        var result = await step.ExecuteAsync(FlowState.Empty, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("crew_config", result.Error, StringComparison.Ordinal);
        Assert.Null(service.ReceivedCrewId);
    }

    [Fact]
    public void ExposesNameAndDescription_FromItsParameters()
    {
        var service = new ScriptedCrewOrchestrationService();
        var step = BuildStep(service, new Dictionary<string, object> { ["crew_config"] = "demo" }, name: "my_step");

        Assert.Equal("my_step", step.Name);
        Assert.Contains("demo", step.Description, StringComparison.Ordinal);

        var bare = BuildStep(service, parameters: null);
        Assert.Contains("unknown", bare.Description, StringComparison.Ordinal);

        Assert.Throws<ArgumentNullException>(() =>
            new CrewFlowStep("x", FlowStepParameters.FromDictionary(null), null!));
    }
}
