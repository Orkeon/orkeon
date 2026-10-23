using System.Collections.Immutable;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// Default <see cref="IPermissionGate"/> mapping permission modes to per-tool verdicts:
/// <c>bypassPermissions</c> allows everything; <c>plan</c> denies anything non-read;
/// <c>acceptEdits</c> auto-accepts reads AND file edits but asks for shell commands
/// (that is the point of the mode); <c>default</c> auto-accepts reads and asks for
/// everything else — read-only tools observing state carry no risk, and denying them
/// headless made every containerized/scripted session useless (aligned with the way
/// coding-assistant CLIs conventionally treat their own default mode, which never
/// prompts for reads); <c>dontAsk</c> allows reads and denies everything else, never
/// asking, whether or not an approval channel exists. Unknown modes
/// fall back to <c>default</c>; unknown tools are classified as writes (fail-closed).
/// With an approval channel (<see cref="IToolApprovalChannel"/>), every <c>ask</c> is put to the
/// operator and comes back allowed or denied. In non-interactive sessions every <c>ask</c>
/// degrades to a motivated deny — the model receives the refusal as the tool result and can adapt.
/// With session rules (<see cref="Rules"/>), a deny rule refuses a call in every mode, and an allow
/// rule lets it run without asking in every mode but <c>plan</c>; a call no rule covers is decided
/// by the mode.
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
        "package_summary", "sub_graph", "access_cost", "node_outline", "node_read",
        "structural_search", "glob", "grep", "affected_tests", "batch_anchors",
        "documented_in", "documents_of",
    };

    private static readonly string[] ReadPrefixes = ["codebase_", "symbol_", "index_"];

    /// <summary>
    /// Workspace-edit tools auto-accepted by <c>acceptEdits</c> (unlike arbitrary command
    /// execution, which keeps asking in that mode).
    /// </summary>
    private static readonly HashSet<string> EditTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "file_write", "spawn_agent",
        "node_replace", "node_insert", "edit_batch",
        "structural_rewrite",
        "edit_file",
    };

    private readonly bool _isInteractive;
    private readonly IToolApprovalChannel? _approvalChannel;

    /// <summary>
    /// The session's allow and deny rules, read at each call before the mode; null when the host keeps
    /// none, and the mode alone decides.
    /// </summary>
    public IToolPermissionRules? Rules { get; init; }

    /// <summary>
    /// Creates the gate. <paramref name="isInteractive"/> declares whether the caller resolves
    /// <c>ask</c> verdicts itself, which then leave the gate as they are; headless hosts (crew
    /// runner, SDK) pass <c>false</c> so <c>ask</c> verdicts degrade to motivated denies,
    /// aligned with the source behaviour.
    /// </summary>
    public ModePermissionGate(bool isInteractive = false) => _isInteractive = isInteractive;

    /// <summary>
    /// Creates a gate that puts every <c>ask</c> to <paramref name="approvalChannel"/> and waits
    /// for the operator: the verdict it returns is then <c>allow</c> or <c>deny</c>, never <c>ask</c>.
    /// </summary>
    /// <param name="approvalChannel">The channel an operator answers through.</param>
    public ModePermissionGate(IToolApprovalChannel approvalChannel)
    {
        ArgumentNullException.ThrowIfNull(approvalChannel);
        _approvalChannel = approvalChannel;
        _isInteractive = true;
    }

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

        var effectiveMode = Normalize(mode);

        // Session rules come first: a deny rule refuses whatever the mode, an allow rule spares the
        // question in every mode but plan, which promises a turn that changes nothing.
        var rules = Rules?.Current ?? [];
        if (rules.FirstOrDefault(r => r.Effect == ToolPermissionEffect.Deny && r.Matches(toolName, arguments)) is { } deny)
            return Task.FromResult(PermissionVerdict.Deny($"tool '{toolName}' is denied by the session rule '{deny}'."));
        if (effectiveMode != "plan"
            && rules.Any(r => r.Effect == ToolPermissionEffect.Allow && r.Matches(toolName, arguments)))
            return Task.FromResult(PermissionVerdict.Allow());

        var verdict = effectiveMode switch
        {
            "bypassPermissions" => PermissionVerdict.Allow(),
            "plan" => isRead
                ? PermissionVerdict.Allow()
                : PermissionVerdict.Deny($"tool '{toolName}' is not permitted in plan mode (read-only)."),
            "dontAsk" => isRead
                ? PermissionVerdict.Allow()
                : PermissionVerdict.Deny($"tool '{toolName}' is not permitted in mode 'dontAsk' (reads only, never asks)."),
            "acceptEdits" => isRead || isEdit
                ? PermissionVerdict.Allow()
                : PermissionVerdict.Ask($"tool '{toolName}' executes commands and requires approval (mode 'acceptEdits')."),
            _ => isRead
                ? PermissionVerdict.Allow()
                : PermissionVerdict.Ask($"tool '{toolName}' requires approval (mode 'default')."),
        };

        if (verdict.Action == PermissionAction.Ask && _approvalChannel is not null)
            return AskOperatorAsync(_approvalChannel, toolName, arguments, effectiveMode, verdict.Message!, cancellationToken);

        if (verdict.Action == PermissionAction.Ask && !_isInteractive)
        {
            verdict = PermissionVerdict.Deny(
                $"{verdict.Message} No interactive approval channel is available in this session, so the call is denied.");
        }

        return Task.FromResult(verdict);
    }

    private static async Task<PermissionVerdict> AskOperatorAsync(
        IToolApprovalChannel channel, string toolName, IReadOnlyDictionary<string, object?> arguments,
        string mode, string reason, CancellationToken cancellationToken)
    {
        var decision = await channel.RequestAsync(
            new ToolApprovalRequest
            {
                ToolName = toolName,
                Arguments = arguments.ToImmutableDictionary(),
                Mode = mode,
                Reason = reason,
            },
            cancellationToken).ConfigureAwait(false);

        return decision is ToolApprovalDecision.AllowOnce or ToolApprovalDecision.AllowForSession
            ? PermissionVerdict.Allow()
            : PermissionVerdict.Deny($"{reason} The operator denied the call.");
    }

    private static bool IsReadTool(string toolName)
        => ReadTools.Contains(toolName) ||
           ReadPrefixes.Any(p => toolName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string mode) => mode switch
    {
        "bypassPermissions" or "plan" or "acceptEdits" or "default" or "dontAsk" => mode,
        _ => "default", // fail-closed: unknown modes get the most restrictive interactive policy
    };
}
