using System.Text.RegularExpressions;

namespace Orkeon.Cli.Scripting.Loading;

/// <summary>
/// Validates <see cref="CommandDescriptor"/> instances against the rules of spec §6.1.
/// </summary>
/// <remarks>
/// Stateless; reusable across loads. Returns structured <see cref="ValidationResult"/>
/// so callers (loader, tests) can branch on the failure kind instead of parsing messages.
/// </remarks>
public static partial class CommandDescriptorValidator
{
    /// <summary>Maximum allowed description length (single line, no newline).</summary>
    public const int MaxDescriptionLength = 200;

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NameRegexFactory();

    private static readonly Regex NameRegex = NameRegexFactory();

    /// <summary>
    /// Hard-reserved names a scripted command may never take: <c>help</c> and its aliases are
    /// the user's escape hatch for command discovery — a broken script that shadowed them
    /// would leave the REPL unexplorable.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "help", "?", "h" };

    /// <summary>
    /// Default-command names a scripted command MAY deliberately shadow (the runner resolves
    /// the scripted registry before the defaults — <c>InteractiveRunnerBase</c>: Specific ??
    /// Defaults). Lets a command surface redefine e.g. <c>/clear</c> as "clear the
    /// conversation" instead of the built-in screen-only clear. The loader logs each
    /// shadowing so it stays visible and intentional.
    /// </summary>
    public static readonly IReadOnlySet<string> ShadowableDefaultNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "exit", "quit", "q", "clear", "cls" };

    /// <summary>Validates a single descriptor in isolation. Cross-descriptor checks live in <see cref="ValidateGlobalUniqueness"/>.</summary>
    public static ValidationResult Validate(CommandDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (string.IsNullOrWhiteSpace(descriptor.Name))
            return ValidationResult.Fail(ValidationFailure.InvalidName, "Command name is empty.");

        if (!NameRegex.IsMatch(descriptor.Name))
            return ValidationResult.Fail(
                ValidationFailure.InvalidName,
                $"Command name '{descriptor.Name}' does not match ^[a-z][a-z0-9-]*$.");

        if (ReservedNames.Contains(descriptor.Name))
            return ValidationResult.Fail(
                ValidationFailure.NameIsBuiltin,
                $"Command name '{descriptor.Name}' collides with the reserved built-in 'help' surface (help/?/h).");

        var aliasVerdict = ValidateAliases(descriptor);
        if (!aliasVerdict.IsValid)
            return aliasVerdict;

        return ValidateDescription(descriptor);
    }

    private static ValidationResult ValidateAliases(CommandDescriptor descriptor)
    {
        foreach (var alias in descriptor.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return ValidationResult.Fail(
                    ValidationFailure.InvalidAlias,
                    $"Command '{descriptor.Name}' declares an empty alias.");

            if (!NameRegex.IsMatch(alias))
                return ValidationResult.Fail(
                    ValidationFailure.InvalidAlias,
                    $"Alias '{alias}' of command '{descriptor.Name}' does not match ^[a-z][a-z0-9-]*$.");

            if (string.Equals(alias, descriptor.Name, StringComparison.OrdinalIgnoreCase))
                return ValidationResult.Fail(
                    ValidationFailure.AliasEqualsName,
                    $"Command '{descriptor.Name}' has an alias identical to its name.");

            if (ReservedNames.Contains(alias))
                return ValidationResult.Fail(
                    ValidationFailure.NameIsBuiltin,
                    $"Alias '{alias}' of command '{descriptor.Name}' collides with the reserved built-in 'help' surface (help/?/h).");
        }

        // Aliases internal duplicates
        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in descriptor.Aliases)
        {
            if (!seenAliases.Add(alias))
                return ValidationResult.Fail(
                    ValidationFailure.InvalidAlias,
                    $"Command '{descriptor.Name}' has duplicate alias '{alias}'.");
        }

        return ValidationResult.Success;
    }

    private static ValidationResult ValidateDescription(CommandDescriptor descriptor)
    {
        if (string.IsNullOrEmpty(descriptor.Description))
            return ValidationResult.Fail(
                ValidationFailure.InvalidDescription,
                $"Command '{descriptor.Name}' has an empty description.");

        if (descriptor.Description.Length > MaxDescriptionLength)
            return ValidationResult.Fail(
                ValidationFailure.InvalidDescription,
                $"Command '{descriptor.Name}' description exceeds {MaxDescriptionLength} chars.");

        if (descriptor.Description.Contains('\n', StringComparison.Ordinal) || descriptor.Description.Contains('\r', StringComparison.Ordinal))
            return ValidationResult.Fail(
                ValidationFailure.InvalidDescription,
                $"Command '{descriptor.Name}' description must be a single line.");

        return ValidationResult.Success;
    }

    /// <summary>
    /// Names among the descriptor's name + aliases that shadow a default command
    /// (<see cref="ShadowableDefaultNames"/>). Empty when nothing is shadowed. The loader
    /// logs these so a deliberate override never passes silently.
    /// </summary>
    public static IReadOnlyList<string> GetShadowedDefaults(CommandDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var shadowed = new List<string>();
        if (ShadowableDefaultNames.Contains(descriptor.Name))
            shadowed.Add(descriptor.Name);
        shadowed.AddRange(descriptor.Aliases.Where(ShadowableDefaultNames.Contains));
        return shadowed;
    }

    /// <summary>
    /// Checks global uniqueness across an ordered batch of already-individually-valid
    /// descriptors. First occurrence wins; later collisions are reported with both source paths.
    /// </summary>
    public static IReadOnlyList<(CommandDescriptor Conflicting, string Reason)> ValidateGlobalUniqueness(
        IReadOnlyList<CommandDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var taken = new Dictionary<string, CommandDescriptor>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<(CommandDescriptor, string)>();

        foreach (var d in descriptors)
        {
            if (taken.TryGetValue(d.Name, out var owner))
            {
                conflicts.Add((d, $"Name '{d.Name}' already taken by {owner.SourceVirtualPath}."));
                continue;
            }

            var aliasConflict = false;
            foreach (var alias in d.Aliases)
            {
                if (taken.TryGetValue(alias, out var aliasOwner))
                {
                    conflicts.Add((d, $"Alias '{alias}' already taken by {aliasOwner.SourceVirtualPath}."));
                    aliasConflict = true;
                    break;
                }
            }
            if (aliasConflict) continue;

            taken[d.Name] = d;
            foreach (var alias in d.Aliases) taken[alias] = d;
        }

        return conflicts;
    }
}

/// <summary>Reason for a validation failure (machine-readable, stable across phases).</summary>
public enum ValidationFailure
{
    InvalidName,
    NameIsBuiltin,
    InvalidAlias,
    AliasEqualsName,
    InvalidDescription,
}

/// <summary>Result of validating a <see cref="CommandDescriptor"/>.</summary>
public readonly record struct ValidationResult(bool IsValid, ValidationFailure? Failure, string? Message)
{
    public static readonly ValidationResult Success = new(true, null, null);
    public static ValidationResult Fail(ValidationFailure failure, string message) => new(false, failure, message);
}
