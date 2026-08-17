using System.Collections.Immutable;

namespace Orkeon.Cli.Commands.Scripting.Args;

/// <summary>
/// Strongly-typed projection of a single entry of the JS-side <c>args</c> object on a
/// <c>defineCommand({...})</c>. The closed hierarchy mirrors spec §4.3 — only the four
/// supported types (string / number / boolean / string[]).
/// </summary>
public abstract record ArgSpec
{
    /// <summary>Name of the argument (e.g. <c>"target"</c> in <c>--target=prod</c>).</summary>
    public required string Name { get; init; }

    /// <summary>JS-side <c>type</c> token (<c>"string"</c>, <c>"number"</c>, …) — used for diagnostics.</summary>
    public abstract string Kind { get; }

    /// <summary>True when the validator must reject input that doesn't carry this arg.</summary>
    public bool Required { get; init; }
}

/// <summary>String-typed argument with optional choices and default.</summary>
public sealed record StringArgSpec : ArgSpec
{
    public override string Kind => "string";
    public string? Default { get; init; }
    public ImmutableArray<string> Choices { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>Number-typed argument with optional min / max / default.</summary>
public sealed record NumberArgSpec : ArgSpec
{
    public override string Kind => "number";
    public double? Default { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
}

/// <summary>Boolean-typed argument. As a bare <c>--flag</c> (no value), <see cref="Default"/> is set to <c>true</c>.</summary>
public sealed record BooleanArgSpec : ArgSpec
{
    public override string Kind => "boolean";
    public bool? Default { get; init; }
}

/// <summary>
/// String-array argument. When matched positionally, greedily consumes the remaining
/// tokens; when matched as <c>--key v1 v2</c>, consumes until the next <c>--*</c>.
/// </summary>
public sealed record StringArrayArgSpec : ArgSpec
{
    public override string Kind => "string[]";
    public ImmutableArray<string>? Default { get; init; }
}
