using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation.Evaluators;

public class SchemaComplianceEvaluatorTests
{
    private readonly SchemaComplianceEvaluatorTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldReturnHighScore_WhenAllRequiredFieldsArePresent()
    {
        var output = """{"name": "John", "age": 30, "active": true}""";
        var schema = """{"name": "String", "age": "Number", "active": "Boolean"}""";

        var input = new EvaluationInput(
            Output: output,
            Metadata: new Dictionary<string, string>
            {
                [SchemaComplianceEvaluatorTestsFixture.SchemaMetadataKey] = schema
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
        Assert.Contains("3/3", result.Reasoning);
    }

    [Fact]
    public async Task ShouldReturnLowerScore_WhenRequiredFieldsAreMissing()
    {
        var output = """{"name": "John"}""";
        var schema = """{"name": "String", "age": "Number", "email": "String"}""";

        var input = new EvaluationInput(
            Output: output,
            Metadata: new Dictionary<string, string>
            {
                [SchemaComplianceEvaluatorTestsFixture.SchemaMetadataKey] = schema
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.True(result.Score < 1.0);
        Assert.True(result.Score > 0.0);
        Assert.Contains("1/3", result.Reasoning);
    }

    [Fact]
    public async Task ShouldDecreaseScore_WhenFieldTypesAreWrong()
    {
        var output = """{"name": 42, "age": "not a number"}""";
        var schema = """{"name": "String", "age": "Number"}""";

        var input = new EvaluationInput(
            Output: output,
            Metadata: new Dictionary<string, string>
            {
                [SchemaComplianceEvaluatorTestsFixture.SchemaMetadataKey] = schema
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenNoSchemaIsProvided()
    {
        var input = new EvaluationInput(Output: """{"anything": "goes"}""");

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
        Assert.Contains("No schema", result.Reasoning);
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenOutputIsInvalidJson()
    {
        var schema = """{"name": "String"}""";
        var input = new EvaluationInput(
            Output: "not json",
            Metadata: new Dictionary<string, string>
            {
                [SchemaComplianceEvaluatorTestsFixture.SchemaMetadataKey] = schema
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public async Task ShouldMatchCorrectly_WhenFieldIsArray()
    {
        var output = """{"tags": ["a", "b"], "count": 2}""";
        var schema = """{"tags": "Array", "count": "Number"}""";

        var input = new EvaluationInput(
            Output: output,
            Metadata: new Dictionary<string, string>
            {
                [SchemaComplianceEvaluatorTestsFixture.SchemaMetadataKey] = schema
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldMatchCorrectly_WhenFieldIsObject()
    {
        var output = """{"config": {"key": "value"}, "name": "test"}""";
        var schema = """{"config": "Object", "name": "String"}""";

        var input = new EvaluationInput(
            Output: output,
            Metadata: new Dictionary<string, string>
            {
                [SchemaComplianceEvaluatorTestsFixture.SchemaMetadataKey] = schema
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldNotRequireLlm_WhenAccessingRequiresLlm()
    {
        Assert.False(_fixture.GetEvaluator().RequiresLlm);
    }
}
