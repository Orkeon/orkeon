using Orkeon.Domain.Tools.Protocol;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Infrastructure.Tests.Knowledge;

public class RagToolTests
{
    private readonly RagToolTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldReturnAnswer_WhenQuestionIsValid()
    {
        var tool = _fixture.WithPipelineResult().CreateTool();

        var request = new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "What is the meaning?" });

        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.NotNull(response.Result);
        Assert.Contains("The answer is 42", response.Result!.ToString()!);
    }

    [Fact]
    public async Task ShouldReturnError_WhenQuestionIsMissing()
    {
        var tool = _fixture.CreateTool();

        var request = new ToolCallRequest("rag_search",
            []);

        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("question", response.Error!);
    }

    [Fact]
    public async Task ShouldReturnError_WhenQuestionIsEmpty()
    {
        var tool = _fixture.CreateTool();

        var request = new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "" });

        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("question", response.Error!);
    }

    [Fact]
    public async Task ShouldIncludeSourcesInResponse_WhenCalling()
    {
        var tool = _fixture.WithPipelineResult().CreateTool();

        var request = new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = ParamQuery });

        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        var responseText = response.Result!.ToString()!;
        Assert.Contains("Sources:", responseText);
        Assert.Contains("guide.txt", responseText);
        Assert.Contains("0.92", responseText);
    }

    [Fact]
    public async Task ShouldPassTopKOption_WhenTopKIsProvided()
    {
        var tool = _fixture.WithPipelineResult().CreateTool();

        var request = new ToolCallRequest("rag_search",
            new Dictionary<string, object?>
            {
                ["question"] = ParamQuery,
                ["top_k"] = 7
            });

        await tool.CallAsync(request, TestContext.Current.CancellationToken);

        var pipeline = _fixture.GetPipeline();
        Assert.Equal(1, pipeline.ExecuteStringCallCount);
        Assert.Equal(ParamQuery, pipeline.LastExecuteQuestion);
        Assert.NotNull(pipeline.LastExecuteOptions);
        Assert.Equal(7, pipeline.LastExecuteOptions!.Retrieval.TopK);
    }

    [Fact]
    public async Task ShouldDelegateToCallAsync_WhenExecuting()
    {
        var tool = _fixture.WithPipelineResult().CreateTool();

        var result = await tool.ExecuteAsync("What is AI?", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("The answer is 42", result.Output!);

        var pipeline = _fixture.GetPipeline();
        Assert.Equal(1, pipeline.ExecuteStringCallCount);
        Assert.Equal("What is AI?", pipeline.LastExecuteQuestion);
    }

    [Fact]
    public void ShouldReturnFalse_WhenValidatingEmptyInput()
    {
        var tool = _fixture.CreateTool();

        Assert.False(tool.ValidateInput(""));
        Assert.False(tool.ValidateInput("   "));
    }

    [Fact]
    public void ShouldReturnTrue_WhenValidatingValidInput()
    {
        var tool = _fixture.CreateTool();

        Assert.True(tool.ValidateInput("What is machine learning?"));
    }

    [Fact]
    public void ShouldHaveCorrectParameters_WhenAccessingSchema()
    {
        var tool = _fixture.CreateTool();
        var schema = tool.Schema;

        Assert.Equal("rag_search", schema.Name);
        Assert.Contains("question", schema.Parameters.Keys);
        Assert.Contains("top_k", schema.Parameters.Keys);
        Assert.True(schema.Parameters["question"].Required);
        Assert.False(schema.Parameters["top_k"].Required);
    }

    [Fact]
    public void ShouldReturnRagSearch_WhenAccessingName()
    {
        var tool = _fixture.CreateTool();

        Assert.Equal("rag_search", tool.Name);
    }
}
