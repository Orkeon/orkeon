using System.Collections.Immutable;
using Orkeon.Generators;

namespace Orkeon.Application.Training;

/// <summary>
/// Simulation metrics collection.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class SimulationMetrics : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="SimulationMetrics"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Execution Time.
        /// </summary>
        [DictionaryEntry("executionTime")]
        public partial Builder AddExecutionTime(TimeSpan value);

        /// <summary>
        /// Add Memory Usage.
        /// </summary>
        [DictionaryEntry("memoryUsage")]
        public partial Builder AddMemoryUsage(long bytes);

        /// <summary>
        /// Add Decision Count.
        /// </summary>
        [DictionaryEntry("decisionCount")]
        public partial Builder AddDecisionCount(int value);

        /// <summary>
        /// Add Tool Call Count.
        /// </summary>
        [DictionaryEntry("toolCallCount")]
        public partial Builder AddToolCallCount(int value);
    }
}

/// <summary>
/// Readiness assessment detailed metrics.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class ReadinessDetailedMetrics : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="ReadinessDetailedMetrics"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Skill Readiness Score.
        /// </summary>
        [DictionaryEntry("skillReadinessScore")]
        public partial Builder AddSkillReadinessScore(double value);

        /// <summary>
        /// Add Experience Level.
        /// </summary>
        [DictionaryEntry("experienceLevel")]
        public partial Builder AddExperienceLevel(int value);

        /// <summary>
        /// Add Consistency Score.
        /// </summary>
        [DictionaryEntry("consistencyScore")]
        public partial Builder AddConsistencyScore(double value);

        /// <summary>
        /// Add Reliability Score.
        /// </summary>
        [DictionaryEntry("reliabilityScore")]
        public partial Builder AddReliabilityScore(double value);
    }
}

/// <summary>
/// Scenario initial context parameters.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class ScenarioInitialContext : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="ScenarioInitialContext"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Environment.
        /// </summary>
        [DictionaryEntry("environment")]
        public partial Builder AddEnvironment(string environment);

        /// <summary>
        /// Add Starting State.
        /// </summary>
        [DictionaryEntry("startingState")]
        public partial Builder AddStartingState(string state);

        /// <summary>
        /// Add Available Resources.
        /// </summary>
        [DictionaryEntry("availableResources")]
        public partial Builder AddAvailableResources(IReadOnlyList<string> resources);

        /// <summary>
        /// Add Constraints.
        /// </summary>
        [DictionaryEntry("constraints")]
        public partial Builder AddConstraints(IReadOnlyList<string> constraints);
    }
}

/// <summary>
/// Scenario success conditions.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class ScenarioSuccessConditions : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="ScenarioSuccessConditions"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Goal State.
        /// </summary>
        [DictionaryEntry("goalState")]
        public partial Builder AddGoalState(string goalState);

        /// <summary>
        /// Add Required Outcomes.
        /// </summary>
        [DictionaryEntry("requiredOutcomes")]
        public partial Builder AddRequiredOutcomes(IReadOnlyList<string> outcomes);

        /// <summary>
        /// Add Minimum Score.
        /// </summary>
        [DictionaryEntry("minimumScore")]
        public partial Builder AddMinimumScore(double score);

        /// <summary>
        /// Add Time Limit.
        /// </summary>
        [DictionaryEntry("timeLimit")]
        public partial Builder AddTimeLimit(TimeSpan limit);
    }
}

/// <summary>
/// Scenario metadata.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class ScenarioMetadata : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="ScenarioMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Author.
        /// </summary>
        [DictionaryEntry("author")]
        public partial Builder AddAuthor(string author);

        /// <summary>
        /// Add Version.
        /// </summary>
        [DictionaryEntry("version")]
        public partial Builder AddVersion(string version);

        /// <summary>
        /// Add Created At.
        /// </summary>
        [DictionaryEntry("createdAt")]
        public partial Builder AddCreatedAt(DateTime date);

        /// <summary>
        /// Add Last Modified.
        /// </summary>
        [DictionaryEntry("lastModified")]
        public partial Builder AddLastModified(DateTime date);
    }
}

/// <summary>
/// Task definition parameters.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class TaskDefinitionParameters : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="TaskDefinitionParameters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Input.
        /// </summary>
        [DictionaryEntry("input")]
        public partial Builder AddInput(string input);

        /// <summary>
        /// Add Expected Output.
        /// </summary>
        [DictionaryEntry("expectedOutput")]
        public partial Builder AddExpectedOutput(string output);

        /// <summary>
        /// Add Tools Required.
        /// </summary>
        [DictionaryEntry("toolsRequired")]
        public partial Builder AddToolsRequired(IReadOnlyList<string> tools);

        /// <summary>
        /// Add Validation Rules.
        /// </summary>
        [DictionaryEntry("validationRules")]
        public partial Builder AddValidationRules(IReadOnlyList<string> rules);
    }
}

