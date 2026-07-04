using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Auth;

/// <summary>
/// Defines how claim values should be matched.
/// </summary>
public enum ClaimMatchMode
{
    /// <summary>Any of the allowed values must be present.</summary>
    Any,
    /// <summary>All of the allowed values must be present.</summary>
    All
}

/// <summary>
/// A single claim requirement for authorization.
/// </summary>
public record ClaimsRequirement(
    string ClaimType,
    IReadOnlyList<string> AllowedValues,
    ClaimMatchMode MatchMode = ClaimMatchMode.Any);

/// <summary>
/// Authorization policy that evaluates claims on the principal against a set of requirements.
/// </summary>
public class ClaimsAuthorizationPolicy : IAuthorizationPolicy
{
    private readonly IReadOnlyList<ClaimsRequirement> _requirements;

    /// <inheritdoc />
    public string PolicyName => "ClaimsBased";

    /// <summary>
    /// Initializes a new instance of <see cref="ClaimsAuthorizationPolicy"/>.
    /// </summary>
    public ClaimsAuthorizationPolicy(IEnumerable<ClaimsRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        _requirements = requirements.ToList();
    }

    /// <inheritdoc />
    public Task<AuthorizationResult> EvaluateAsync(AuthorizationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var requirement in _requirements)
        {
            var claims = context.Principal.FindAll(requirement.ClaimType)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool satisfied = requirement.MatchMode switch
            {
                ClaimMatchMode.Any => requirement.AllowedValues.Any(v =>
                    claims.Contains(v)),
                ClaimMatchMode.All => requirement.AllowedValues.All(v =>
                    claims.Contains(v)),
                _ => false
            };

            if (!satisfied)
            {
                return Task.FromResult(new AuthorizationResult(
                    false,
                    $"Required claim '{requirement.ClaimType}' not satisfied"));
            }
        }

        return Task.FromResult(new AuthorizationResult(true));
    }
}
