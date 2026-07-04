using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Infrastructure.Caching;
using Orkeon.Application.Interfaces.Monitoring;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Interfaces;

public class InterfaceRecordsTests
{
    // ══════════════ IVotingStrategy — records & enums ══════════════

    [Fact]
    public void ShouldCreateVote_WhenUsingDefaults()
    {
        // Act
        var vote = new Vote();

        // Assert
        Assert.Equal(string.Empty, vote.VoterId);
        Assert.Equal(string.Empty, vote.VoterRole);
        Assert.Equal(string.Empty, vote.Choice);
        Assert.Equal(1.0f, vote.Confidence);
        Assert.Equal(1.0f, vote.Weight);
        Assert.Null(vote.Justification);
    }

    [Fact]
    public void ShouldCreateVote_WhenPopulatingAllFields()
    {
        // Act
        var vote = new Vote
        {
            VoterId = AgentId1,
            VoterRole = RoleAnalyst,
            Choice = "Option A",
            Confidence = 0.9f,
            Weight = 2.0f,
            Justification = "Strong evidence"
        };

        // Assert
        Assert.Equal(AgentId1, vote.VoterId);
        Assert.Equal(RoleAnalyst, vote.VoterRole);
        Assert.Equal("Option A", vote.Choice);
        Assert.Equal(0.9f, vote.Confidence);
        Assert.Equal(2.0f, vote.Weight);
        Assert.Equal("Strong evidence", vote.Justification);
    }

    [Fact]
    public void ShouldCreateVoteResult_WhenConsensusReached()
    {
        // Act
        var result = new VoteResult
        {
            ConsensusReached = true,
            WinningChoice = "Plan B",
            AgreementScore = 0.8f,
            TotalVotes = 5,
            VotesForWinner = 4,
            Scores = new Dictionary<string, float>
            {
                ["Plan A"] = 1.0f,
                ["Plan B"] = 4.0f
            }
        };

        // Assert
        Assert.True(result.ConsensusReached);
        Assert.Equal("Plan B", result.WinningChoice);
        Assert.Equal(0.8f, result.AgreementScore);
        Assert.Equal(5, result.TotalVotes);
        Assert.Equal(4, result.VotesForWinner);
        Assert.Equal(2, result.Scores.Count);
    }

    [Fact]
    public void ShouldSetDefaultVoteResultValues_WhenUsingDefaults()
    {
        // Act
        var result = new VoteResult();

        // Assert
        Assert.False(result.ConsensusReached);
        Assert.Null(result.WinningChoice);
        Assert.Equal(0f, result.AgreementScore);
        Assert.Equal(0, result.TotalVotes);
        Assert.Empty(result.Scores);
        Assert.Empty(result.AllVotes);
    }

    [Fact]
    public void ShouldSetDefaultVotingOptions_WhenConstructing()
    {
        // Act
        var options = new VotingOptions();

        // Assert
        Assert.Equal(ConsensusType.Majority, options.ConsensusType);
        Assert.Equal(50f, options.QuorumPercent);
        Assert.Equal(66.7f, options.ConsensusThreshold);
        Assert.False(options.UseWeightedVotes);
        Assert.Equal(3, options.MaxVotingRounds);
        Assert.True(options.AllowAbstention);
    }

    [Theory]
    [InlineData(ConsensusType.Majority)]
    [InlineData(ConsensusType.SuperMajority)]
    [InlineData(ConsensusType.Unanimity)]
    [InlineData(ConsensusType.WeightedConsensus)]
    [InlineData(ConsensusType.BordaCount)]
    public void ShouldContainExpectedValue_WhenCheckingConsensusTypeEnum(ConsensusType type)
    {
        // Assert
        Assert.True(Enum.IsDefined<ConsensusType>(type));
    }

    // ══════════════ ILlmCache — CacheStatistics ══════════════

    [Fact]
    public void ShouldCreateEmptyCacheStatistics_WhenCallingEmpty()
    {
        // Act
        var stats = CacheStatistics.Empty;

        // Assert
        Assert.Equal(0, stats.TotalHits);
        Assert.Equal(0, stats.TotalMisses);
        Assert.Equal(0.0, stats.HitRate);
        Assert.Equal(TimeSpan.Zero, stats.AverageLatency);
        Assert.Equal(0, stats.EstimatedTokensSaved);
        Assert.Equal(0, stats.CacheSizeBytes);
    }

