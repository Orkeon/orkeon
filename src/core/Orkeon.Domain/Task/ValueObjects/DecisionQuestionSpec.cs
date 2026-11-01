using System.Collections.Immutable;
using Orkeon.Constants.Llm;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>Equality by content for the collections a value object holds.</summary>
internal static class ContentEquality
{
    internal static bool Same<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> left, IReadOnlyDictionary<TKey, TValue> right)
        where TKey : notnull =>
        left.Count == right.Count
        && left.All(kv => right.TryGetValue(kv.Key, out var value) && EqualityComparer<TValue>.Default.Equals(kv.Value, value));

    internal static int Hash<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary)
        where TKey : notnull =>
        dictionary.Aggregate(0, (hash, kv) => hash ^ HashCode.Combine(kv.Key, kv.Value));

    internal static int Hash<T>(IEnumerable<T> items)
    {
        var hash = default(HashCode);
        foreach (var item in items)
            hash.Add(item);
        return hash.ToHashCode();
    }
}

/// <summary>A question a decision task asks.</summary>
public abstract record DecisionQuestionSpec
{
    /// <summary>Gets the question name.</summary>
    public required string Name { get; init; }

    internal abstract void Validate();
}

/// <summary>A choice among named options.</summary>
public sealed record ChoiceQuestionSpec : DecisionQuestionSpec
{
    /// <summary>Gets the instructions.</summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>Gets the options, name to description.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>Creates a choice question.</summary>
    public static ChoiceQuestionSpec Create(string name, string instructions, IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var spec = new ChoiceQuestionSpec { Name = name, Instructions = instructions, Options = options.ToImmutableDictionary() };
        spec.Validate();
        return spec;
    }

    /// <inheritdoc />
    public bool Equals(ChoiceQuestionSpec? other) =>
        other is not null && Name == other.Name && Instructions == other.Instructions && ContentEquality.Same(Options, other.Options);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Instructions, ContentEquality.Hash(Options));

    internal override void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (Options.Count == 0 || Options.Count > DecisionLimits.MaxChoiceOptions)
            throw new ArgumentException($"A choice question holds 1 to {DecisionLimits.MaxChoiceOptions} options.", nameof(Options));
    }
}

/// <summary>A score on ordered levels.</summary>
public sealed record ScoreQuestionSpec : DecisionQuestionSpec
{
    /// <summary>Gets the instructions.</summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>Gets the levels.</summary>
    public IReadOnlyList<string> Levels { get; init; } = ImmutableList<string>.Empty;

    /// <summary>Creates a score question.</summary>
    public static ScoreQuestionSpec Create(string name, string instructions, IReadOnlyList<string> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var spec = new ScoreQuestionSpec { Name = name, Instructions = instructions, Levels = levels.ToImmutableList() };
        spec.Validate();
        return spec;
    }

    /// <inheritdoc />
    public bool Equals(ScoreQuestionSpec? other) =>
        other is not null && Name == other.Name && Instructions == other.Instructions && Levels.SequenceEqual(other.Levels);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Instructions, ContentEquality.Hash(Levels));

    internal override void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (Levels.Count == 0 || Levels.Count > DecisionLimits.MaxScoreLevels)
            throw new ArgumentException($"A score question holds 1 to {DecisionLimits.MaxScoreLevels} levels.", nameof(Levels));
        var repeated = Levels.GroupBy(l => l, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
            throw new ArgumentException($"A score question's levels are distinct: '{repeated.Key}' repeats.", nameof(Levels));
    }
}

/// <summary>A yes/no question.</summary>
public sealed record YesNoQuestionSpec : DecisionQuestionSpec
{
    /// <summary>Gets the statement.</summary>
    public string Statement { get; init; } = string.Empty;

    /// <summary>Creates a yes/no question.</summary>
    public static YesNoQuestionSpec Create(string name, string statement)
    {
        var spec = new YesNoQuestionSpec { Name = name, Statement = statement };
        spec.Validate();
        return spec;
    }

    internal override void Validate() => ArgumentException.ThrowIfNullOrWhiteSpace(Name);
}
