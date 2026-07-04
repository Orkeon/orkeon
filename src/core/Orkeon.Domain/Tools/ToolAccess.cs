namespace Orkeon.Domain.Tools;

/// <summary>
/// Declarative access classification of a tool, consumed by permission gates
/// (e.g. <c>ModePermissionGate</c>) to decide per-mode verdicts without maintaining
/// name lists. A tool that does not declare anything reports
/// <see cref="Unspecified"/> and the gate falls back to its own classification,
/// treating unknown tools as writes (fail-closed).
/// </summary>
public enum ToolAccess
{
    /// <summary>No declaration — the permission gate applies its own (fail-closed) classification.</summary>
    Unspecified = 0,

    /// <summary>Only observes state (workspace reads, session/memory bookkeeping, codebase analysis).</summary>
    Read,

    /// <summary>Edits workspace files (auto-accepted by <c>acceptEdits</c>, unlike command execution).</summary>
    Edit,

    /// <summary>Executes commands or performs external side effects (most restrictive class).</summary>
    Execute,
}
