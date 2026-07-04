using System.Collections.Immutable;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Security;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Security context value object for access control and auditing.
/// </summary>
public sealed record SecurityContext : ValueObjectRecord
{
    private static readonly string[] s_adminRoles = ["admin", "user"];
    private static readonly string[] s_adminPermissions = ["create", "read", "update", "delete", "execute", "manage"];
    private static readonly string[] s_viewerRoles = ["viewer"];
    private static readonly string[] s_viewerPermissions = ["read"];

    /// <summary>Gets the user ID associated with this security context.</summary>
    public string UserId { get; init; }
    /// <summary>Gets the set of roles assigned to the user.</summary>
    public ImmutableHashSet<string> Roles { get; init; }
    /// <summary>Gets the set of permissions granted to the user.</summary>
    public ImmutableHashSet<string> Permissions { get; init; }
    /// <summary>Gets the security level.</summary>
    public SecurityLevel Level { get; init; }
    /// <summary>Gets when this security context was created.</summary>
    public DateTime CreatedAt { get; init; }
    /// <summary>Gets when this security context expires.</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Initializes a new <see cref="SecurityContext"/>.</summary>
    /// <param name="userId">The user identifier (non-empty).</param>
    /// <param name="roles">The user's roles.</param>
    /// <param name="permissions">The user's permissions.</param>
    /// <param name="level">The security level (default Standard).</param>
    /// <param name="validity">Optional validity duration (default 8 hours).</param>
    private SecurityContext(
        string userId,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        SecurityLevel? level = null,
        TimeSpan? validity = null)
    {
        UserId = string.IsNullOrWhiteSpace(userId) ? throw new ArgumentException("User ID cannot be empty", nameof(userId)) : userId;
        Roles = roles.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        Permissions = permissions.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        Level = level ?? SecurityLevel.Standard;
        CreatedAt = DateTime.UtcNow;
        ExpiresAt = CreatedAt.Add(validity ?? SecurityDefaults.DefaultSecurityContextValidity);
    }

    /// <summary>Creates a new <see cref="SecurityContext"/>.</summary>
    /// <param name="userId">The user identifier (non-empty).</param>
    /// <param name="roles">The user's roles.</param>
    /// <param name="permissions">The user's permissions.</param>
    /// <param name="level">The security level (default Standard).</param>
    /// <param name="validity">Optional validity duration (default 8 hours).</param>
    /// <returns>A new <see cref="SecurityContext"/>.</returns>
    public static SecurityContext Create(
        string userId,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        SecurityLevel? level = null,
        TimeSpan? validity = null) =>
        new(userId, roles, permissions, level, validity);

    /// <summary>Returns whether the user has the specified role.</summary>
    /// <param name="role">The role name.</param>
    /// <returns><see langword="true"/> if the user has the role; otherwise <see langword="false"/>.</returns>
    public bool HasRole(string role) => Roles.Contains(role);
    /// <summary>Returns whether the user has the specified permission.</summary>
    /// <param name="permission">The permission name.</param>
    /// <returns><see langword="true"/> if the user has the permission; otherwise <see langword="false"/>.</returns>
    public bool HasPermission(string permission) => Permissions.Contains(permission);
    /// <summary>Gets whether this security context has expired.</summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    /// <summary>Gets whether this security context is still valid.</summary>
    public bool IsValid => !IsExpired;

    /// <summary>Returns a new instance with the specified role added.</summary>
    /// <param name="role">The role to add.</param>
    /// <returns>A new <see cref="SecurityContext"/> with the role added.</returns>
    public SecurityContext AddRole(string role) => this with { Roles = Roles.Add(role) };
    /// <summary>Returns a new instance with the specified permission added.</summary>
    /// <param name="permission">The permission to add.</param>
    /// <returns>A new <see cref="SecurityContext"/> with the permission added.</returns>
    public SecurityContext AddPermission(string permission) => this with { Permissions = Permissions.Add(permission) };

    /// <summary>Creates an admin security context for the specified user.</summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>A high-security admin <see cref="SecurityContext"/>.</returns>
    public static SecurityContext Admin(string userId) => new(
        userId,
        s_adminRoles,
        s_adminPermissions,
        SecurityLevel.High);

    /// <summary>Creates a read-only security context for the specified user.</summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>A low-security read-only <see cref="SecurityContext"/>.</returns>
    public static SecurityContext ReadOnly(string userId) => new(
        userId,
        s_viewerRoles,
        s_viewerPermissions,
        SecurityLevel.Low);

    /// <inheritdoc />
    public override string ToString() =>
        $"User: {UserId}, Roles: {Roles.Count}, Permissions: {Permissions.Count}, Level: {Level}";
}

/// <summary>Security level for access control.</summary>
public sealed record SecurityLevel
{
    /// <summary>Gets the string value of this security level.</summary>
    public string Value { get; }
    private SecurityLevel(string value) => Value = value;

    /// <summary>Minimal access restrictions; suitable for read-only viewers.</summary>
    public static readonly SecurityLevel Low = new("Low");
    /// <summary>Default security level for regular users.</summary>
    public static readonly SecurityLevel Standard = new("Standard");
    /// <summary>Elevated restrictions for privileged users or sensitive operations.</summary>
    public static readonly SecurityLevel High = new("High");
    /// <summary>Strictest security level; full access control enforced.</summary>
    public static readonly SecurityLevel Maximum = new("Maximum");

    private static readonly Dictionary<string, SecurityLevel> s_all = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Low)] = Low,
        [nameof(Standard)] = Standard,
        [nameof(High)] = High,
        [nameof(Maximum)] = Maximum,
    };

    /// <summary>Gets all valid security levels.</summary>
    public static IReadOnlyCollection<SecurityLevel> All => s_all.Values;

    /// <summary>Creates a <see cref="SecurityLevel"/> from its string representation.</summary>
    public static SecurityLevel From(string value) =>
        s_all.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentException($"Unknown SecurityLevel: '{value}'", nameof(value));

    /// <summary>Attempts to create a <see cref="SecurityLevel"/> from its string representation.</summary>
    public static bool TryFrom(string? value, out SecurityLevel? result)
    {
        if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; }
        result = null; return false;
    }

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
    /// <summary>Implicitly converts to string.</summary>
    public static implicit operator string(SecurityLevel s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