/// <summary>
/// Analytics custom filters.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class AnalyticsCustomFilters : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="AnalyticsCustomFilters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Min Score.
        /// </summary>
        [DictionaryEntry("minScore")]
        public partial Builder AddMinScore(double value);

        /// <summary>
        /// Add Max Duration.
        /// </summary>
        [DictionaryEntry("maxDuration")]
        public partial Builder AddMaxDuration(TimeSpan value);

        /// <summary>
        /// Add Difficulty Level.
        /// </summary>
        [DictionaryEntry("difficultyLevel")]
        public partial Builder AddDifficultyLevel(string level);

        /// <summary>
        /// Add Team Size.
        /// </summary>
        [DictionaryEntry("teamSize")]
        public partial Builder AddTeamSize(int size);
    }
}

/// <summary>
/// Training analytics custom metrics.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class TrainingCustomMetrics : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="TrainingCustomMetrics"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Engagement Score.
        /// </summary>
        [DictionaryEntry("engagementScore")]
        public partial Builder AddEngagementScore(double value);

        /// <summary>
        /// Add Collaboration Index.
        /// </summary>
        [DictionaryEntry("collaborationIndex")]
        public partial Builder AddCollaborationIndex(double value);

        /// <summary>
        /// Add Innovation Score.
        /// </summary>
        [DictionaryEntry("innovationScore")]
        public partial Builder AddInnovationScore(double value);
    }
}

/// <summary>
/// Training insight supporting data.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class InsightSupportingData : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="InsightSupportingData"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Data Points.
        /// </summary>
        [DictionaryEntry("dataPoints")]
        public partial Builder AddDataPoints(IReadOnlyList<double> points);

        /// <summary>
        /// Add Trend Analysis.
        /// </summary>
        [DictionaryEntry("trendAnalysis")]
        public partial Builder AddTrendAnalysis(string analysis);

        /// <summary>
        /// Add Correlation Factor.
        /// </summary>
        [DictionaryEntry("correlationFactor")]
        public partial Builder AddCorrelationFactor(double value);
    }
}

/// <summary>
/// Team objective success criteria.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class TeamObjectiveSuccessCriteria : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="TeamObjectiveSuccessCriteria"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Completion Rate.
        /// </summary>
        [DictionaryEntry("completionRate")]
        public partial Builder AddCompletionRate(double value);

        /// <summary>
        /// Add Quality Score.
        /// </summary>
        [DictionaryEntry("qualityScore")]
        public partial Builder AddQualityScore(double value);

        /// <summary>
        /// Add Time Efficiency.
        /// </summary>
        [DictionaryEntry("timeEfficiency")]
        public partial Builder AddTimeEfficiency(double value);

        /// <summary>
        /// Add Collaboration Score.
        /// </summary>
        [DictionaryEntry("collaborationScore")]
        public partial Builder AddCollaborationScore(double value);
    }
}

/// <summary>
/// Simulation environment variables.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class SimulationEnvironmentVariables : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="SimulationEnvironmentVariables"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Api Endpoint.
        /// </summary>
        [DictionaryEntry("apiEndpoint")]
        public partial Builder AddApiEndpoint(string endpoint);

        /// <summary>
        /// Add Data Source.
        /// </summary>
        [DictionaryEntry("dataSource")]
        public partial Builder AddDataSource(string source);

        /// <summary>
        /// Add Simulation Seed.
        /// </summary>
        [DictionaryEntry("simulationSeed")]
        public partial Builder AddSimulationSeed(int seed);

        /// <summary>
        /// Add Debug Mode.
        /// </summary>
        [DictionaryEntry("debugMode")]
        public partial Builder AddDebugMode(bool debug);
    }
}

/// <summary>
/// Simulation event data.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class SimulationEventData : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="SimulationEventData"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Event Id.
        /// </summary>
        [DictionaryEntry("eventId")]
        public partial Builder AddEventId(string id);

        /// <summary>
        /// Add Source Agent.
        /// </summary>
        [DictionaryEntry("sourceAgent")]
        public partial Builder AddSourceAgent(string agentId);

        /// <summary>
        /// Add Payload.
        /// </summary>
        [DictionaryEntry("payload")]
        public partial Builder AddPayload(string payload);

        /// <summary>
        /// Add Impact.
        /// </summary>
        [DictionaryEntry("impact")]
        public partial Builder AddImpact(string impact);
    }
}

/// <summary>
/// Data point metadata.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class DataPointMetadata : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="DataPointMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Source.
        /// </summary>
        [DictionaryEntry("source")]
        public partial Builder AddSource(string source);

        /// <summary>
        /// Add Confidence.
        /// </summary>
        [DictionaryEntry("confidence")]
        public partial Builder AddConfidence(double confidence);

        /// <summary>
        /// Add Context.
        /// </summary>
        [DictionaryEntry("context")]
        public partial Builder AddContext(string context);
    }
}
