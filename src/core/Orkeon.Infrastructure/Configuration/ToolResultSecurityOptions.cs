using Orkeon.Domain.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for tool result sanitization (<c>Security:ToolResults</c>). The
/// length of a result is bounded by one rule only, <c>AgentDefaults.ResolveMaxToolResultLength</c>,
/// applied by the tool-invocation pipeline before this sanitizer runs.
/// </summary>
public class ToolResultSecurityOptions
{
    /// <summary>
    /// The policy applied to a successful tool result. Default <see cref="SanitizationPolicy.Warn"/>:
    /// the result reaches the model tagged as data and any injection pattern is logged and
    /// audited. <see cref="SanitizationPolicy.Block"/> withholds a result carrying a High or
    /// Critical pattern (the model is told so); <see cref="SanitizationPolicy.None"/> passes
    /// results untouched and untagged.
    /// </summary>
    public SanitizationPolicy Policy { get; set; } = SanitizationPolicy.Warn;

    /// <summary>
    /// Tools whose results bypass sanitization. Defaults to the <c>email_*</c> tools, which
    /// screen what they read with <c>PromptInjectionDocumentValidator</c> and mark it untrusted
    /// themselves (ADR-012) — tagging them again would stack two envelopes. Entries from
    /// configuration are added to these.
    /// </summary>
    public HashSet<string> TrustedTools { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "email_accounts", "email_create_folder", "email_delete", "email_draft", "email_folders",
        "email_mark", "email_move", "email_parser", "email_read", "email_rename_folder",
        "email_save_attachment", "email_search", "email_send",
    };
}
