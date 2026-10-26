namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>
/// The call's input of a <see cref="PendingToolApproval"/> as the operator reads it: the arguments cut at
/// <see cref="MaxLength"/> characters, an ellipsis marking the cut. The status line of the TUI and the
/// <c>approvals</c> service of a scripted command show the same text through it.
/// </summary>
public static class PendingToolApprovalInput
{
    /// <summary>The longest input shown before it is cut: the status line has one row.</summary>
    public const int MaxLength = 60;

    /// <summary>The input of <paramref name="approval"/>, cut at <see cref="MaxLength"/> characters.</summary>
    public static string Summarize(PendingToolApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return Summarize(approval.Arguments);
    }

    /// <summary>The <paramref name="arguments"/> of a call, cut at <see cref="MaxLength"/> characters.</summary>
    public static string Summarize(string? arguments)
    {
        arguments ??= string.Empty;
        return arguments.Length <= MaxLength
            ? arguments
            : string.Concat(arguments.AsSpan(0, MaxLength), "…");
    }
}
