using System;

namespace Orkeon.Domain.Constants.Llm;

/// <summary>
/// Canonical conversation role names (lowercase wire values) and a single normalized
/// comparison helper (SML-009 / R12.5).
/// <para>
/// Roles travel as plain strings on <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmMessage.Role"/>
/// and in provider payloads. Before this type, some paths compared them case-sensitively
/// (<c>msg.Role == "assistant"</c>) and others case-insensitively
/// (<c>string.Equals(..., OrdinalIgnoreCase)</c>), so a mixed-case role like <c>"Assistant"</c>
/// was serialized one way and policy-matched another. All comparisons now route through
/// <see cref="Is"/> (OrdinalIgnoreCase), so the same role is handled identically everywhere.
/// </para>
/// </summary>
public static class LlmRoles
{
    /// <summary>The system role.</summary>
    public const string System = "system";

    /// <summary>The user role.</summary>
    public const string User = "user";

    /// <summary>The assistant role.</summary>
    public const string Assistant = "assistant";

    /// <summary>The tool (tool-result) role.</summary>
    public const string Tool = "tool";

    /// <summary>
    /// Returns <c>true</c> when <paramref name="role"/> equals <paramref name="canonical"/>
    /// ignoring case — the single, authoritative way to test a role across the codebase.
    /// </summary>
    public static bool Is(string? role, string canonical)
        => string.Equals(role, canonical, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns <c>true</c> when <paramref name="role"/> is the system role.</summary>
    public static bool IsSystem(string? role) => Is(role, System);

    /// <summary>Returns <c>true</c> when <paramref name="role"/> is the user role.</summary>
    public static bool IsUser(string? role) => Is(role, User);

    /// <summary>Returns <c>true</c> when <paramref name="role"/> is the assistant role.</summary>
    public static bool IsAssistant(string? role) => Is(role, Assistant);

    /// <summary>Returns <c>true</c> when <paramref name="role"/> is the tool role.</summary>
    public static bool IsTool(string? role) => Is(role, Tool);
}
