namespace Orkeon.Domain.Tools;

/// <summary>
/// Implemented by the built-in tools that hand work or a question to another agent of the crew
/// (<c>delegate_work_to_coworker</c>, <c>ask_question_to_coworker</c>). The permission gate lets the call of
/// such a tool through, since the coworker's own calls are gated under the stricter of the two modes: that
/// exemption is bound to this type, never to a name, which an MCP server, a plugin or a script can declare.
/// </summary>
public interface ICoworkerDelegationTool : IBaseTool
{
    /// <summary>Gets the role of the coworker one call targets, read from its <paramref name="arguments"/>; null when none is named.</summary>
    /// <param name="arguments">The arguments the model supplied.</param>
    /// <returns>The coworker's role, or null.</returns>
    string? CoworkerRoleOf(IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        foreach (var (key, value) in arguments)
        {
            if (string.Equals(key.Replace("_", "", StringComparison.Ordinal), "coworkerrole", StringComparison.OrdinalIgnoreCase))
                return value?.ToString();
        }

        return null;
    }
}

/// <summary>
/// A tool that wraps another and forwards its calls (an observer, a meter). What a wrapper is, is told
/// from the tool it wraps: a delegation tool stays one once observed.
/// </summary>
public interface IToolWrapper
{
    /// <summary>Gets the tool this one forwards its calls to.</summary>
    IBaseTool Inner { get; }
}

/// <summary>The names of the coworker delegation tools, reserved to them, and how a call to one is recognised.</summary>
public static class CoworkerDelegation
{
    /// <summary>The tool a coworker is handed work through.</summary>
    public const string DelegateWorkToolName = "delegate_work_to_coworker";

    /// <summary>The tool a coworker is asked a question through.</summary>
    public const string AskQuestionToolName = "ask_question_to_coworker";

    // A chain of wrappers deeper than this is not unwrapped further: the tool is then not a delegation one.
    private const int MaxWrapperDepth = 16;

    /// <summary>Gets the names reserved to the delegation tools.</summary>
    public static IReadOnlyList<string> ToolNames { get; } = [DelegateWorkToolName, AskQuestionToolName];

    /// <summary>Whether <paramref name="toolName"/> is reserved to a delegation tool (case ignored).</summary>
    /// <param name="toolName">The tool name.</param>
    /// <returns>True for a reserved name.</returns>
    public static bool IsReservedName(string toolName) =>
        string.Equals(toolName, DelegateWorkToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, AskQuestionToolName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The delegation tool behind <paramref name="tool"/> (through its wrappers), or null when it is not one:
    /// a tool that merely carries a reserved name is not.
    /// </summary>
    /// <param name="tool">The tool called.</param>
    /// <returns>The delegation tool, or null.</returns>
    public static ICoworkerDelegationTool? Find(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var current = tool;
        for (var depth = 0; depth < MaxWrapperDepth; depth++)
        {
            if (current is ICoworkerDelegationTool delegation)
                return IsReservedName(delegation.Name) && IsReservedName(tool.Name) ? delegation : null;
            if (current is not IToolWrapper { Inner: { } inner })
                return null;
            current = inner;
        }

        return null;
    }

    /// <summary>Whether <paramref name="tool"/> is a coworker delegation tool (see <see cref="Find"/>).</summary>
    /// <param name="tool">The tool called.</param>
    /// <returns>True for a delegation tool.</returns>
    public static bool IsDelegationTool(IBaseTool tool) => Find(tool) is not null;
}
