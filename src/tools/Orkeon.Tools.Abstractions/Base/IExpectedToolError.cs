namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// Marks an exception a tool throws on purpose, whose message alone tells the user or the agent what
/// to fix: a choice to make, an account missing, a grant refused. <see cref="ToolBase"/> logs its
/// type and message and leaves its stack trace out; any other exception is a fault and keeps it.
/// </summary>
#pragma warning disable CA1040 // A marker: the type is the information
public interface IExpectedToolError;
#pragma warning restore CA1040
