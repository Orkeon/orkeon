using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Application.Training;

/// <summary>
/// Base class for training-related parameters.
/// </summary>
public abstract class TrainingParametersBase
{
    /// <summary>Gets the backing parameter entries.</summary>
    protected ImmutableDictionary<string, TrainingParameterValue> Parameters { get; }

    /// <summary>Initializes a new instance of <see cref="TrainingParametersBase"/>.</summary>
    protected TrainingParametersBase(ImmutableDictionary<string, TrainingParameterValue> parameters)
    {
        Parameters = parameters ?? [];
    }

    /// <summary>
    /// Get.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!Parameters.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>
    /// Get Required.
    /// </summary>
    public T GetRequired<T>(string key)
    {
        if (!Parameters.TryGetValue(key, out var value))
            throw new ArgumentException($"Required parameter '{key}' not found", nameof(key));

        return value.GetValue<T>();
    }

    /// <summary>
    /// Contains.
    /// </summary>
    public bool Contains(string key) => Parameters.ContainsKey(key);
    /// <summary>
    /// Gets the keys.
    /// </summary>
    public IEnumerable<string> Keys => Parameters.Keys;
    /// <summary>
    /// Gets the number of entries.
    /// </summary>
    public int Count => Parameters.Count;

    /// <summary>
    /// To Dictionary.
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in Parameters)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }
}

/// <summary>
/// Training plan custom parameters.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class TrainingPlanParameters : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="TrainingPlanParameters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Max Retries.
        /// </summary>
        [DictionaryEntry("maxRetries")]
        public partial Builder AddMaxRetries(int value);

        /// <summary>
        /// Add Adaptive Difficulty.
        /// </summary>
        [DictionaryEntry("adaptiveDifficulty")]
        public partial Builder AddAdaptiveDifficulty(bool value);

        /// <summary>
        /// Add Feedback Mode.
        /// </summary>
        [DictionaryEntry("feedbackMode")]
        public partial Builder AddFeedbackMode(string mode);
    }
}

/// <summary>
/// Training module resources.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class TrainingResources : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="TrainingResources"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Documentation Url.
        /// </summary>
        [DictionaryEntry("documentationUrl")]
        public partial Builder AddDocumentationUrl(Uri url);

        /// <summary>
        /// Add Video Urls.
        /// </summary>
        [DictionaryEntry("videoUrls")]
        public partial Builder AddVideoUrls(IReadOnlyList<string> urls);

        /// <summary>
        /// Add Example Code.
        /// </summary>
        [DictionaryEntry("exampleCode")]
        public partial Builder AddExampleCode(string code);

        /// <summary>
        /// Add Dataset Path.
        /// </summary>
        [DictionaryEntry("datasetPath")]
        public partial Builder AddDatasetPath(string path);
    }
}

/// <summary>
/// Training success metrics.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class TrainingSuccessMetrics : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="TrainingSuccessMetrics"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Accuracy Threshold.
        /// </summary>
        [DictionaryEntry("accuracyThreshold")]
        public partial Builder AddAccuracyThreshold(double value);

        /// <summary>
        /// Add Completion Time Limit.
        /// </summary>
        [DictionaryEntry("completionTimeLimit")]
        public partial Builder AddCompletionTimeLimit(TimeSpan value);

        /// <summary>
        /// Add Minimum Score.
        /// </summary>
        [DictionaryEntry("minimumScore")]
        public partial Builder AddMinimumScore(double value);

        /// <summary>
        /// Add Required Tool Usage.
        /// </summary>
        [DictionaryEntry("requiredToolUsage")]
        public partial Builder AddRequiredToolUsage(IReadOnlyList<string> tools);
    }
}

/// <summary>
/// Training recommendation context.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class TrainingRecommendationContext : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="TrainingRecommendationContext"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Previous Attempts.
        /// </summary>
        [DictionaryEntry("previousAttempts")]
        public partial Builder AddPreviousAttempts(int value);

        /// <summary>
        /// Add Time Constraint.
        /// </summary>
        [DictionaryEntry("timeConstraint")]
        public partial Builder AddTimeConstraint(TimeSpan value);

        /// <summary>
        /// Add Preferred Difficulty.
        /// </summary>
        [DictionaryEntry("preferredDifficulty")]
        public partial Builder AddPreferredDifficulty(string difficulty);
    }
}

/// <summary>
/// Training statistics collection.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class TrainingStatistics : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="TrainingStatistics"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Total Attempts.
        /// </summary>
        [DictionaryEntry("totalAttempts")]
        public partial Builder AddTotalAttempts(int value);

        /// <summary>
        /// Add Success Rate.
        /// </summary>
        [DictionaryEntry("successRate")]
        public partial Builder AddSuccessRate(double value);

        /// <summary>
        /// Add Average Score.
        /// </summary>
        [DictionaryEntry("averageScore")]
        public partial Builder AddAverageScore(double value);

        /// <summary>
        /// Add Improvement Rate.
        /// </summary>
        [DictionaryEntry("improvementRate")]
        public partial Builder AddImprovementRate(double value);
    }
}

/// <summary>
/// Scenario performance data.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class ScenarioPerformanceData : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="ScenarioPerformanceData"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Tasks Completed.
        /// </summary>
        [DictionaryEntry("tasksCompleted")]
        public partial Builder AddTasksCompleted(int value);

        /// <summary>
        /// Add Error Count.
        /// </summary>
        [DictionaryEntry("errorCount")]
        public partial Builder AddErrorCount(int value);

        /// <summary>
        /// Add Tool Efficiency.
        /// </summary>
        [DictionaryEntry("toolEfficiency")]
        public partial Builder AddToolEfficiency(double value);
    }
}

/// <summary>
/// Achievement criteria data.
/// </summary>
[TypedDictionary(typeof(TrainingParameterValue))]
public sealed partial class AchievementCriteriaData : TrainingParametersBase
{
    /// <summary>Builder for constructing <see cref="AchievementCriteriaData"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Required Score.
        /// </summary>
        [DictionaryEntry("requiredScore")]
        public partial Builder AddRequiredScore(double value);

        /// <summary>
        /// Add Completion Count.
        /// </summary>
        [DictionaryEntry("completionCount")]
        public partial Builder AddCompletionCount(int value);

        /// <summary>
        /// Add Time Limit.
        /// </summary>
        [DictionaryEntry("timeLimit")]
        public partial Builder AddTimeLimit(TimeSpan value);
    }
}

/// <summary>
/// Training parameter value wrapper.
/// </summary>
public sealed class TrainingParameterValue
{
    private readonly object _value;
    private readonly Type _type;

    private TrainingParameterValue(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
    }

    /// <summary>
    /// Get Value.
    /// </summary>
    public T GetValue<T>()
    {
        if (_value is T typedValue)
            return typedValue;

        try
        {
            return (T)Convert.ChangeType(_value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"Cannot convert training parameter value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>
    /// Raw Value.
    /// </summary>
    public object RawValue => _value;
    /// <summary>
    /// Value Type.
    /// </summary>
    public Type ValueType => _type;

    /// <summary>
    /// From.
    /// </summary>
    public static TrainingParameterValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new TrainingParameterValue(value, value.GetType());
    }
}
