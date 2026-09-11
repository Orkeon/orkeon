using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
namespace Orkeon.Infrastructure.Tests.Telemetry;

public sealed class OrkeonMetricsTests : IDisposable
{
    private readonly OrkeonMetricsTestsFixture _fixture = new();

    [Fact]
    public void ShouldIncrementCounter_WhenRecordingLlmCall()
    {
        _fixture.RecordLlmCall("OpenAI", ModelGpt4, durationMs: 150.0, promptTokens: 100, completionTokens: 50);

        Assert.True(_fixture.HasMeasurement("orkeon.llm.calls"));
        Assert.Single(_fixture.GetMeasurement("orkeon.llm.calls"));
        Assert.Equal(1L, _fixture.GetMeasurement("orkeon.llm.calls")[0]);
    }

    [Fact]
    public void ShouldRecordTokens_WhenRecordingLlmCall()
    {
        _fixture.RecordLlmCall("OpenAI", ModelGpt4, durationMs: 150.0, promptTokens: 100, completionTokens: 50);

        // gen_ai.client.token.usage: one sample per token type (input, output), never a sum.
        Assert.True(_fixture.HasMeasurement("gen_ai.client.token.usage"));
        Assert.Equal(2, _fixture.GetMeasurement("gen_ai.client.token.usage").Count);
        Assert.Equal(100L, _fixture.GetMeasurement("gen_ai.client.token.usage")[0]);
        Assert.Equal(50L, _fixture.GetMeasurement("gen_ai.client.token.usage")[1]);
    }

    [Fact]
    public void ShouldRecordDuration_WhenRecordingLlmCall()
    {
        _fixture.RecordLlmCall("OpenAI", ModelGpt4, durationMs: 250.5);

        // gen_ai.client.operation.duration is in seconds, as the convention says.
        Assert.True(_fixture.HasMeasurement("gen_ai.client.operation.duration"));
        Assert.Single(_fixture.GetMeasurement("gen_ai.client.operation.duration"));
        Assert.Equal(0.2505, Assert.IsType<double>(_fixture.GetMeasurement("gen_ai.client.operation.duration")[0]), precision: 6);
    }

    [Fact]
    public void ShouldRecordHistogram_WhenRecordingToolExecution()
    {
        _fixture.RecordToolExecution(ToolFileRead, durationMs: 50.0, success: true, agentRole: "Researcher");

        Assert.True(_fixture.HasMeasurement("orkeon.tool.executions"));
        Assert.Single(_fixture.GetMeasurement("orkeon.tool.executions"));
        Assert.True(_fixture.HasMeasurement("orkeon.tool.duration"));
        Assert.Equal(50.0, _fixture.GetMeasurement("orkeon.tool.duration")[0]);
    }

    [Fact]
    public void ShouldRecordCounterAndHistogram_WhenRecordingTaskExecution()
    {
        _fixture.RecordTaskExecution(TaskId1, durationMs: 1000.0, success: true, agentRole: "Writer");

        Assert.True(_fixture.HasMeasurement("orkeon.task.executions"));
        Assert.Equal(1L, _fixture.GetMeasurement("orkeon.task.executions")[0]);
        Assert.True(_fixture.HasMeasurement("orkeon.task.duration"));
        Assert.Equal(1000.0, _fixture.GetMeasurement("orkeon.task.duration")[0]);
    }

    [Fact]
    public void ShouldRecordCounterAndHistogram_WhenRecordingCrewExecution()
    {
        _fixture.RecordCrewExecution(CrewId1, durationMs: 5000.0, success: true, processType: "sequential");

        Assert.True(_fixture.HasMeasurement("orkeon.crew.executions"));
        Assert.Equal(1L, _fixture.GetMeasurement("orkeon.crew.executions")[0]);
        Assert.True(_fixture.HasMeasurement("orkeon.crew.duration"));
        Assert.Equal(5000.0, _fixture.GetMeasurement("orkeon.crew.duration")[0]);
    }

    [Fact]
    public void ShouldGoUpAndDown_WhenTrackingActiveCrews()
    {
        _fixture
            .CrewStarted(CrewId1)
            .CrewStarted(CrewId2)
            .CrewCompleted(CrewId1)
            .FlushObservable();

        Assert.True(_fixture.HasMeasurement("orkeon.crew.active"));
        Assert.Equal(3, _fixture.GetMeasurement("orkeon.crew.active").Count);
    }

    [Fact]
    public void ShouldRecordUpDownCounter_WhenTaskStartsAndCompletes()
    {
        _fixture
            .TaskStarted(TaskId1)
            .TaskCompleted(TaskId1)
            .FlushObservable();

        Assert.True(_fixture.HasMeasurement("orkeon.task.active"));
        Assert.Equal(2, _fixture.GetMeasurement("orkeon.task.active").Count);
        Assert.Equal(1L, _fixture.GetMeasurement("orkeon.task.active")[0]);
        Assert.Equal(-1L, _fixture.GetMeasurement("orkeon.task.active")[1]);
    }

    [Fact]
    public void ShouldRecordUpDownCounter_WhenLlmCallStartsAndCompletes()
    {
        _fixture
            .LlmCallStarted("OpenAI")
            .LlmCallCompleted("OpenAI")
            .FlushObservable();

        Assert.True(_fixture.HasMeasurement("orkeon.llm.active"));
        Assert.Equal(2, _fixture.GetMeasurement("orkeon.llm.active").Count);
    }

    [Fact]
    public void ShouldAccumulateCost_WhenMultipleLlmCallsAreMade()
    {
        _fixture
            .RecordLlmCall("OpenAI", ModelGpt4, durationMs: 100, costUsd: 0.01)
            .RecordLlmCall("OpenAI", ModelGpt4, durationMs: 100, costUsd: 0.02)
            .RecordLlmCall("OpenAI", ModelGpt4, durationMs: 100, costUsd: 0.03);

        Assert.True(_fixture.HasMeasurement("orkeon.cost.total_usd"));
        var totalCost = _fixture.GetMeasurement("orkeon.cost.total_usd").Cast<double>().Last();
        Assert.True(Math.Abs(totalCost - 0.06) <= 0.001);
    }

    [Fact]
    public void ShouldIncrementWithEventType_WhenRecordingSecurityEvent()
    {
        _fixture
            .RecordSecurityEvent("prompt_injection_blocked")
            .RecordSecurityEvent("ssrf_blocked")
            .RecordSecurityEvent("prompt_injection_blocked")
            .FlushObservable();

        Assert.True(_fixture.HasMeasurement("orkeon.security.events"));
        Assert.Equal(3, _fixture.GetMeasurement("orkeon.security.events").Count);
    }

    [Fact]
    public void ShouldCleanUpMeter_WhenDisposing()
    {
        var metrics = new Infrastructure.Telemetry.OrkeonMetrics();

        metrics.Dispose();

        var exception = Record.Exception(() => metrics.RecordLlmCall("OpenAI", ModelGpt4, 100));
        Assert.Null(exception);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
