using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

/// <summary>
/// Regression coverage for Experiment 07 friction #2: legacy crew YAML uses snake_case
/// (<c>expected_output:</c>, <c>output_file:</c>, <c>async_execution:</c>, …) and may carry
/// forward-compatible extra keys. The Orkeon C# loader must accept both conventions and
/// ignore unknown properties so that authoring in the snake_case dialect does not crash the loader.
/// </summary>
public class YamlSnakeCaseToleranceTests
{
    private readonly YamlDotNetSerializer _serializer = new();

    [Fact]
    public void Deserialize_ShouldKeepCamelCaseAsCanonicalForm()
    {
        var yaml = """
expectedOutput: A markdown summary
outputFile: report.md
asyncExecution: true
humanInput: false
""";

        var result = _serializer.Deserialize<TaskYamlShape>(yaml);

        Assert.Equal("A markdown summary", result.ExpectedOutput);
        Assert.Equal("report.md", result.OutputFile);
        Assert.True(result.AsyncExecution);
        Assert.False(result.HumanInput);
    }

    [Fact]
    public void Deserialize_ShouldAcceptSnakeCaseAlias_ForKnownProperty()
    {
        var yaml = """
expected_output: A markdown summary
output_file: report.md
async_execution: true
human_input: false
""";

        var result = _serializer.Deserialize<TaskYamlShape>(yaml);

        Assert.Equal("A markdown summary", result.ExpectedOutput);
        Assert.Equal("report.md", result.OutputFile);
        Assert.True(result.AsyncExecution);
        Assert.False(result.HumanInput);
    }

    [Fact]
    public void Deserialize_ShouldAcceptMixedCamelAndSnakeCase_InTheSameDocument()
    {
        var yaml = """
expectedOutput: mixed-mode
output_file: mixed.md
asyncExecution: true
human_input: false
""";

        var result = _serializer.Deserialize<TaskYamlShape>(yaml);

        Assert.Equal("mixed-mode", result.ExpectedOutput);
        Assert.Equal("mixed.md", result.OutputFile);
        Assert.True(result.AsyncExecution);
        Assert.False(result.HumanInput);
    }

    [Fact]
    public void Deserialize_ShouldIgnoreUnknownExtraFields_WithoutThrowing()
    {
        var yaml = """
expectedOutput: known
unknownExtra: true
someOtherFutureField:
  nested: value
""";

        var result = _serializer.Deserialize<TaskYamlShape>(yaml);

        Assert.Equal("known", result.ExpectedOutput);
    }

    [Fact]
    public void Deserialize_ShouldIgnoreUnknownExtra_EvenWhenMixedWithSnakeCaseAliases()
    {
        var yaml = """
expected_output: known
unknown_extra: true
""";

        var result = _serializer.Deserialize<TaskYamlShape>(yaml);

        Assert.Equal("known", result.ExpectedOutput);
    }

    [Fact]
    public void Serialize_ShouldEmitCamelCase_AsCanonicalForm()
    {
        var task = new TaskYamlShape
        {
            ExpectedOutput = "summary",
            OutputFile = "file.md",
            AsyncExecution = true,
            HumanInput = false,
        };

        var yaml = _serializer.Serialize(task);

        Assert.Contains("expectedOutput:", yaml);
        Assert.Contains("outputFile:", yaml);
        Assert.Contains("asyncExecution:", yaml);
        Assert.Contains("humanInput:", yaml);
        // Snake_case never reappears on the wire — canonical form is preserved.
        Assert.DoesNotContain("expected_output:", yaml);
    }

    [Fact]
    public void SnakeToCamelCase_ShouldReturnInputUnchanged_WhenAlreadyCamelOrLowercaseWord()
    {
        Assert.Equal("expectedOutput", CamelOrSnakeCaseTypeInspector.SnakeToCamelCase("expectedOutput"));
        Assert.Equal("model", CamelOrSnakeCaseTypeInspector.SnakeToCamelCase("model"));
    }

    [Fact]
    public void SnakeToCamelCase_ShouldHandleMultipleSegments()
    {
        Assert.Equal("expectedOutput", CamelOrSnakeCaseTypeInspector.SnakeToCamelCase("expected_output"));
        Assert.Equal("asyncExecutionMode", CamelOrSnakeCaseTypeInspector.SnakeToCamelCase("async_execution_mode"));
    }
}

internal sealed class TaskYamlShape
{
    public string ExpectedOutput { get; set; } = "";
    public string OutputFile { get; set; } = "";
    public bool AsyncExecution { get; set; }
    public bool HumanInput { get; set; }
}
