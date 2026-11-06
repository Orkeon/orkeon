namespace Orkeon.Tools.Web.Firefox;

/// <summary>The profile a browser starts with: <see cref="Disposable"/>, or a name of the host's Firefox.</summary>
internal sealed record FirefoxProfileChoice(string Name)
{
    /// <summary>The name of the profile a browser gets for itself, removed when it closes.</summary>
    public const string DisposableName = "disposable";

    /// <summary>A fresh profile of the browser's own.</summary>
    public static readonly FirefoxProfileChoice Disposable = new(DisposableName);

    /// <summary><c>true</c> for a profile created for the browser and removed with it.</summary>
    public bool IsDisposable => Is(Name, DisposableName);

    /// <summary>Whether two profile names name the same profile.</summary>
    public static bool Is(string name, string other) => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this choice names the same profile as <paramref name="other"/>.</summary>
    public bool Same(FirefoxProfileChoice other) => other is not null && Is(Name, other.Name);

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// Which profiles the host lets a browser start with: the operator's default (<c>Profile</c>), the ones an
/// agent may name (<c>AllowedProfiles</c>, beside <c>disposable</c> and the default), and the ones no
/// browser ever uses (<c>DeniedProfiles</c>). Fail-closed: an agent names only what the host lists.
/// </summary>
internal sealed class FirefoxProfilePolicy
{
    private readonly FirefoxProfileChoice _default;
    private readonly List<string> _allowed;
    private readonly List<string> _denied;

    public FirefoxProfilePolicy(FirefoxToolOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _default = string.IsNullOrWhiteSpace(options.Profile) ? FirefoxProfileChoice.Disposable : new(options.Profile.Trim());
        _allowed = [.. options.AllowedProfiles.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())];
        _denied = [.. options.DeniedProfiles.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())];
    }

    /// <summary>The profile a browser starts with when the agent names none.</summary>
    public FirefoxProfileChoice Default => _default;

    /// <summary>The profile the agent's request resolves to, or the default when it names none.</summary>
    /// <exception cref="FirefoxException">The host does not let a browser start with that profile.</exception>
    public FirefoxProfileChoice Resolve(string? requested)
    {
        var choice = string.IsNullOrWhiteSpace(requested) ? _default : new FirefoxProfileChoice(requested.Trim());
        if (_denied.Any(name => FirefoxProfileChoice.Is(name, choice.Name)))
            throw new FirefoxException($"'{choice}' is a profile this host forbids ({FirefoxToolOptions.SectionName}:{nameof(FirefoxToolOptions.DeniedProfiles)}).");

        if (!string.IsNullOrWhiteSpace(requested) && !choice.IsDisposable && !choice.Same(_default)
            && !_allowed.Any(name => FirefoxProfileChoice.Is(name, choice.Name)))
        {
            var names = string.Join(", ", new[] { FirefoxProfileChoice.DisposableName, _default.Name }.Concat(_allowed).Distinct(StringComparer.OrdinalIgnoreCase));
            throw new FirefoxException($"'{choice}' is not a profile this host lets an agent name ({FirefoxToolOptions.SectionName}:{nameof(FirefoxToolOptions.AllowedProfiles)}): {names}.");
        }

        return choice;
    }
}
