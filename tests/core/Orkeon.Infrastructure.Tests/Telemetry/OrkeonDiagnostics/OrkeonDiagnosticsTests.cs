namespace Orkeon.Infrastructure.Tests.Telemetry;

public class OrkeonDiagnosticsTests
{
    [Fact]
    public void ShouldHaveCorrectName_WhenAccessingCrewSource()
    {
        Assert.Equal("Orkeon.Crew", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.CrewSource.Name);
        Assert.Equal("1.0.0", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.CrewSource.Version);
    }

    [Fact]
    public void ShouldHaveCorrectName_WhenAccessingAgentSource()
    {
        Assert.Equal("Orkeon.Agent", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.AgentSource.Name);
        Assert.Equal("1.0.0", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.AgentSource.Version);
    }

    [Fact]
    public void ShouldHaveCorrectName_WhenAccessingTaskSource()
    {
        Assert.Equal("Orkeon.Task", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.TaskSource.Name);
        Assert.Equal("1.0.0", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.TaskSource.Version);
    }

    [Fact]
    public void ShouldHaveCorrectName_WhenAccessingLlmSource()
    {
        Assert.Equal("Orkeon.Llm", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.LlmSource.Name);
        Assert.Equal("1.0.0", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.LlmSource.Version);
    }

    [Fact]
    public void ShouldHaveCorrectName_WhenAccessingToolSource()
    {
        Assert.Equal("Orkeon.Tool", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.ToolSource.Name);
        Assert.Equal("1.0.0", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.ToolSource.Version);
    }

    [Fact]
    public void ShouldHaveCorrectName_WhenAccessingMemorySource()
    {
        Assert.Equal("Orkeon.Memory", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.MemorySource.Name);
        Assert.Equal("1.0.0", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.MemorySource.Version);
    }

    [Fact]
    public void ShouldContainEverySource_WhenAccessingAllSourceNames()
    {
        Assert.Equal(7, global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.AllSourceNames.Length);
        Assert.Contains("Orkeon.Crew", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.AllSourceNames);
        Assert.Contains("Orkeon.Agent", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.AllSourceNames);
        Assert.Contains("Orkeon.Task", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.AllSourceNames);
        Assert.Contains("Orkeon.Llm", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.AllSourceNames);
        Assert.Contains("Orkeon.Tool", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.AllSourceNames);
        Assert.Contains("Orkeon.Memory", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.AllSourceNames);
        Assert.Contains("Orkeon.EventHub", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.AllSourceNames);
    }

    [Fact]
    public void ShouldBeOrkeonNET_WhenAccessingServiceName()
    {
        Assert.Equal("Orkeon", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.ServiceName);
    }

    [Fact]
    public void ShouldBe100_WhenAccessingServiceVersion()
    {
        Assert.Equal("1.0.0", global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnostics.ServiceVersion);
    }

    [Theory]
    [InlineData(nameof(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.CrewId), "orkeon.crew.id")]
    [InlineData(nameof(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.CrewName), "orkeon.crew.name")]
    [InlineData(nameof(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.AgentRole), "gen_ai.agent.name")]
    [InlineData(nameof(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmProvider), "gen_ai.provider.name")]
    [InlineData(nameof(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmModel), "gen_ai.request.model")]
    [InlineData(nameof(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.ToolName), "gen_ai.tool.name")]
    [InlineData(nameof(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.MemoryOperation), "orkeon.memory.operation")]
    [InlineData(nameof(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.TaskId), "orkeon.task.id")]
    public void ShouldHaveExpectedValues_WhenAccessingTags(string fieldName, string expectedValue)
    {
        var field = typeof(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags).GetField(fieldName);
        Assert.NotNull(field);
        var value = field!.GetValue(null) as string;
        Assert.Equal(expectedValue, value);
    }

    [Fact]
    public void ShouldNotBeNullOrEmpty_WhenAccessingAllTagConstants()
    {
        var tagFields = typeof(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        Assert.NotEmpty(tagFields);

        foreach (var field in tagFields)
        {
            var value = field.GetValue(null) as string;
            Assert.False(string.IsNullOrEmpty(value), $"Tag {field.Name} should have a value");
        }
    }
}
