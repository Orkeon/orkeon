using System.Text.RegularExpressions;
using Orkeon.Domain.Constants.Validation;

namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>
/// Value object representing a tool access control policy for an agent or crew.
/// Supports unrestricted access, whitelist mode (explicit allowlist), and blacklist mode (explicit blocklist).
/// Supports wildcard patterns for tool names (e.g., "File*" matches FileRead, FileWrite, etc.).
/// When two policies are merged, the result uses least-privilege (intersection) semantics:
/// a tool is only allowed if BOTH policies allow it.
/// </summary>
public sealed class ToolAccessPolicy : IEquatable<ToolAccessPolicy>
{
    /// <summary>
    /// Gets the access mode for this policy.
    /// </summary>
    public ToolAccessMode Mode { get; }

    /// <summary>
    /// Gets the list of tool names/patterns in the policy.
    /// In Whitelist mode: tools that are allowed.
    /// In Blacklist mode: tools that are blocked.
    /// In Unrestricted mode: this list is ignored.
    /// </summary>
    public IReadOnlyList<string> ToolList { get; }

    /// <summary>
    /// Gets whether this policy is unrestricted (allows all tools).
    /// </summary>
    public bool IsUnrestricted => Mode == ToolAccessMode.Unrestricted && _intersectedPolicy is null;

    /// <summary>
    /// Optional secondary policy for intersection (least-privilege merge).
    /// When set, IsToolAllowed requires BOTH this policy and the intersected policy to allow the tool.
    /// </summary>
    private readonly ToolAccessPolicy? _intersectedPolicy;

    /// <summary>
    /// Private constructor to enforce factory methods.
    /// </summary>
    private ToolAccessPolicy(ToolAccessMode mode, IReadOnlyList<string> toolList, ToolAccessPolicy? intersectedPolicy = null)
    {
        Mode = mode;
        ToolList = toolList;
        _intersectedPolicy = intersectedPolicy;
    }

    /// <summary>
    /// Creates an unrestricted policy that allows all tools.
    /// </summary>
    public static ToolAccessPolicy CreateUnrestricted() =>
        new(ToolAccessMode.Unrestricted, Array.Empty<string>());

    /// <summary>
    /// Creates a whitelist policy that only allows the specified tools.
    /// </summary>
    /// <param name="allowedTools">Tools that are allowed. Can include wildcard patterns like "File*".</param>
    /// <exception cref="ArgumentNullException">Thrown when allowedTools is null.</exception>
    /// <exception cref="ArgumentException">Thrown when allowedTools is empty.</exception>
    public static ToolAccessPolicy CreateWhitelist(params string[] allowedTools)
    {
        ArgumentNullException.ThrowIfNull(allowedTools);
        return CreateWhitelist(allowedTools.AsEnumerable());
    }

    /// <summary>
    /// Creates a whitelist policy that only allows the specified tools.
    /// </summary>
    /// <param name="allowedTools">Tools that are allowed. Can include wildcard patterns like "File*".</param>
    /// <exception cref="ArgumentNullException">Thrown when allowedTools is null.</exception>
    /// <exception cref="ArgumentException">Thrown when allowedTools is empty or contains null/empty items.</exception>
    public static ToolAccessPolicy CreateWhitelist(IEnumerable<string> allowedTools)
    {
        ArgumentNullException.ThrowIfNull(allowedTools);

        var tools = NormalizeToolList(allowedTools);
        if (tools.Count == 0)
            throw new ArgumentException("Whitelist must contain at least one tool.", nameof(allowedTools));

        return new(ToolAccessMode.Whitelist, tools);
    }

    /// <summary>
    /// Creates a blacklist policy that blocks the specified tools.
    /// </summary>
    /// <param name="blockedTools">Tools that are blocked. Can include wildcard patterns like "File*".</param>
    /// <exception cref="ArgumentNullException">Thrown when blockedTools is null.</exception>
    /// <exception cref="ArgumentException">Thrown when blockedTools is empty.</exception>
    public static ToolAccessPolicy CreateBlacklist(params string[] blockedTools)
    {
        ArgumentNullException.ThrowIfNull(blockedTools);
        return CreateBlacklist(blockedTools.AsEnumerable());
    }

    /// <summary>
    /// Creates a blacklist policy that blocks the specified tools.
    /// </summary>
    /// <param name="blockedTools">Tools that are blocked. Can include wildcard patterns like "File*".</param>
    /// <exception cref="ArgumentNullException">Thrown when blockedTools is null.</exception>
    /// <exception cref="ArgumentException">Thrown when blockedTools is empty or contains null/empty items.</exception>
    public static ToolAccessPolicy CreateBlacklist(IEnumerable<string> blockedTools)
    {
        ArgumentNullException.ThrowIfNull(blockedTools);

        var tools = NormalizeToolList(blockedTools);
        if (tools.Count == 0)
            throw new ArgumentException("Blacklist must contain at least one tool.", nameof(blockedTools));

        return new(ToolAccessMode.Blacklist, tools);
    }

