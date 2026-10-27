using System.Globalization;
using Jint.Native;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Scripting.Exceptions;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Converts the untyped <c>crewBuilder().budget({...})</c> spec dictionary into a typed
/// <see cref="AgentExecutionBudget"/>. Returns <c>null</c> when the spec carries no
/// recognized dimension, so crews without a budget keep the pre-F1 behaviour (no
/// enforcement) byte-for-byte.
/// </summary>
internal static class JsBudgetBridge
{
    /// <summary>
    /// Recognized keys: <c>toolCalls</c>, <c>tokens</c> (the running sum of every call's prompt and
    /// completion), <c>contextTokens</c> (the context counted once, #224: <see cref="AgentExecutionBudget.MaxContextTokens"/>),
    /// <c>delegationDepth</c>, <c>spawnedAgents</c> (integers) and <c>wallTime</c> (number = seconds, string =
    /// duration with unit <c>ms|s|m|h</c>). Invalid values are ignored — a script must
    /// never crash at build time because of a malformed budget field. With
    /// <paramref name="chargedTo"/>, the budget also charges that one (#189) and reads its clock. A spec
    /// that sets <c>contextTokens</c> without <c>tokens</c> leaves the running sum uncapped: the default
    /// sum would otherwise cut the run short of the context it asked for.
    /// </summary>
    internal static AgentExecutionBudget? FromSpec(IReadOnlyDictionary<string, object?>? spec, AgentExecutionBudget? chargedTo = null)
    {
        if (spec is null || spec.Count == 0) return null;

        int? toolCalls = TryReadInt(spec, "toolCalls");
        int? tokens = TryReadInt(spec, "tokens");
        int? contextTokens = TryReadInt(spec, ContextTokensKey);
        int? delegationDepth = TryReadInt(spec, "delegationDepth");
        int? spawnedAgents = TryReadInt(spec, "spawnedAgents");
        TimeSpan? wallTime = TryReadWallTime(spec);

        if (toolCalls is null && tokens is null && contextTokens is null && delegationDepth is null &&
            spawnedAgents is null && wallTime is null)
            return null;

        var defaults = AgentExecutionBudget.Default;
        return new AgentExecutionBudget
        {
            MaxToolCalls = toolCalls ?? defaults.MaxToolCalls,
            MaxTokensConsumed = tokens ?? (contextTokens is null ? defaults.MaxTokensConsumed : int.MaxValue),
            MaxContextTokens = contextTokens ?? defaults.MaxContextTokens,
            MaxDelegationDepth = delegationDepth ?? defaults.MaxDelegationDepth,
            MaxSpawnedAgents = spawnedAgents ?? defaults.MaxSpawnedAgents,
            MaxWallTime = wallTime ?? defaults.MaxWallTime,
            TimeProvider = chargedTo?.TimeProvider ?? TimeProvider.System,
            ChargedTo = chargedTo,
        };
    }

    /// <summary>
    /// The <c>crewBuilder().budget({...})</c> spec of a declarative autonomous crew as the crew's budget
    /// (#218): a dimension left out keeps the crew's default - no token cap and no wall time. A limit that
    /// is not 1 or more - zero, a negative or fractional count, a wall time it cannot read - is refused
    /// with an <see cref="InvalidScriptException"/>, as YAML <c>budget:</c> and C# <c>Crew.Create</c>
    /// refuse it, instead of leaving the run unbounded. Null when the spec carries no dimension.
    /// </summary>
    internal static CrewBudget? ToCrewBudget(IReadOnlyDictionary<string, object?>? spec)
    {
        if (spec is null || spec.Count == 0) return null;

        // The autonomous strategy records what a finished task spent, never a call's prompt and
        // completion: it cannot count the context once, so the key is refused rather than read as the sum.
        if (spec.TryGetValue(ContextTokensKey, out var context) && context is not null)
            throw new InvalidScriptException(
                $"crewBuilder().budget({{...}}) budget.{ContextTokensKey}: an autonomous crew counts the tokens of each finished task, "
                + "not each call's prompt and completion, so it cannot count the context once; use tokens, the running sum.");

        foreach (var key in Dimensions)
        {
            if (spec.TryGetValue(key, out var raw) && raw is not null && !IsValidLimit(key, raw))
                throw new InvalidScriptException(
                    $"crewBuilder().budget({{...}}) budget.{key}: {Show(raw)} - every limit of a crew's budget is 1 or more "
                    + "(a whole number; wallTime in seconds or a duration such as \"90s\" or \"5m\"). Leave the key out for its default.");
        }

        var budget = new CrewBudget
        {
            ToolCalls = TryReadInt(spec, "toolCalls"),
            Tokens = TryReadInt(spec, "tokens"),
            DelegationDepth = TryReadInt(spec, "delegationDepth"),
            SpawnedAgents = TryReadInt(spec, "spawnedAgents"),
            WallTime = TryReadWallTime(spec),
        };
        return budget == CrewBudget.Unset ? null : budget;
    }

