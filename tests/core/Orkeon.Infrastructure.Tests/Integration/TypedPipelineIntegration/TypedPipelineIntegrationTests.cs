using Orkeon.Application.Evaluation;
using Orkeon.Domain.Common;
using Orkeon.Domain.Flows.ValueObjects;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using Orkeon.Infrastructure.Evaluation.Evaluators;
using Orkeon.Infrastructure.Flows.Steps;
using Orkeon.Tools.Data;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Infrastructure.Tests.Integration;

/// <summary>
/// End-to-end integration tests validating that the typed Request/Response pipeline
/// works correctly across all component types: Tools, Evaluators, and Flow Steps.
/// Verifies the full Dict → TRequest → ExecuteTypedAsync → TResponse → Dict round-trip.
/// </summary>
public class TypedPipelineIntegrationTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // 1. Tool Pipeline E2E
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ShouldReturnTypedFields_WhenJsonToolParseOperationTypedPipeline()
    {
        // Arrange
        using var tool = new JsonTool(new FakeFileSystemService());
        var request = new ToolCallRequest(
            ToolName: "json_tool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = "{\"name\": \"Alice\", \"age\": 30}",
                ["operation"] = "parse"
            });

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success, $"Expected success but got error: {response.Error}");
        Assert.NotNull(response.Result);

        // The result should be a dictionary with typed fields from JsonToolResponse
        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);

        // Verify typed fields: parsed, info, value
        Assert.True(resultDict.ContainsKey("parsed"), "Result should contain 'parsed' key");
        Assert.True(resultDict.ContainsKey("info"), "Result should contain 'info' key");
        Assert.True(resultDict.ContainsKey("value"), "Result should contain 'value' key");

        // parsed should be true
        var parsed = resultDict["parsed"];
        Assert.NotNull(parsed);
    }

    [Fact]
    public async Task ShouldReturnQueryResult_WhenJsonToolQueryOperationTypedPipeline()
    {
        // Arrange
        using var tool = new JsonTool(new FakeFileSystemService());
        var request = new ToolCallRequest(
            ToolName: "json_tool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = "{\"data\": {\"items\": [{\"name\": \"first\"}, {\"name\": \"second\"}]}}",
                ["operation"] = ParamQuery,
                [ParamQuery] = "data.items[0].name"
            });

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success, $"Expected success but got error: {response.Error}");
        Assert.NotNull(response.Result);

        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);

        Assert.True(resultDict.ContainsKey(ParamQuery), "Result should contain 'query' key");
        Assert.True(resultDict.ContainsKey("value"), "Result should contain 'value' key");
        Assert.True(resultDict.ContainsKey("type"), "Result should contain 'type' key");
        Assert.Equal("data.items[0].name", resultDict[ParamQuery]?.ToString());
        Assert.Equal("first", resultDict["value"]?.ToString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenJsonToolInvalidOperationTypedPipeline()
    {
        // Arrange
        using var tool = new JsonTool(new FakeFileSystemService());
        var request = new ToolCallRequest(
            ToolName: "json_tool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = "{\"key\": \"value\"}",
                ["operation"] = "invalid_op"
            });

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        // The error comes from schema-level enum validation (before typed dispatch)
        Assert.Contains("operation", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. Evaluator Pipeline E2E
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ShouldReturnTypedScoreAndDetails_WhenTextQualityEvaluatorTypedPipeline()
    {
        // Arrange
        var evaluator = new TextQualityEvaluator();
        var text = "The artificial intelligence industry has undergone remarkable transformation. " +
                   "Machine learning algorithms now power diverse applications. " +
                   "Natural language processing enables nuanced understanding. " +
                   "Computer vision systems identify complex patterns. " +
                   "Ethical considerations remain paramount for deployment. " +
                   "Researchers explore novel architectures constantly. " +
                   "Hardware improvements drive algorithmic breakthroughs forward.";

        var input = new EvaluationInput(Output: text);

        // Act
        var result = await evaluator.EvaluateAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("TextQuality", result.EvaluatorName);
        Assert.True(result.Score > 0.0, "Score should be positive for quality text");
        Assert.True(result.Score <= 1.0, "Score should not exceed 1.0");
        Assert.NotNull(result.Reasoning);

        // Verify the Details dictionary contains typed data from TextQualityResult
        Assert.NotNull(result.Details);
        Assert.True(result.Details.ContainsKey("word_count"), "Details should contain 'word_count'");
        Assert.True(result.Details.ContainsKey("sentence_count"), "Details should contain 'sentence_count'");
        Assert.True(result.Details.ContainsKey("vocabulary_richness_score"), "Details should contain 'vocabulary_richness_score'");
        Assert.True(result.Details.ContainsKey("repetition_score"), "Details should contain 'repetition_score'");
        Assert.True(result.Details.ContainsKey("overall_score"), "Details should contain 'overall_score'");
    }

    [Fact]
    public async Task ShouldReturnTypedDetails_WhenSimilarityEvaluatorTypedPipeline()
    {
        // Arrange
        var evaluator = new SimilarityEvaluator();
        var input = new EvaluationInput(
            Output: "The quick brown fox jumps over the lazy dog",
            ExpectedOutput: "The quick brown fox leaps over the lazy dog");

        // Act
        var result = await evaluator.EvaluateAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Similarity", result.EvaluatorName);
        Assert.True(result.Score > 0.5, $"Similar texts should have high score, got {result.Score}");
        Assert.True(result.Score <= 1.0, "Score should not exceed 1.0");

        // Verify Details dictionary contains typed similarity metrics
        Assert.NotNull(result.Details);
        Assert.True(result.Details.ContainsKey("levenshtein_similarity"), "Details should contain 'levenshtein_similarity'");
        Assert.True(result.Details.ContainsKey("jaccard_similarity"), "Details should contain 'jaccard_similarity'");
        Assert.True(result.Details.ContainsKey("bigram_overlap"), "Details should contain 'bigram_overlap'");
        Assert.True(result.Details.ContainsKey("overall_score"), "Details should contain 'overall_score'");
        Assert.True(result.Details.ContainsKey("reasoning"), "Details should contain 'reasoning'");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. Flow Step Pipeline E2E
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ShouldExecuteAndReturnsTypedOutput_WhenDelayFlowStepTypedPipeline()
    {
        // Arrange: create a DelayFlowStep with a very short delay for testing
        var parameters = FlowStepParameters.CreateBuilder()
            .Add("delay_ms", 10)
            .Build();
        var step = new DelayFlowStep("test_delay", parameters);

        var context = FlowState.CreateBuilder()
            .Add("delay_ms", 10)
            .Build();

        // Act
        var result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Step should succeed but got error: {result.Error}");
        Assert.NotNull(result.Output);

        // The output should be a DelayFlowStepOutput with a Message
        var output = result.Output as DelayFlowStepOutput;
        Assert.NotNull(output);
        Assert.Contains("Delayed for", output!.Message);

        // The UpdatedContext should contain serialized output
        Assert.NotNull(result.UpdatedContext);
        Assert.True(result.UpdatedContext.ContainsKey("message"), "UpdatedContext should contain 'message' key from serialized output");
    }

    [Fact]
    public async Task ShouldMiniFlowWithStatePassingTypedPipelinePreservesState_WhenFlowStep()
    {
        // Arrange: simulate a mini-flow with two steps passing state
        var step1Params = FlowStepParameters.CreateBuilder()
            .Add("delay_ms", 5)
            .Build();
        var step1 = new DelayFlowStep("step1", step1Params);

        var step2Params = FlowStepParameters.CreateBuilder()
            .Add("delay_ms", 5)
            .Build();
        var step2 = new DelayFlowStep("step2", step2Params);

        // Initial context
        var initialContext = FlowState.CreateBuilder()
            .Add("delay_ms", 5)
            .Add("flow_id", "test-flow-123")
            .Build();

        // Act: execute step1
        var result1 = await step1.ExecuteAsync(initialContext, TestContext.Current.CancellationToken);
        Assert.True(result1.Success, $"Step1 failed: {result1.Error}");

        // Merge step1 output into context for step2
        var step2Context = result1.UpdatedContext
            .Set("delay_ms", 5)
            .Set("previous_step", "step1");

        var result2 = await step2.ExecuteAsync(step2Context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result2.Success, $"Step2 failed: {result2.Error}");
        Assert.NotNull(result2.Output);

        var output2 = result2.Output as DelayFlowStepOutput;
        Assert.NotNull(output2);
        Assert.Contains("Delayed for", output2!.Message);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. Round-trip test: Dict → DeserializeRequest → typed → SerializeResponse → Dict
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ShouldDictToTypedAndBackNoDataLoss_WhenRoundTrip()
    {
        // Arrange: create a ComponentBase pipeline helper for round-trip testing
        var pipeline = new RoundTripPipeline();

        var originalDict = new Dictionary<string, object?>
        {
            ["input"] = "{\"key\": \"value\"}",
            ["operation"] = "parse",
            [ParamQuery] = "data.field"
        };

        // Act: Dict → TRequest
        var typedRequest = pipeline.TestDeserialize(originalDict);

        // Assert: typed fields are populated correctly
        Assert.Equal("{\"key\": \"value\"}", typedRequest.Input);
        Assert.Equal(JsonOperation.Parse, typedRequest.Operation);
        Assert.Equal("data.field", typedRequest.Query);

        // Act: create a typed response and serialize back to dict
        var typedResponse = new JsonToolResponse
        {
            Parsed = true,
            Info = new Dictionary<string, object>
            {
                ["type"] = "Object",
                ["valid"] = true,
                ["property_count"] = 1
            },
            Value = "{\"key\": \"value\"}"
        };

        var roundTrippedDict = pipeline.TestSerialize(typedResponse);

        // Assert: no data loss in the response round-trip
        Assert.True(roundTrippedDict.ContainsKey("parsed"), "Serialized dict should contain 'parsed'");
        Assert.True(roundTrippedDict.ContainsKey("info"), "Serialized dict should contain 'info'");
        Assert.True(roundTrippedDict.ContainsKey("value"), "Serialized dict should contain 'value'");

        // Null fields should be omitted (DefaultIgnoreCondition.WhenWritingNull)
        Assert.False(roundTrippedDict.ContainsKey(ParamQuery), "Null 'query' should be omitted from serialized dict");
        Assert.False(roundTrippedDict.ContainsKey("formatted"), "Null 'formatted' should be omitted from serialized dict");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 5. Backward compatibility: old Dictionary<string,object> still works
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ShouldOldDictionaryParametersBackwardCompatible_WhenJsonTool()
    {
        // Arrange: simulate the old calling pattern with plain Dictionary<string,object>
        using var tool = new JsonTool(new FakeFileSystemService());

        // Old-style: caller constructs ToolCallRequest with a raw dictionary
        var parameters = new Dictionary<string, object?>
        {
            ["input"] = "{\"items\": [1, 2, 3]}",
            ["operation"] = "format"
        };

        var request = new ToolCallRequest(
            ToolName: "json_tool",
            Parameters: parameters);

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert: tool should still work with the old dictionary pattern
        Assert.True(response.Success, $"Old dictionary pattern should work. Error: {response.Error}");
        Assert.NotNull(response.Result);

        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);

        // The format operation should return formatted JSON and length
        Assert.True(resultDict.ContainsKey("formatted"), "Format result should contain 'formatted'");
        Assert.True(resultDict.ContainsKey("length"), "Format result should contain 'length'");

        // Verify the formatted JSON is indented
        var formatted = resultDict["formatted"]?.ToString();
        Assert.NotNull(formatted);
        Assert.Contains("\n", formatted); // Indented JSON has newlines
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test helpers
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exposes ComponentBase deserialization/serialization for round-trip testing.
    /// </summary>
    private sealed class RoundTripPipeline : ComponentBase<JsonToolRequest, JsonToolResponse>
    {
        public JsonToolRequest TestDeserialize(Dictionary<string, object?> parameters)
            => DeserializeRequest(parameters);

        public Dictionary<string, object?> TestSerialize(JsonToolResponse response)
            => SerializeResponse(response);

        protected override Task<JsonToolResponse> ExecuteTypedAsync(JsonToolRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException("Test helper does not execute.");
    }
}
