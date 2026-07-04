using System.Globalization;
using Orkeon.Domain.Autonomous;

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
    /// Recognized keys: <c>toolCalls</c>, <c>tokens</c>, <c>delegationDepth</c>,
    /// <c>spawnedAgents</c> (integers) and <c>wallTime</c> (number = seconds, string =
    /// duration with unit <c>ms|s|m|h</c>). Invalid values are ignored — a script must
    /// never crash at build time because of a malformed budget field.
    /// </summary>
    internal static AgentExecutionBudget? FromSpec(IReadOnlyDictionary<string, object?>? spec)
    {
        if (spec is null || spec.Count == 0) return null;

        int? toolCalls = TryReadInt(spec, "toolCalls");
        int? tokens = TryReadInt(spec, "tokens");
        int? delegationDepth = TryReadInt(spec, "delegationDepth");
        int? spawnedAgents = TryReadInt(spec, "spawnedAgents");
        TimeSpan? wallTime = TryReadWallTime(spec);

        if (toolCalls is null && tokens is null && delegationDepth is null &&
            spawnedAgents is null && wallTime is null)
            return null;

        var defaults = AgentExecutionBudget.Default;
        return new AgentExecutionBudget
        {
            MaxToolCalls = toolCalls ?? defaults.MaxToolCalls,
            MaxTokensConsumed = tokens ?? defaults.MaxTokensConsumed,
            MaxDelegationDepth = delegationDepth ?? defaults.MaxDelegationDepth,
            MaxSpawnedAgents = spawnedAgents ?? defaults.MaxSpawnedAgents,
            MaxWallTime = wallTime ?? defaults.MaxWallTime,
        };
    }

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