    [Fact]
    public void ShouldCreateCacheStatistics_WhenPopulatingAllFields()
    {
        // Act
        var stats = new CacheStatistics(
            TotalHits: 100,
            TotalMisses: 20,
            HitRate: 0.833,
            AverageLatency: TimeSpan.FromMilliseconds(5),
            EstimatedTokensSaved: 50_000,
            CacheSizeBytes: 1024 * 1024,
            LastResetTime: DateTime.UtcNow);

        // Assert
        Assert.Equal(100, stats.TotalHits);
        Assert.Equal(20, stats.TotalMisses);
        Assert.Equal(0.833, stats.HitRate, precision: 3);
        Assert.Equal(50_000, stats.EstimatedTokensSaved);
    }

    // ══════════════ IEncryptionProvider — EncryptionResult ══════════════

    [Fact]
    public void ShouldStoreEncryptionResultFields_WhenCreating()
    {
        // Arrange
        var cipher = new byte[] { 1, 2, 3 };
        var nonce = new byte[] { 4, 5, 6 };
        var tag = new byte[] { 7, 8, 9 };

        // Act
        var result = new EncryptionResult(cipher, nonce, tag);

        // Assert
        Assert.Equal(cipher, result.CipherText);
        Assert.Equal(nonce, result.Nonce);
        Assert.Equal(tag, result.Tag);
    }

    // ══════════════ MonitoringTypes ══════════════