    /// <summary>
    /// Checks whether the agent is allowed to access a specific tool based on this policy.
    /// When this policy is an intersection of two policies, the tool must be allowed by both.
    /// </summary>
    /// <param name="toolName">The name of the tool to check access for.</param>
    /// <returns>True if the tool is allowed, false if blocked.</returns>
    /// <exception cref="ArgumentException">Thrown when toolName is null or whitespace.</exception>
    public bool IsToolAllowed(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var allowedByThis = Mode switch
        {
            ToolAccessMode.Unrestricted => true,
            ToolAccessMode.Whitelist => ToolMatches(toolName, ToolList),
            ToolAccessMode.Blacklist => !ToolMatches(toolName, ToolList),
            _ => throw new InvalidOperationException($"Unknown access mode: {Mode}")
        };

        // If this policy blocks it, no need to check the intersected policy
        if (!allowedByThis)
            return false;

        // If there's an intersected policy, the tool must also be allowed by it
        if (_intersectedPolicy is not null)
            return _intersectedPolicy.IsToolAllowed(toolName);

        return true;
    }

    /// <summary>
    /// Merges this policy with a crew-level override policy using least-privilege (intersection) semantics.
    /// A tool is only allowed if BOTH this policy and the crew policy allow it.
    /// The crew override can only restrict access, never expand it.
    /// </summary>
    /// <param name="crewOverride">The crew-level override policy, if any.</param>
    /// <returns>The effective intersected policy (most restrictive combination of both).</returns>
    public ToolAccessPolicy MergeWithCrewOverride(ToolAccessPolicy? crewOverride)
    {
        if (crewOverride == null)
            return this;

        // If either is unrestricted (and not already an intersection), the other is more restrictive
        if (IsUnrestricted)
            return crewOverride;
        if (crewOverride.IsUnrestricted)
            return this;

        // Both have restrictions — create an intersection policy that requires both to allow
        return new ToolAccessPolicy(Mode, ToolList, crewOverride);
    }

    /// <summary>
    /// Determines equality based on mode and tool list.
    /// </summary>
    public override bool Equals(object? obj) =>
        obj is ToolAccessPolicy other && Equals(other);

    /// <summary>
    /// Determines equality based on mode and tool list.
    /// </summary>
    public bool Equals(ToolAccessPolicy? other)
    {
        if (other is null)
            return false;

        if (Mode != other.Mode || ToolList.Count != other.ToolList.Count)
            return false;

        if (!ToolList.SequenceEqual(other.ToolList, StringComparer.OrdinalIgnoreCase))
            return false;

        // Compare intersected policies
        if (_intersectedPolicy is null && other._intersectedPolicy is null)
            return true;
        if (_intersectedPolicy is null || other._intersectedPolicy is null)
            return false;

        return _intersectedPolicy.Equals(other._intersectedPolicy);
    }

    /// <summary>
    /// Computes hash code based on mode and tool list.
    /// </summary>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Mode.GetHashCode();
            foreach (var tool in ToolList)
            {
                hash = hash * 31 + tool.GetHashCode(StringComparison.OrdinalIgnoreCase);
            }
            if (_intersectedPolicy is not null)
            {
                hash = hash * 31 + _intersectedPolicy.GetHashCode();
            }
            return hash;
        }
    }

    /// <summary>
    /// Returns a human-readable string representation of the policy.
    /// </summary>
    public override string ToString()
    {
        var baseStr = Mode switch
        {
            ToolAccessMode.Unrestricted => "ToolAccessPolicy(Unrestricted)",
            ToolAccessMode.Whitelist => $"ToolAccessPolicy(Whitelist: {string.Join(", ", ToolList)})",
            ToolAccessMode.Blacklist => $"ToolAccessPolicy(Blacklist: {string.Join(", ", ToolList)})",
            _ => "ToolAccessPolicy(Unknown)"
        };

        if (_intersectedPolicy is not null)
            return $"Intersected({baseStr} ∩ {_intersectedPolicy})";

        return baseStr;
    }

    /// <summary>
    /// Normalizes a list of tool names by trimming whitespace and removing empty entries.
    /// </summary>
    private static System.Collections.ObjectModel.ReadOnlyCollection<string> NormalizeToolList(IEnumerable<string> tools)
    {
        var normalized = tools
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.AsReadOnly();
    }

    /// <summary>
    /// Checks whether a tool name matches any pattern in the list.
    /// Supports wildcard patterns: * matches any sequence, ? matches single character.
    /// </summary>
    private static bool ToolMatches(string toolName, IReadOnlyList<string> patterns)
        => patterns.Any(pattern => WildcardMatch(toolName, pattern));

    /// <summary>
    /// Performs wildcard matching: * matches any sequence, ? matches single character.
    /// </summary>
    private static bool WildcardMatch(string toolName, string pattern)
    {
        // Case-insensitive matching
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal)
            + "$";

        return Regex.IsMatch(toolName, regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(ValidationDefaults.RegexTimeoutSeconds));
    }
}
