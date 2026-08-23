using System.Text.Json.Serialization;

namespace Orkeon.Studio.Core.Profiles;

/// <summary>
/// Every model profile on this machine, plus the two elections: the default (applied to new
/// teams and mirrored into the settings file) and Studio's own assistant profile (null until
/// the user picks one — the creation wizard is gated on it). Immutable; every mutation
/// returns the set to persist.
/// </summary>
public sealed record ModelProfileSet
{
    /// <summary>The empty first-run state: no profile, nothing elected.</summary>
    public static ModelProfileSet Empty { get; } = new();

    /// <summary>The profiles, in display order.</summary>
    public IReadOnlyList<ModelProfile> Profiles { get; init; } = [];

    /// <summary>Name of the default profile, or null when none is elected.</summary>
    public string? DefaultProfile { get; init; }

    /// <summary>Name of the profile Studio's assistant uses, or null while unconfigured.</summary>
    public string? StudioProfile { get; init; }

    /// <summary>The elected default, resolved; null when absent or dangling.</summary>
    [JsonIgnore]
    public ModelProfile? Default => Find(DefaultProfile);

    /// <summary>The assistant's profile, resolved; null while unconfigured or dangling.</summary>
    [JsonIgnore]
    public ModelProfile? Studio => Find(StudioProfile);

    /// <summary>Finds a profile by name (ordinal), or null.</summary>
    public ModelProfile? Find(string? name) =>
        name is { Length: > 0 }
            ? Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
            : null;

    /// <summary>
    /// Adds or replaces a profile. Replacing renames follow: when <paramref name="previousName"/>
    /// differs from the profile's new name, the elections that pointed at the old name move too.
    /// While no default is elected, the upserted profile becomes it — a machine with exactly
    /// one setting has exactly one answer to "which one".
    /// </summary>
    public ModelProfileSet Upsert(ModelProfile profile, string? previousName = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var target = previousName ?? profile.Name;
        var replaced = Find(target) is not null;
        var profiles = replaced
            ? Profiles.Select(p => string.Equals(p.Name, target, StringComparison.Ordinal) ? profile : p).ToList()
            : Profiles.Append(profile).ToList();

        return this with
        {
            Profiles = profiles,
            DefaultProfile = string.Equals(DefaultProfile, target, StringComparison.Ordinal)
                ? profile.Name
                : DefaultProfile ?? profile.Name,
            StudioProfile = string.Equals(StudioProfile, target, StringComparison.Ordinal) ? profile.Name : StudioProfile,
        };
    }

    /// <summary>
    /// Removes a profile. A dangling election is cleared rather than left pointing at
    /// nothing; the default falls back to the first remaining profile.
    /// </summary>
    public ModelProfileSet Remove(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var profiles = Profiles.Where(p => !string.Equals(p.Name, name, StringComparison.Ordinal)).ToList();
        var defaultProfile = string.Equals(DefaultProfile, name, StringComparison.Ordinal)
            ? profiles.FirstOrDefault()?.Name
            : DefaultProfile;
        var studioProfile = string.Equals(StudioProfile, name, StringComparison.Ordinal) ? null : StudioProfile;

        return this with { Profiles = profiles, DefaultProfile = defaultProfile, StudioProfile = studioProfile };
    }

    /// <summary>Elects a default; a name that matches no profile leaves the set unchanged.</summary>
    public ModelProfileSet WithDefault(string name) =>
        Find(name) is null ? this : this with { DefaultProfile = name };

    /// <summary>Elects the assistant's profile; unknown names leave the set unchanged.</summary>
    public ModelProfileSet WithStudio(string name) =>
        Find(name) is null ? this : this with { StudioProfile = name };

    /// <summary>A unique copy name for duplication: "Name (copy)", "Name (copy 2)", …</summary>
    public string CopyNameFor(string name, string copySuffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(copySuffix);

        var candidate = $"{name} ({copySuffix})";
        for (var i = 2; Find(candidate) is not null; i++)
            candidate = $"{name} ({copySuffix} {i})";
        return candidate;
    }
}
