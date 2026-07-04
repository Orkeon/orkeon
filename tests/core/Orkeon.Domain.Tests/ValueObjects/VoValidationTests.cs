using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.HumanInput.ValueObjects;
using Orkeon.Domain.Memory.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Domain.Task.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>
/// Tests for AUDIT-R32: Value Object constructor/factory validation.
/// </summary>
public class VoValidationTests
{
    // ────────────────────────────────────────────
    // AgentSelectionResult
    // ────────────────────────────────────────────

    [Fact]
    public void AgentSelectionResult_Success_ShouldRequireAgentId()
    {
        var agentId = AgentId.Create();
        var result = AgentSelectionResult.Success(agentId, 0.9);

        Assert.True(result.IsSuccess);
        Assert.Equal(agentId, result.SelectedAgentId);
        Assert.Equal(0.9, result.ConfidenceScore);
    }

    [Fact]
    public void AgentSelectionResult_Failure_ShouldAcceptNullAgentId()
    {
        var result = AgentSelectionResult.Failure("no match");

        Assert.False(result.IsSuccess);
        Assert.Null(result.SelectedAgentId);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(2.0)]
    public void AgentSelectionResult_Create_ShouldRejectOutOfRangeConfidence(double confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: confidence));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void AgentSelectionResult_Create_ShouldAcceptValidConfidence(double confidence)
    {
        var result = AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: confidence);
        Assert.Equal(confidence, result.ConfidenceScore);
    }

    [Fact]
    public void AgentSelectionResult_Create_ShouldRejectSuccessWithoutAgentId()
    {
        Assert.Throws<ArgumentException>(() =>
            AgentSelectionResult.Create(null, isSuccess: true));
    }

    // ────────────────────────────────────────────
    // CrewCompletionResult
    // ────────────────────────────────────────────

    [Fact]
    public void CrewCompletionResult_Create_ShouldAcceptValidValues()
    {
        var result = CrewCompletionResult.Create("output", true, TimeSpan.FromSeconds(5), 3, 2);

        Assert.Equal("output", result.Output);
        Assert.True(result.Success);
        Assert.Equal(3, result.TaskCount);
        Assert.Equal(2, result.SuccessfulTaskCount);
    }

    [Fact]
    public void CrewCompletionResult_Create_ShouldRejectNegativeTaskCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrewCompletionResult.Create("out", true, TimeSpan.FromSeconds(1), taskCount: -1));
    }

    [Fact]
    public void CrewCompletionResult_Create_ShouldRejectNegativeSuccessfulTaskCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrewCompletionResult.Create("out", true, TimeSpan.FromSeconds(1), taskCount: 5, successfulTaskCount: -1));
    }

    [Fact]
    public void CrewCompletionResult_Create_ShouldRejectSuccessfulGreaterThanTotal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrewCompletionResult.Create("out", true, TimeSpan.FromSeconds(1), taskCount: 2, successfulTaskCount: 5));
    }

    [Fact]
    public void CrewCompletionResult_Create_ShouldRejectNegativeExecutionTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrewCompletionResult.Create("out", true, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void CrewCompletionResult_Create_ShouldAcceptZeroExecutionTime()
    {
        var result = CrewCompletionResult.Create("out", true, TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, result.ExecutionTime);
    }

    // ────────────────────────────────────────────
    // ContextWindowSettings
    // ────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(4000)]
    [InlineData(128000)]
    public void ContextWindowSettings_ShouldAcceptValidMaxTokens(int maxTokens)
    {
        var settings = ContextWindowSettings.Create(maxTokens: maxTokens);
        Assert.Equal(maxTokens, settings.MaxTokens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ContextWindowSettings_ShouldRejectInvalidMaxTokens(int maxTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextWindowSettings.Create(maxTokens: maxTokens));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ContextWindowSettings_ShouldAcceptValidCompressionRatio(double ratio)
    {
        var settings = ContextWindowSettings.Create(compressionRatio: ratio);
        Assert.Equal(ratio, settings.CompressionRatio);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(2.0)]
    public void ContextWindowSettings_ShouldRejectInvalidCompressionRatio(double ratio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextWindowSettings.Create(compressionRatio: ratio));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ContextWindowSettings_ShouldRejectEmptySummaryPrompt(string prompt)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ContextWindowSettings.Create(summaryPrompt: prompt));
    }

    [Fact]
    public void ContextWindowSettings_Default_ShouldHaveValidDefaults()
    {
        var settings = ContextWindowSettings.Default;
        Assert.Equal(4096, settings.MaxTokens);
        Assert.Equal(0.5, settings.CompressionRatio);
        Assert.True(settings.AutoSummarize);
    }

    // ────────────────────────────────────────────
    // LlmConfig
    // ────────────────────────────────────────────

    [Fact]
    public void LlmConfig_Create_ShouldRejectNullOrEmptyModel()
    {
        Assert.ThrowsAny<ArgumentException>(() => LlmConfig.Create(""));
        Assert.ThrowsAny<ArgumentException>(() => LlmConfig.Create(" "));
        Assert.ThrowsAny<ArgumentException>(() => LlmConfig.Create(null!));
    }

    [Fact]
    public void LlmConfig_Create_ShouldAcceptValidModel()
    {
        var config = LlmConfig.Create(ModelGpt4);
        Assert.Equal(ModelGpt4, config.Model);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void LlmConfig_CreateValidated_ShouldRejectInvalidTemperature(double temp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LlmConfig.CreateValidated(ModelGpt4, temperature: temp));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void LlmConfig_CreateValidated_ShouldAcceptValidTemperature(double temp)
    {
        var config = LlmConfig.CreateValidated(ModelGpt4, temperature: temp);
        Assert.Equal(temp, config.Temperature);
    }

    [Fact]
    public void LlmConfig_CreateValidated_ShouldRejectInvalidMaxTokens()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LlmConfig.CreateValidated(ModelGpt4, maxTokens: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LlmConfig.CreateValidated(ModelGpt4, maxTokens: -1));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void LlmConfig_CreateValidated_ShouldRejectInvalidTopP(double topP)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LlmConfig.CreateValidated(ModelGpt4, topP: topP));
    }

    [Fact]
    public void LlmConfig_CreateValidated_ShouldRejectInvalidTimeoutSeconds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LlmConfig.CreateValidated(ModelGpt4, timeoutSeconds: 0));
    }

    [Fact]
    public void LlmConfig_CreateValidated_ShouldRejectNegativeMaxRetries()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LlmConfig.CreateValidated(ModelGpt4, maxRetries: -1));
    }

    [Fact]
    public void LlmConfig_CreateValidated_ShouldAcceptZeroMaxRetries()
    {
        var config = LlmConfig.CreateValidated(ModelGpt4, maxRetries: 0);
        Assert.Equal(0, config.MaxRetries);
    }

    [Theory]
    [InlineData(-2.1)]
    [InlineData(2.1)]
    public void LlmConfig_CreateValidated_ShouldRejectInvalidFrequencyPenalty(double penalty)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LlmConfig.CreateValidated(ModelGpt4, frequencyPenalty: penalty));
    }

    [Theory]
    [InlineData(-2.0)]
    [InlineData(0.0)]
    [InlineData(2.0)]
    public void LlmConfig_CreateValidated_ShouldAcceptValidFrequencyPenalty(double penalty)
    {
        var config = LlmConfig.CreateValidated(ModelGpt4, frequencyPenalty: penalty);
        Assert.Equal(penalty, config.FrequencyPenalty);
    }

    // ────────────────────────────────────────────
    // ExecutionConfig
    // ────────────────────────────────────────────

    [Fact]
    public void ExecutionConfig_Create_ShouldAcceptValidValues()
    {
        var config = ExecutionConfig.Create(maxConcurrentTasks: 5, maxRetries: 2, maxRPM: 100);
        Assert.Equal(5, config.MaxConcurrentTasks);
        Assert.Equal(2, config.MaxRetries);
        Assert.Equal(100, config.MaxRPM);
    }

    [Fact]
    public void ExecutionConfig_Create_ShouldRejectInvalidMaxConcurrentTasks()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExecutionConfig.Create(maxConcurrentTasks: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExecutionConfig.Create(maxConcurrentTasks: -1));
    }

    [Fact]
    public void ExecutionConfig_Create_ShouldRejectNegativeMaxRetries()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExecutionConfig.Create(maxRetries: -1));
    }

    [Fact]
    public void ExecutionConfig_Create_ShouldRejectNegativeMaxRPM()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExecutionConfig.Create(maxRPM: -1));
    }

    [Fact]
    public void ExecutionConfig_Create_ShouldRejectNonPositiveTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExecutionConfig.Create(defaultTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExecutionConfig.Create(defaultTimeout: TimeSpan.FromSeconds(-1)));
    }

    // ────────────────────────────────────────────
    // ContentPart subtypes
    // ────────────────────────────────────────────

    [Fact]
    public void TextContentPart_ShouldRejectNullText()
    {
        Assert.Throws<ArgumentNullException>(() => new TextContentPart(null!));
    }

    [Fact]
    public void TextContentPart_ShouldAcceptEmptyString()
    {
        var part = new TextContentPart("");
        Assert.Equal("", part.Text);
    }

    [Fact]
    public void ImageContentPart_FromUri_ShouldRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => ImageContentPart.FromUri(null!));
    }

    [Fact]
    public void ImageContentPart_FromBytes_ShouldRejectNullData()
    {
        Assert.Throws<ArgumentNullException>(() => ImageContentPart.FromBytes(null!));
    }

    [Fact]
    public void ImageContentPart_FromBytes_ShouldRejectEmptyData()
    {
        Assert.Throws<ArgumentException>(() => ImageContentPart.FromBytes([]));
    }

    [Fact]
    public void ImageContentPart_FromBase64_ShouldRejectEmpty()
    {
        Assert.ThrowsAny<ArgumentException>(() => ImageContentPart.FromBase64(""));
    }

    [Fact]
    public void ImageContentPart_FromBytes_ShouldAcceptValidData()
    {
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var part = ImageContentPart.FromBytes(data);
        Assert.Equal(data, part.Data);
    }

    [Fact]
    public void AudioContentPart_FromUri_ShouldRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => AudioContentPart.FromUri(null!));
    }

    [Fact]
    public void AudioContentPart_FromBytes_ShouldRejectEmptyData()
    {
        Assert.Throws<ArgumentException>(() => AudioContentPart.FromBytes([]));
    }

    // ────────────────────────────────────────────
    // CrewVariables
    // ────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CrewVariables_Set_ShouldRejectEmptyKey(string key)
    {
        var vars = CrewVariables.Empty;

        Assert.ThrowsAny<ArgumentException>(() => vars.Set(key, "value"));
        Assert.ThrowsAny<ArgumentException>(() => vars.Set(key, 42));
        Assert.ThrowsAny<ArgumentException>(() => vars.Set(key, true));
        Assert.ThrowsAny<ArgumentException>(() => vars.Set(key, 3.14));
    }

    [Fact]
    public void CrewVariables_Set_ShouldAcceptValidKey()
    {
        var vars = CrewVariables.Empty.Set("myKey", "value");
        Assert.Equal("value", vars.GetString("myKey"));
    }

    // ────────────────────────────────────────────
    // DelegationParameters
    // ────────────────────────────────────────────

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void DelegationParameters_AddMinSkillMatch_ShouldRejectOutOfRange(double value)
    {
        var builder = DelegationParameters.CreateBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddMinSkillMatch(value));
    }

    [Fact]
    public void DelegationParameters_AddMaxWorkload_ShouldRejectZero()
    {
        var builder = DelegationParameters.CreateBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddMaxWorkload(0));
    }

    [Fact]
    public void DelegationParameters_AddManagerRole_ShouldRejectEmpty()
    {
        var builder = DelegationParameters.CreateBuilder();
        Assert.ThrowsAny<ArgumentException>(() => builder.AddManagerRole(""));
    }

    [Fact]
    public void DelegationParameters_AddTimeout_ShouldRejectNonPositive()
    {
        var builder = DelegationParameters.CreateBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddTimeout(TimeSpan.Zero));
    }

    [Fact]
    public void DelegationParameters_AddRetryLimit_ShouldRejectNegative()
    {
        var builder = DelegationParameters.CreateBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddRetryLimit(-1));
    }

    [Fact]
    public void DelegationParameters_AddRetryLimit_ShouldAcceptZero()
    {
        var builder = DelegationParameters.CreateBuilder();
        builder.AddRetryLimit(0); // should not throw
        var parameters = builder.Build();
        Assert.True(parameters.Contains("retryLimit"));
    }

    [Fact]
    public void DelegationParameters_ForSkillBased_ShouldCreateValidInstance()
    {
        var parameters = DelegationParameters.ForSkillBased(0.9);
        Assert.True(parameters.Contains("minSkillMatch"));
    }

    // ────────────────────────────────────────────
    // EpisodicMemoryMetadata
    // ────────────────────────────────────────────

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void EpisodicMemoryMetadata_AddSuccessScore_ShouldRejectOutOfRange(double score)
    {
        var builder = EpisodicMemoryMetadata.CreateBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddSuccessScore(score));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void EpisodicMemoryMetadata_AddSuccessScore_ShouldAcceptValidValues(double score)
    {
        var builder = EpisodicMemoryMetadata.CreateBuilder();
        builder.AddSuccessScore(score);
        var metadata = builder.Build();
        Assert.NotNull(metadata);
    }

    [Fact]
    public void EpisodicMemoryMetadata_AddIterationCount_ShouldRejectNegative()
    {
        var builder = EpisodicMemoryMetadata.CreateBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddIterationCount(-1));
    }

    [Fact]
    public void EpisodicMemoryMetadata_AddIterationCount_ShouldAcceptZero()
    {
        var builder = EpisodicMemoryMetadata.CreateBuilder();
        builder.AddIterationCount(0); // should not throw
        var metadata = builder.Build();
        Assert.NotNull(metadata);
    }

    // ────────────────────────────────────────────
    // EpisodeEventData (confidence validation)
    // ────────────────────────────────────────────

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void EpisodeEventData_AddConfidence_ShouldRejectOutOfRange(double confidence)
    {
        var builder = EpisodeEventData.CreateBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddConfidence(confidence));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void EpisodeEventData_AddConfidence_ShouldAcceptValidValues(double confidence)
    {
        var builder = EpisodeEventData.CreateBuilder();
        builder.AddConfidence(confidence);
        var data = builder.Build();
        Assert.NotNull(data);
    }

    // ────────────────────────────────────────────
    // ProcessResult (VO #1)
    // ────────────────────────────────────────────

    [Fact]
    public void ProcessResult_ShouldRejectNegativeExecutionTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProcessResult.Create(true, executionTime: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void ProcessResult_ShouldAcceptZeroExecutionTime()
    {
        var result = ProcessResult.Create(true, executionTime: TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, result.ExecutionTime);
    }

    [Fact]
    public void ProcessResult_Failure_ShouldRequireErrorMessage()
    {
        Assert.Throws<ArgumentException>(() =>
            ProcessResult.Create(false));
    }

    [Fact]
    public void ProcessResult_Failure_ShouldRejectWhitespaceErrorMessage()
    {
        Assert.Throws<ArgumentException>(() =>
            ProcessResult.Create(false, errorMessage: "   "));
    }

    [Fact]
    public void ProcessResult_FactoryFailure_ShouldAcceptErrorMessage()
    {
        var result = ProcessResult.Failure("Something went wrong");
        Assert.False(result.IsSuccess);
        Assert.Equal("Something went wrong", result.ErrorMessage);
    }

    [Fact]
    public void ProcessResult_Success_ShouldAllowNullErrorMessage()
    {
        var result = ProcessResult.Success();
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void CrewResult_ShouldRejectNegativeExecutionTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrewResult.Create(true, timing: new ExecutionTiming(TimeSpan.FromSeconds(-1))));
    }

    [Fact]
    public void CrewResult_Failure_ShouldRequireErrorMessage()
    {
        Assert.Throws<ArgumentException>(() =>
            CrewResult.Create(false));
    }

    [Fact]
    public void CrewResult_Failure_ShouldRejectWhitespaceErrorMessage()
    {
        Assert.Throws<ArgumentException>(() =>
            CrewResult.Create(false, errorMessage: "   "));
    }

    [Fact]
    public void CrewResult_FactoryFailure_ShouldAcceptErrorMessage()
    {
        var result = CrewResult.Failure("Crew failed");
        Assert.False(result.IsSuccess);
        Assert.Equal("Crew failed", result.ErrorMessage);
    }

    // ────────────────────────────────────────────
    // TaskOutputInfo (VO #2)
    // ────────────────────────────────────────────

    [Fact]
    public void TaskOutputInfo_Create_ShouldRejectNullRawOutput()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TaskOutputInfo.Create(rawOutput: null!));
    }

    [Fact]
    public void TaskOutputInfo_Create_ShouldRejectEmptyRawOutput()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TaskOutputInfo.Create(rawOutput: ""));
    }

    [Fact]
    public void TaskOutputInfo_Create_ShouldRejectWhitespaceRawOutput()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TaskOutputInfo.Create(rawOutput: "   "));
    }

    [Fact]
    public void TaskOutputInfo_Create_ShouldRejectEmptyFormat()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TaskOutputInfo.Create(rawOutput: "test", format: ""));
    }

    [Fact]
    public void TaskOutputInfo_Create_ShouldRejectNegativeExecutionTime()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TaskOutputInfo.Create(rawOutput: "test", executionTime: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void TaskOutputInfo_Create_ShouldAcceptValidValues()
    {
        var info = TaskOutputInfo.Create(
            rawOutput: "output",
            format: "json",
            executionTime: TimeSpan.FromSeconds(5));

        Assert.Equal("output", info.RawOutput);
        Assert.Equal("json", info.Format);
        Assert.Equal(TimeSpan.FromSeconds(5), info.ExecutionTime);
    }

    [Fact]
    public void TaskOutputInfo_Create_ShouldAcceptZeroExecutionTime()
    {
        var info = TaskOutputInfo.Create(rawOutput: "test", executionTime: TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, info.ExecutionTime);
    }

    // ────────────────────────────────────────────
    // AgentStepContext (VO #3)
    // ────────────────────────────────────────────

    [Fact]
    public void AgentStepContext_ShouldRejectNullThought()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentStepContext(null!, "action", "input", "obs", null, StepMetadata.Empty));
    }

    [Fact]
    public void AgentStepContext_ShouldRejectNullAction()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentStepContext("thought", null!, "input", "obs", null, StepMetadata.Empty));
    }

    [Fact]
    public void AgentStepContext_ShouldRejectNullActionInput()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentStepContext("thought", "action", null!, "obs", null, StepMetadata.Empty));
    }

    [Fact]
    public void AgentStepContext_ShouldRejectNullObservation()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentStepContext("thought", "action", "input", null!, null, StepMetadata.Empty));
    }

    [Fact]
    public void AgentStepContext_ShouldRejectNullMetadata()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentStepContext("thought", "action", "input", "obs", null, null!));
    }

    [Fact]
    public void AgentStepContext_Create_ShouldAcceptValidValues()
    {
        var ctx = AgentStepContext.Create(
            "thinking", "act", "input", "obs", null, StepMetadata.Empty);

        Assert.Equal("thinking", ctx.Thought);
        Assert.Equal("act", ctx.Action);
    }

    [Fact]
    public void StepMetadata_ShouldRejectNegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StepMetadata(DateTime.UtcNow, TimeSpan.FromSeconds(-1), 0));
    }

    [Fact]
    public void StepMetadata_ShouldRejectNegativeTokensUsed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StepMetadata(DateTime.UtcNow, TimeSpan.Zero, -5));
    }

    [Fact]
    public void StepMetadata_ShouldAcceptZeroValues()
    {
        var meta = new StepMetadata(DateTime.UtcNow, TimeSpan.Zero, 0);
        Assert.Equal(TimeSpan.Zero, meta.Duration);
        Assert.Equal(0, meta.TokensUsed);
    }

    // ────────────────────────────────────────────
    // ExecutionMetadata (VO #4)
    // ────────────────────────────────────────────

    [Fact]
    public void ExecutionMetadata_ShouldRejectNegativeRetryCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExecutionMetadata(
                new ExecutionTimingInfo(DateTime.UtcNow, null, null),
                new ExecutionIdentityInfo("exec-1", null),
                RetryCount: -1,
                LastError: null,
                Tags: [],
                CustomProperties: []));
    }

    [Fact]
    public void ExecutionMetadata_ShouldRejectEmptyExecutionId()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ExecutionMetadata(
                new ExecutionTimingInfo(DateTime.UtcNow, null, null),
                new ExecutionIdentityInfo("", null),
                RetryCount: 0,
                LastError: null,
                Tags: [],
                CustomProperties: []));
    }

    [Fact]
    public void ExecutionMetadata_ShouldRejectWhitespaceExecutionId()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ExecutionMetadata(
                new ExecutionTimingInfo(DateTime.UtcNow, null, null),
                new ExecutionIdentityInfo("   ", null),
                RetryCount: 0,
                LastError: null,
                Tags: [],
                CustomProperties: []));
    }

    [Fact]
    public void ExecutionMetadata_ShouldRejectCompletedAtBeforeStartedAt()
    {
        var startedAt = DateTime.UtcNow;
        var completedAt = startedAt.AddMinutes(-5);

        Assert.Throws<ArgumentException>(() =>
            new ExecutionMetadata(
                new ExecutionTimingInfo(startedAt, completedAt, null),
                new ExecutionIdentityInfo("exec-1", null),
                RetryCount: 0,
                LastError: null,
                Tags: [],
                CustomProperties: []));
    }

    [Fact]
    public void ExecutionMetadata_ShouldAcceptCompletedAtEqualToStartedAt()
    {
        var now = DateTime.UtcNow;
        var metadata = new ExecutionMetadata(
            new ExecutionTimingInfo(now, now, null),
            new ExecutionIdentityInfo("exec-1", null),
            RetryCount: 0,
            LastError: null,
            Tags: [],
            CustomProperties: []);

        Assert.Equal(now, metadata.CompletedAt);
    }

    [Fact]
    public void ExecutionMetadata_ShouldAcceptNullCompletedAt()
    {
        var metadata = ExecutionMetadata.CreateNew();
        Assert.Null(metadata.CompletedAt);
        Assert.Equal(0, metadata.RetryCount);
    }

    // ────────────────────────────────────────────
    // LlmMetadata (VO #5)
    // ────────────────────────────────────────────

    [Fact]
    public void LlmMetadata_ShouldRejectNegativeTokensUsed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LlmMetadata(TokensUsed: -1));
    }

    [Fact]
    public void LlmMetadata_ShouldRejectNegativeTokensLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LlmMetadata(TokensLimit: -1));
    }

    [Fact]
    public void LlmMetadata_ShouldRejectNegativeTemperature()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LlmMetadata(Temperature: -0.1));
    }

    [Fact]
    public void LlmMetadata_ShouldRejectTemperatureAboveTwo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LlmMetadata(Temperature: 2.1));
    }

    [Fact]
    public void LlmMetadata_ShouldRejectNegativeResponseTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LlmMetadata(ResponseTime: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void LlmMetadata_ShouldAcceptZeroTokensUsed()
    {
        var metadata = new LlmMetadata(TokensUsed: 0);
        Assert.Equal(0, metadata.TokensUsed);
    }

    [Fact]
    public void LlmMetadata_ShouldAcceptBoundaryTemperature()
    {
        var low = new LlmMetadata(Temperature: 0.0);
        var high = new LlmMetadata(Temperature: 2.0);
        Assert.Equal(0.0, low.Temperature);
        Assert.Equal(2.0, high.Temperature);
    }

    [Fact]
    public void LlmMetadata_ShouldAcceptZeroResponseTime()
    {
        var metadata = new LlmMetadata(ResponseTime: TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, metadata.ResponseTime);
    }

    [Fact]
    public void LlmMetadata_ShouldAcceptAllNulls()
    {
        var metadata = new LlmMetadata();
        Assert.Null(metadata.TokensUsed);
        Assert.Null(metadata.TokensLimit);
        Assert.Null(metadata.Temperature);
        Assert.Null(metadata.ResponseTime);
    }

    // ────────────────────────────────────────────
    // HumanInputEventContext (VO #6)
    // ────────────────────────────────────────────

    [Fact]
    public void HumanInputEventContext_Create_ShouldRejectEmptyAnalysisType()
    {
        Assert.Throws<ArgumentException>(() =>
            HumanInputEventContext.Create(analysisType: ""));
    }

    [Fact]
    public void HumanInputEventContext_Create_ShouldRejectWhitespaceAnalysisType()
    {
        Assert.Throws<ArgumentException>(() =>
            HumanInputEventContext.Create(analysisType: "   "));
    }

    [Fact]
    public void HumanInputEventContext_Create_ShouldAcceptNullAnalysisType()
    {
        var ctx = HumanInputEventContext.Create(analysisType: null);
        Assert.Null(ctx.AnalysisType);
    }

    [Fact]
    public void HumanInputEventContext_Create_ShouldAcceptValidAnalysisType()
    {
        var ctx = HumanInputEventContext.Create(analysisType: "sentiment");
        Assert.Equal("sentiment", ctx.AnalysisType);
    }

    [Fact]
    public void HumanInputEventContext_Create_ShouldRejectEmptyPriority()
    {
        Assert.Throws<ArgumentException>(() =>
            HumanInputEventContext.Create(priority: ""));
    }

    [Fact]
    public void HumanInputEventContext_Create_ShouldRejectEmptyTaskDescription()
    {
        Assert.Throws<ArgumentException>(() =>
            HumanInputEventContext.Create(taskDescription: ""));
    }

    [Fact]
    public void HumanInputEventContext_Create_ShouldRejectEmptyAgentRole()
    {
        Assert.Throws<ArgumentException>(() =>
            HumanInputEventContext.Create(agentRole: ""));
    }

    [Fact]
    public void HumanInputEventContext_Create_ShouldAcceptAllNulls()
    {
        var ctx = HumanInputEventContext.Create();
        Assert.Null(ctx.AnalysisType);
        Assert.Null(ctx.Priority);
        Assert.Null(ctx.TaskDescription);
        Assert.Null(ctx.AgentRole);
        Assert.Null(ctx.Notes);
    }

    [Fact]
    public void HumanInputEventContext_Create_ShouldAcceptAllValidValues()
    {
        var ctx = HumanInputEventContext.Create(
            analysisType: "type",
            priority: "high",
            taskDescription: "desc",
            agentRole: "role",
            notes: "some notes");

        Assert.Equal("type", ctx.AnalysisType);
        Assert.Equal("high", ctx.Priority);
        Assert.Equal("desc", ctx.TaskDescription);
        Assert.Equal("role", ctx.AgentRole);
        Assert.Equal("some notes", ctx.Notes);
    }

    // ────────────────────────────────────────────
    // TypedTaskExecutionContext (VO #7)
    // ────────────────────────────────────────────

    [Fact]
    public void TypedTaskExecutionContext_ShouldRejectNullTaskId()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TypedTaskExecutionContext(
                null!,
                AgentId.Create(),
                TaskVariables.Empty,
                [],
                ExecutionMetadata.CreateNew(),
                []));
    }

    [Fact]
    public void TypedTaskExecutionContext_ShouldRejectNullExecutingAgent()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TypedTaskExecutionContext(
                TaskId.Create(),
                null!,
                TaskVariables.Empty,
                [],
                ExecutionMetadata.CreateNew(),
                []));
    }

    [Fact]
    public void TypedTaskExecutionContext_ShouldRejectNullVariables()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TypedTaskExecutionContext(
                TaskId.Create(),
                AgentId.Create(),
                null!,
                [],
                ExecutionMetadata.CreateNew(),
                []));
    }

    [Fact]
    public void TypedTaskExecutionContext_ShouldRejectNullMetadata()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TypedTaskExecutionContext(
                TaskId.Create(),
                AgentId.Create(),
                TaskVariables.Empty,
                [],
                null!,
                []));
    }

    [Fact]
    public void TypedTaskExecutionContext_Create_ShouldAcceptValidValues()
    {
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var ctx = TypedTaskExecutionContext.Create(taskId, agentId);

        Assert.Equal(taskId, ctx.TaskId);
        Assert.Equal(agentId, ctx.ExecutingAgent);
        Assert.Same(TaskVariables.Empty, ctx.Variables);
        Assert.NotNull(ctx.Metadata);
    }
}
