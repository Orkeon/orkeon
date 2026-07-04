using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// Task complexity value object with detailed metrics.
/// </summary>
public sealed record TaskComplexity : ValueObjectRecord
{
    /// <summary>Estimated effort in story points for a simple task.</summary>
    public const int SimpleEffort = 1;

    /// <summary>Estimated effort in story points for a medium complexity task.</summary>
    public const int MediumEffort = 3;

    /// <summary>Estimated effort in story points for a complex task.</summary>
    public const int ComplexEffort = 8;

    /// <summary>Gets the complexity level.</summary>
    public ComplexityLevel Level { get; init; }
    /// <summary>Gets the estimated effort in story points.</summary>
    public int EstimatedEffort { get; init; }
    /// <summary>Gets the estimated duration.</summary>
    public TimeSpan EstimatedDuration { get; init; }
    /// <summary>Gets the list of required skills.</summary>
    public ImmutableList<string> RequiredSkills { get; init; }
    /// <summary>Gets the list of task dependencies.</summary>
    public ImmutableList<string> Dependencies { get; init; }

    /// <summary>Initializes a new <see cref="TaskComplexity"/>.</summary>
    /// <param name="level">The complexity level.</param>
    /// <param name="estimatedEffort">The estimated effort in story points (must be positive).</param>
    /// <param name="estimatedDuration">The estimated duration (must be positive).</param>
    /// <param name="requiredSkills">Optional list of required skills.</param>
    /// <param name="dependencies">Optional list of dependencies.</param>
    private TaskComplexity(
        ComplexityLevel level,
        int estimatedEffort,
        TimeSpan estimatedDuration,
        IEnumerable<string>? requiredSkills = null,
        IEnumerable<string>? dependencies = null)
    {
        Level = level;
        EstimatedEffort = estimatedEffort > 0 ? estimatedEffort : throw new ArgumentException("Effort must be positive", nameof(estimatedEffort));
        EstimatedDuration = estimatedDuration > TimeSpan.Zero ? estimatedDuration : throw new ArgumentException("Duration must be positive", nameof(estimatedDuration));
        RequiredSkills = (requiredSkills ?? []).ToImmutableList();
        Dependencies = (dependencies ?? []).ToImmutableList();
    }

    /// <summary>Creates a new <see cref="TaskComplexity"/>.</summary>
    /// <param name="level">The complexity level.</param>
    /// <param name="estimatedEffort">The estimated effort in story points (must be positive).</param>
    /// <param name="estimatedDuration">The estimated duration (must be positive).</param>
    /// <param name="requiredSkills">Optional list of required skills.</param>
    /// <param name="dependencies">Optional list of dependencies.</param>
    /// <returns>A new <see cref="TaskComplexity"/>.</returns>
    public static TaskComplexity Create(
        ComplexityLevel level,
        int estimatedEffort,
        TimeSpan estimatedDuration,
        IEnumerable<string>? requiredSkills = null,
        IEnumerable<string>? dependencies = null) =>
        new(level, estimatedEffort, estimatedDuration, requiredSkills, dependencies);

    /// <summary>Returns whether the specified skill is required.</summary>
    /// <param name="skill">The skill name.</param>
    /// <returns><see langword="true"/> if required; otherwise <see langword="false"/>.</returns>
    public bool RequiresSkill(string skill) => RequiredSkills.Contains(skill, StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets whether this task has dependencies.</summary>
    public bool HasDependencies => Dependencies.Count > 0;

    /// <summary>Creates a simple task complexity.</summary>
    /// <param name="duration">The estimated duration.</param>
    /// <returns>A simple <see cref="TaskComplexity"/>.</returns>
    public static TaskComplexity Simple(TimeSpan duration) => new(ComplexityLevel.Simple, SimpleEffort, duration);
    /// <summary>Creates a medium task complexity.</summary>
    /// <param name="duration">The estimated duration.</param>
    /// <param name="skills">Optional required skills.</param>
    /// <returns>A medium <see cref="TaskComplexity"/>.</returns>
    public static TaskComplexity Medium(TimeSpan duration, params string[] skills) => new(ComplexityLevel.Medium, MediumEffort, duration, skills);
    /// <summary>Creates a complex task complexity.</summary>
    /// <param name="duration">The estimated duration.</param>
    /// <param name="skills">Required skills.</param>
    /// <param name="dependencies">Task dependencies.</param>
    /// <returns>A complex <see cref="TaskComplexity"/>.</returns>
    public static TaskComplexity Complex(TimeSpan duration, IEnumerable<string> skills, IEnumerable<string> dependencies) =>
        new(ComplexityLevel.Complex, ComplexEffort, duration, skills, dependencies);

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Level} ({EstimatedEffort} pts, {EstimatedDuration.TotalMinutes:F0}m, {RequiredSkills.Count} skills)");
}

/// <summary>Complexity level of a task.</summary>
public sealed record ComplexityLevel
{
    /// <summary>Gets the string value of this complexity level.</summary>
    public string Value { get; }
    private ComplexityLevel(string value) => Value = value;

    /// <summary>Simple complexity, minimal effort required.</summary>
    public static readonly ComplexityLevel Simple = new("Simple");
    /// <summary>Medium complexity, moderate effort required.</summary>
    public static readonly ComplexityLevel Medium = new("Medium");
    /// <summary>Complex task requiring significant effort and skills.</summary>
    public static readonly ComplexityLevel Complex = new("Complex");
    /// <summary>Very complex task requiring extensive effort and multiple dependencies.</summary>
    public static readonly ComplexityLevel VeryComplex = new("VeryComplex");

    private static readonly Dictionary<string, ComplexityLevel> s_all = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Simple)] = Simple,
        [nameof(Medium)] = Medium,
        [nameof(Complex)] = Complex,
        [nameof(VeryComplex)] = VeryComplex,
    };

    /// <summary>Gets all known complexity levels.</summary>
    public static IReadOnlyCollection<ComplexityLevel> All => s_all.Values;

    /// <summary>Returns the <see cref="ComplexityLevel"/> matching <paramref name="value"/>, or throws if unknown.</summary>
    public static ComplexityLevel From(string value) =>
        s_all.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentException($"Unknown ComplexityLevel: '{value}'", nameof(value));

    /// <summary>Tries to parse <paramref name="value"/> into a known <see cref="ComplexityLevel"/>.</summary>
    public static bool TryFrom(string? value, out ComplexityLevel? result)
    {
        if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; }
        result = null; return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
    /// <summary>Implicitly converts a <see cref="ComplexityLevel"/> to its string value.</summary>
    public static implicit operator string(ComplexityLevel s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
