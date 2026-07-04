using Orkeon.Application.Evaluation;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

namespace Orkeon.Infrastructure.Tests.Evaluation.Evaluators;

public class ToolAccuracyEvaluatorTests
{
    private readonly ToolAccuracyEvaluatorTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldReturnScoreOne_WhenAllExpectedToolsAreMentioned()
    {
        var input = new EvaluationInput(
            Output: "I used the FileRead tool first, then called WebScrape to get the data.",
            Metadata: new Dictionary<string, string>
            {
                [ToolAccuracyEvaluatorTestsFixture.ExpectedToolsKey] = "FileRead,WebScrape"
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenNoToolsAreMentioned()
    {
        var input = new EvaluationInput(
            Output: "I completed the task successfully with great results.",
            Metadata: new Dictionary<string, string>
            {
                [ToolAccuracyEvaluatorTestsFixture.ExpectedToolsKey] = "FileRead,WebScrape,HttpApi"
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnPartialScore_WhenSomeToolsAreMentioned()
    {
        var input = new EvaluationInput(
            Output: "I used the FileRead tool to get the data.",
            Metadata: new Dictionary<string, string>
            {
                [ToolAccuracyEvaluatorTestsFixture.ExpectedToolsKey] = "FileRead,WebScrape"
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.5, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenNoExpectedToolsAreSpecified()
    {
        var input = new EvaluationInput(Output: "Some output without tool info.");

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
        Assert.Contains("No expected tools", result.Reasoning);
    }

    [Fact]
    public async Task ShouldMatchCaseInsensitively_WhenCheckingTools()
    {
        var input = new EvaluationInput(
            Output: "I used the fileread tool.",
            Metadata: new Dictionary<string, string>
            {
                [ToolAccuracyEvaluatorTestsFixture.ExpectedToolsKey] = ToolFileRead
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldContainPerToolStatus_WhenAccessingDetails()
    {
        var input = new EvaluationInput(
            Output: "I used FileRead.",
            Metadata: new Dictionary<string, string>
            {
                [ToolAccuracyEvaluatorTestsFixture.ExpectedToolsKey] = "FileRead,WebScrape"
            });

        var result = await _fixture.EvaluateAsync(input);

        Assert.NotNull(result.Details);
        Assert.True(result.Details.ContainsKey("tool_details"));
        var toolDetails = result.Details["tool_details"] as IDictionary<string, object>;
        Assert.NotNull(toolDetails);
        Assert.Equal("found", toolDetails[ToolFileRead]?.ToString());
        Assert.Equal("missing", toolDetails[ToolWebScrape]?.ToString());
    }

    [Fact]
    public async Task ShouldNotRequireLlm_WhenAccessingRequiresLlm()
    {
        Assert.False(_fixture.GetEvaluator().RequiresLlm);
    }
}
