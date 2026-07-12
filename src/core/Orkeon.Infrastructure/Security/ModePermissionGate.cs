using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// Default <see cref="IPermissionGate"/> mapping permission modes to per-tool verdicts
/// (exp07 SPEC §3.3, cross-checked against the Claude Code analysis of exp02 round-36/37):
/// <c>bypassPermissions</c> allows everything; <c>plan</c> denies anything non-read;
/// <c>acceptEdits</c> auto-accepts reads AND file edits but asks for shell commands
/// (that is the point of the mode); <c>default</c> auto-accepts reads and asks for
/// everything else — read-only tools observing state carry no risk, and denying them
/// headless made every containerized/scripted session useless (aligned with the
/// original's default mode, which never prompts for reads). Unknown modes
/// fall back to <c>default</c>; unknown tools are classified as writes (fail-closed).
/// In non-interactive sessions every <c>ask</c> degrades to a motivated deny — the model
/// receives the refusal as the tool result and can adapt.
/// </summary>
public sealed class ModePermissionGate : IPermissionGate
{
    /// <summary>
    /// Tools that only observe state (workspace reads, session/memory bookkeeping,
    /// codebase analysis). Note: <c>index_codebase</c>/<c>incremental_reindex</c> mutate the
    /// local analysis index but are spec-classified as reads — they never touch the
    /// user's files.
    /// </summary>
    private static readonly HashSet<string> ReadTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "file_read", "directory_read", "directory_search", "count_pattern", "list_mounts",
        "session_store", "session_snip", "session_cost", "session_stats", "token_budget",
        "memory_store", "local_embed_text", "is_path_indexed", "statement_query",
        "complexity_report", "dependency_graph", "flow_trace", "impact_analysis",
        "package_summary", "sub_graph",
    };

    private static readonly string[] ReadPrefixes = ["codebase_", "symbol_", "index_"];

    /// <summary>
    /// Workspace-edit tools auto-accepted by <c>acceptEdits</c> (unlike arbitrary command
    /// execution, which keeps asking in that mode).
    /// </summary>
    private static readonly HashSet<string> EditTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "file_write",
    };

    private readonly bool _isInteractive;

    /// <summary>
    /// Creates the gate. <paramref name="isInteractive"/> declares whether an interactive
    /// approval channel exists; headless hosts (crew runner, SDK) pass <c>false</c> so
    /// <c>ask</c> verdicts degrade to motivated denies, aligned with the source behaviour.
    /// </summary>
    public ModePermissionGate(bool isInteractive = false) => _isInteractive = isInteractive;

    /// <inheritdoc />
    public Task<PermissionVerdict> CheckAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        string mode,
        ToolAccess declaredAccess = ToolAccess.Unspecified,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        // A self-declared access class (IBaseTool.Access) wins over the name tables; the
        // tables remain the fallback for tools that declare nothing (Unspecified), so
        // pre-flag tools keep their exact verdicts. Execute declarations are honoured
        // as writes even if a name table said otherwise.
        var isRead = declaredAccess == ToolAccess.Read ||
                     (declaredAccess == ToolAccess.Unspecified && IsReadTool(toolName));
        var isEdit = declaredAccess == ToolAccess.Edit ||
                     (declaredAccess == ToolAccess.Unspecified && EditTools.Contains(toolName));

        var verdict = Normalize(mode) switch
        {
            "bypassPermissions" => PermissionVerdict.Allow(),
            "plan" => isRead
                ? PermissionVerdict.Allow()
                : PermissionVerdict.Deny($"tool '{toolName}' is not permitted in plan mode (read-only)."),
            "acceptEdits" => isRead || isEdit
                ? PermissionVerdict.Allow()
                : PermissionVerdict.Ask($"tool '{toolName}' executes commands and requires approval (mode 'acceptEdits')."),
            _ => isRead
                ? PermissionVerdict.Allow()
                : PermissionVerdict.Ask($"tool '{toolName}' requires approval (mode 'default')."),
        };

        if (verdict.Action == PermissionAction.Ask && !_isInteractive)
        {
            verdict = PermissionVerdict.Deny(
                $"{verdict.Message} No interactive approval channel is available in this session, so the call is denied.");
        }

        return Task.FromResult(verdict);
    }

    private static bool IsReadTool(string toolName)
        => ReadTools.Contains(toolName) ||
           ReadPrefixes.Any(p => toolName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string mode) => mode switch
    {
        "bypassPermissions" or "plan" or "acceptEdits" or "default" => mode,
        _ => "default", // fail-closed: unknown modes get the most restrictive interactive policy
    };
}