    /// <summary>The keys of a budget spec, in the order a refusal names them.</summary>
    private static readonly string[] Dimensions = ["toolCalls", "tokens", ContextTokensKey, "wallTime", "delegationDepth", "spawnedAgents"];

    /// <summary>The key of the token budget that counts the context once (#224).</summary>
    private const string ContextTokensKey = "contextTokens";

    private const string Where = "crew.runAgent / ctx.delegate options:";

    /// <summary>
    /// The <c>budget</c> option of <c>crew.runAgent</c> and <c>ctx.delegate</c> (#189): the keys and
    /// values of <c>crewBuilder().budget({...})</c>, read by <see cref="FromSpec"/>, but checked first. A
    /// run option is given at the call that spends it, so a value <see cref="FromSpec"/> would skip (an
    /// unknown key, a count that is not a whole number of 1 or more, a wall time it cannot read) is
    /// refused with an <see cref="InvalidScriptException"/> instead of leaving the run unbounded. Null
    /// for a spec with no dimension (<c>{}</c>), as for the crew.
    /// </summary>
    internal static AgentExecutionBudget? FromRunOption(JsValue value, AgentExecutionBudget? chargedTo)
    {
        if (value is not JsObject spec || spec.ToObject() is not IDictionary<string, object?> fields)
            throw new InvalidScriptException($"{Where} budget must be an object such as {{ toolCalls: 20, tokens: 50000, wallTime: 300 }}.");

        foreach (var (key, raw) in fields)
        {
            if (Array.IndexOf(Dimensions, key) < 0)
                throw new InvalidScriptException(
                    $"{Where} budget.{key} is not a budget dimension; expected one of {string.Join(", ", Dimensions)}.");
            if (raw is not null && IsValidLimit(key, raw))
                continue;
            if (key == "wallTime")
                throw new InvalidScriptException(
                    $"{Where} budget.wallTime must be a positive number of seconds or a duration such as \"90s\" or \"5m\"; got {Show(raw)}.");
            throw new InvalidScriptException($"{Where} budget.{key} must be a whole number of 1 or more; got {Show(raw)}.");
        }

        return FromSpec(fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal), chargedTo);
    }

    /// <summary>
    /// Whether <paramref name="raw"/> is a valid limit for <paramref name="key"/>: a whole number of 1 or
    /// more, or for <c>wallTime</c> a positive number of seconds or a duration <see cref="ParseDuration"/> reads.
    /// </summary>
    private static bool IsValidLimit(string key, object raw)
    {
        var number = AsNumber(raw);
        if (key == "wallTime")
            return raw is string text ? ParseDuration(text) is not null : number is { } seconds && double.IsFinite(seconds) && seconds > 0;
        return number is { } count && double.IsFinite(count) && count >= 1 && Math.Floor(count) == count;
    }

    private static double? AsNumber(object? raw) => raw switch
    {
        double d => d,
        int i => i,
        long l => l,
        _ => null,
    };

    private static string Show(object? raw) => raw switch
    {
        null => "null",
        string text => $"\"{text}\"",
        double or int or long => Convert.ToString(raw, CultureInfo.InvariantCulture)!,
        bool flag => flag ? "true" : "false",
        _ => raw.GetType().Name,
    };

    private static int? TryReadInt(IReadOnlyDictionary<string, object?> spec, string key)
    {
        if (!spec.TryGetValue(key, out var raw) || raw is null) return null;
        try
        {
            // Jint's ToObject() surfaces JS numbers as double; tolerate int/long/string too.
            var value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return null;
            return (int)Math.Min(value, int.MaxValue);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static TimeSpan? TryReadWallTime(IReadOnlyDictionary<string, object?> spec)
    {
        if (!spec.TryGetValue("wallTime", out var raw) || raw is null) return null;
        if (raw is string text) return ParseDuration(text);
        try
        {
            var seconds = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return null;
            return TimeSpan.FromSeconds(seconds);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static TimeSpan? ParseDuration(string text)
    {
        // Mirrors CrewRunOptions.ParseDuration ("500ms" / "45s" / "10m" / "1h"), except a
        // bare number means SECONDS here for symmetry with the numeric wallTime form.
        text = text.Trim();
        int i = 0;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == ',')) i++;
        if (i == 0 || !double.TryParse(text[..i], CultureInfo.InvariantCulture, out var value) || value <= 0)
            return null;
#pragma warning disable CA1308 // normalized unit key driving the switch below
        var unit = text[i..].Trim().ToLowerInvariant();
#pragma warning restore CA1308
        return unit switch
        {
            "ms" => TimeSpan.FromMilliseconds(value),
            "s" or "" => TimeSpan.FromSeconds(value),
            "m" => TimeSpan.FromMinutes(value),
            "h" => TimeSpan.FromHours(value),
            _ => null,
        };
    }
}
