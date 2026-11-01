namespace Orkeon.Constants.Llm;

/// <summary>
/// The keys of the <see cref="ModelProviderCategory.Decision"/> providers. Kept apart from
/// <see cref="LlmProviderKeys"/>: a decision provider is not an LLM provider, and the provider
/// factory must never build one for a chat call.
/// </summary>
public static class DecisionProviderKeys
{
    /// <summary>TypeSafe, which serves the Jev System One models.</summary>
    public const string TypeSafe = "typesafe";

    /// <summary>Liquid, which serves the d1 System One models.</summary>
    public const string Liquid = "liquid";

    /// <summary>Kev, a System One server run locally.</summary>
    public const string Kev = "kev";

    /// <summary>Every decision provider key, in the order the documentation lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [TypeSafe, Liquid, Kev];
}