    [Fact]
    public void ShouldCreateAggregatedMetrics_WhenPopulatingFields()
    {
        // Act
        var metrics = new AggregatedMetrics
        {
            TotalLlmCalls = 500,
            TotalToolExecutions = 200,
            TotalTaskExecutions = 100,
            TotalCrewExecutions = 10,
            ActiveCrews = 2,
            ActiveTasks = 5,
            TotalCostUsd = 12.50,
            CapturedAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(500, metrics.TotalLlmCalls);
        Assert.Equal(200, metrics.TotalToolExecutions);
        Assert.Equal(100, metrics.TotalTaskExecutions);
        Assert.Equal(10, metrics.TotalCrewExecutions);
        Assert.Equal(2, metrics.ActiveCrews);
        Assert.Equal(5, metrics.ActiveTasks);
        Assert.Equal(12.50, metrics.TotalCostUsd, precision: 2);
    }

    [Fact]
    public void ShouldCreateDefaultAggregatedMetrics_WhenUsingDefaults()
    {
        // Act
        var metrics = new AggregatedMetrics();

        // Assert
        Assert.Equal(0, metrics.TotalLlmCalls);
        Assert.Equal(0, metrics.ActiveCrews);
        Assert.Equal(0.0, metrics.TotalCostUsd);
    }

    [Fact]
    public void ShouldCreateMetricsSnapshot_WhenPopulatingFields()
    {
        // Act
        var snapshot = new MetricsSnapshot
        {
            Metrics = new AggregatedMetrics { TotalLlmCalls = 42 },
            CountersByName = new Dictionary<string, long> { ["llm.calls"] = 42 },
            HistogramAverages = new Dictionary<string, double> { ["llm.latency"] = 150.5 },
            SnapshotAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(42, snapshot.Metrics.TotalLlmCalls);
        Assert.Single(snapshot.CountersByName);
        Assert.Single(snapshot.HistogramAverages);
    }

    [Fact]
    public void ShouldCreateTraceInfo_WhenPopulatingFields()
    {
        // Act
        var trace = new TraceInfo
        {
            TraceId = "trace-1",
            OperationName = "ExecuteCrew",
            StartTime = DateTime.UtcNow,
            Duration = TimeSpan.FromSeconds(5),
            Status = "OK",
            SpanCount = 3
        };

        // Assert
        Assert.Equal("trace-1", trace.TraceId);
        Assert.Equal("ExecuteCrew", trace.OperationName);
        Assert.Equal("OK", trace.Status);
        Assert.Equal(3, trace.SpanCount);
    }

    [Fact]
    public void ShouldCreateSpanInfo_WhenPopulatingFields()
    {
        // Act
        var span = new SpanInfo
        {
            SpanId = "span-1",
            OperationName = "LlmCall",
            StartTime = DateTime.UtcNow,
            Duration = TimeSpan.FromMilliseconds(200),
            ParentSpanId = "root",
            Tags = new Dictionary<string, string> { ["model"] = ModelGpt4 }
        };

        // Assert
        Assert.Equal("span-1", span.SpanId);
        Assert.Equal("root", span.ParentSpanId);
        Assert.Single(span.Tags);
    }

    [Fact]
    public void ShouldCreateTraceSearchCriteria_WhenUsingDefaults()
    {
        // Act
        var criteria = new TraceSearchCriteria();

        // Assert
        Assert.Null(criteria.OperationName);
        Assert.Null(criteria.From);
        Assert.Null(criteria.To);
        Assert.Null(criteria.Status);
        Assert.Equal(50, criteria.Limit);
    }

    // ══════════════ IFlowEngine — FlowValidationResult ══════════════

    [Fact]
    public void ShouldCreateValidResult_WhenCallingValidFactory()
    {
        // Act
        var result = FlowValidationResult.Valid();

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ShouldCreateInvalidResult_WhenCallingInvalidFactory()
    {
        // Act
        var result = FlowValidationResult.Invalid("Missing step", "Cyclic dependency");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("Missing step", result.Errors);
        Assert.Contains("Cyclic dependency", result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ShouldCreateValidWithWarnings_WhenCallingWithWarningsFactory()
    {
        // Act
        var result = FlowValidationResult.WithWarnings("Slow step detected");

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Single(result.Warnings);
    }

    // ══════════════ IFlowEngine — FlowMetrics ══════════════

    [Fact]
    public void ShouldCalculateSuccessRate_WhenFlowHasExecutions()
    {
        // Act
        var metrics = new FlowMetrics(
            FlowName: "research",
            TotalExecutions: 10,
            SuccessfulExecutions: 8,
            FailedExecutions: 2,
            AverageExecutionTime: TimeoutQuick,
            LastExecution: DateTime.UtcNow,
            StepExecutionCounts: new Dictionary<string, int> { ["step1"] = 10 });

        // Assert
        Assert.Equal(0.8, metrics.SuccessRate, precision: 5);
    }

    [Fact]
    public void ShouldReturnZeroSuccessRate_WhenNoExecutions()
    {
        // Act
        var metrics = new FlowMetrics(
            FlowName: null,
            TotalExecutions: 0,
            SuccessfulExecutions: 0,
            FailedExecutions: 0,
            AverageExecutionTime: TimeSpan.Zero,
            LastExecution: null,
            StepExecutionCounts: new Dictionary<string, int>());

        // Assert
        Assert.Equal(0.0, metrics.SuccessRate);
    }

    // ══════════════ ITemplateEngine — TemplateValidation ══════════════

    [Fact]
    public void ShouldCreateValidTemplate_WhenCallingValidFactory()
    {
        // Act
        var v = TemplateValidation.Valid("param1", "param2");

        // Assert
        Assert.True(v.IsValid);
        Assert.Empty(v.Errors);
        Assert.Equal(2, v.Parameters.Count);
        Assert.Empty(v.Warnings);
    }

    [Fact]
    public void ShouldCreateInvalidTemplate_WhenCallingInvalidFactory()
    {
        // Act
        var v = TemplateValidation.Invalid("Unclosed tag", "Unknown filter");

        // Assert
        Assert.False(v.IsValid);
        Assert.Equal(2, v.Errors.Count);
        Assert.Empty(v.Parameters);
    }

    // ══════════════ ITemplateInstantiator — InstantiationValidation ══════════════

    [Fact]
    public void ShouldCreateValidInstantiation_WhenCallingValidFactory()
    {
        // Act
        var v = InstantiationValidation.Valid();

        // Assert
        Assert.True(v.IsValid);
        Assert.Empty(v.Errors);
        Assert.Empty(v.MissingParameters);
    }

    [Fact]
    public void ShouldCreateInvalidInstantiation_WhenCallingInvalidFactory()
    {
        // Act
        var v = InstantiationValidation.Invalid("param 'role' required");

        // Assert
        Assert.False(v.IsValid);
        Assert.Single(v.Errors);
    }

    // ══════════════ TemplateType enum ══════════════

    [Theory]
    [InlineData(TemplateType.Agent)]
    [InlineData(TemplateType.Task)]
    [InlineData(TemplateType.Both)]
    public void ShouldContainExpectedValue_WhenCheckingTemplateTypeEnum(TemplateType type)
    {
        Assert.True(Enum.IsDefined<TemplateType>(type));
    }
}
