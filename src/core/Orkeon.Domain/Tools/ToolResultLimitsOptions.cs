using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.Tools;

/// <summary>
/// The cut of each tool's result, bound from <c>Orkeon:Tools:ResultLimits</c>, and the fan-out bound of a tool-call batch.
/// </summary>
public sealed class ToolResultLimitsOptions
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "Orkeon:Tools:ResultLimits";

    /// <summary>The cut in characters, by tool name (case-insensitive).</summary>
    public Dictionary<string, int> Limits { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The most tool calls of one batch that run at once.</summary>
    public int MaxParallelToolCalls { get; set; } = 4;

    /// <summary>
    /// Cuts <paramref name="result"/> to <paramref name="maxLength"/> characters; a cut result ends with a notice
    /// of how many characters were omitted.
    /// </summary>
    public static string Cut(string result, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Length <= maxLength) return result;
        return result[..maxLength] +
            $"\n\n[... truncated, {result.Length - maxLength} chars omitted. Use more specific parameters to narrow results.]";
    }

    /// <summary>The cut that applies to <paramref name="toolName"/>: its configured limit, else the <see cref="AgentDefaults"/> one.</summary>
    public int ResolveFor(string toolName) =>
        !string.IsNullOrEmpty(toolName) && Limits.TryGetValue(toolName, out var limit)
            ? limit
            : AgentDefaults.ResolveMaxToolResultLength(toolName);
}
