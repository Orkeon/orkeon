using Orkeon.Constants.Llm;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Profiles;

/// <summary>What becomes of a model setting's name as a host LLM profile (STUDIO-48).</summary>
public enum HostProfileStatus
{
    /// <summary>Offered to crews: the setting is the host profile <see cref="HostProfileCheck.Id"/>.</summary>
    Offered,

    /// <summary>The setting names no provider (the « no model » card): no host profile can be an echo.</summary>
    NoProvider,

    /// <summary>The name keeps no ASCII letter or digit: there is no id for a crew to write.</summary>
    NoId,

    /// <summary>The id is <c>default</c>, the reserved name of the <c>Llm</c> section itself.</summary>
    DefaultName,

    /// <summary>Another setting answers to the same id (<see cref="HostProfileCheck.TakenBy"/>).</summary>
    TakenBySetting,

    /// <summary>An entry written by hand in the settings file holds the id; Studio never takes one over.</summary>
    TakenByFile,
}

/// <summary>The standing of one name as a host profile.</summary>
/// <param name="Status">Whether the name is offered, and why not.</param>
/// <param name="Id">The id the name gives (<see cref="ModelProfile.HostProfileIdOf"/>); null when it gives none.</param>
/// <param name="TakenBy">The setting that already answers to <paramref name="Id"/>, for <see cref="HostProfileStatus.TakenBySetting"/>.</param>
public sealed record HostProfileCheck(HostProfileStatus Status, string? Id, string? TakenBy = null)
{
    /// <summary>True when the setting is offered to crews under <see cref="Id"/>.</summary>
    public bool IsOffered => Status == HostProfileStatus.Offered;

    /// <summary>
    /// Whether the editor refuses the name: the reserved name, or an id another owner holds. A
    /// setting with no provider or no id is legitimate — it serves a team or the assistant — and
    /// is simply offered to no crew.
    /// </summary>
    public bool BlocksSave => Status is HostProfileStatus.DefaultName or HostProfileStatus.TakenBySetting or HostProfileStatus.TakenByFile;
}

/// <summary>
/// Studio's model settings as the host's LLM profiles (STUDIO-48). Each setting that names a
/// provider is the entry <c>Llm:Profiles:&lt;id&gt;</c> of the settings file — its id the name's
/// slug —, written without the key: the entry names the variable holding it (<c>ApiKeyEnvVar</c>,
/// STUDIO-49), which a run outside Studio reads, and a launch passes the key itself as
/// <c>ORKEON_Llm__Profiles__&lt;id&gt;__ApiKey</c>; so a crew that writes <c>profile: claude</c> runs
/// on the « Claude » setting from Studio, and from a terminal <c>orkeon run</c> or a scheduled
/// team reading the same file. The elected setting is the <c>Llm</c> section itself, written
/// whole (<see cref="ElectDefault"/>). An entry no setting owns was written by hand: Studio shows
/// it, never rewrites it, never removes it, and refuses a name that would take it over.
/// <para>
/// Ownership is the rule of the fiche: an id is Studio's when a setting of
/// <c>studio-model-profiles.json</c> offers it. The first setting to answer to an id owns it — the
/// editor refuses a second one, so only a hand-edited store can hold two.
/// </para>
/// </summary>
public static class HostLlmProfiles
{
    /// <summary>Each setting's standing, by name, in set order.</summary>
    public static IReadOnlyDictionary<string, HostProfileCheck> Classify(ModelProfileSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        var standing = new Dictionary<string, HostProfileCheck>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in set.Profiles)
        {
            // Two settings under one name: the first one is the one every lookup finds.
            if (standing.ContainsKey(profile.Name))
                continue;

            var id = profile.HostProfileId;
            HostProfileCheck check;
            if (id is not null && LlmProfilesSection.IsDefault(id))
            {
                check = new HostProfileCheck(HostProfileStatus.DefaultName, id);
            }
            else if (id is not null && owners.TryGetValue(id, out var owner))
            {
                check = new HostProfileCheck(HostProfileStatus.TakenBySetting, id, owner);
            }
            else
            {
                if (id is not null)
                    owners[id] = profile.Name;

                check = !profile.DescribesProvider
                    ? new HostProfileCheck(HostProfileStatus.NoProvider, id)
                    : id is null
                        ? new HostProfileCheck(HostProfileStatus.NoId, null)
                        : new HostProfileCheck(HostProfileStatus.Offered, id);
            }

            standing[profile.Name] = check;
        }

        return standing;
    }

    /// <summary>The host profile id each offered setting answers to, by setting name.</summary>
    public static IReadOnlyDictionary<string, string> Offered(ModelProfileSet set) =>
        Classify(set)
            .Where(entry => entry.Value.IsOffered)
            .ToDictionary(entry => entry.Key, entry => entry.Value.Id!, StringComparer.Ordinal);

    /// <summary>
    /// The entries of <paramref name="document"/>'s <c>Llm:Profiles</c> no setting of
    /// <paramref name="set"/> owns — written by hand —, in document order.
    /// </summary>
    public static IReadOnlyList<LlmProfileEntry> HandWritten(AppSettingsDocument document, ModelProfileSet set)
    {
        ArgumentNullException.ThrowIfNull(document);

        var owned = new HashSet<string>(Offered(set).Values, StringComparer.OrdinalIgnoreCase);
        var profiles = document.Llm.Profiles;
        return
        [
            .. profiles.Ids
                .Where(id => !owned.Contains(id))
                .Select(id => profiles.Get(id))
                .OfType<LlmProfileEntry>(),
        ];
    }

    /// <summary>
    /// The standing a name typed in the profile editor would have.
    /// </summary>
    /// <param name="name">The name as typed.</param>
    /// <param name="previousName">The name the edited setting had; null for a new one.</param>
    /// <param name="describesProvider">Whether the draft names an endpoint or a model.</param>
    /// <param name="set">The settings as they are.</param>
    /// <param name="document">The settings file being edited, for the entries written by hand.</param>
    public static HostProfileCheck Check(
        string name,
        string? previousName,
        bool describesProvider,
        ModelProfileSet set,
        AppSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(document);

        var id = ModelProfile.HostProfileIdOf(name?.Trim());
        if (id is not null && LlmProfilesSection.IsDefault(id))
            return new HostProfileCheck(HostProfileStatus.DefaultName, id);

        if (id is not null
            && set.Profiles.FirstOrDefault(p =>
                    !string.Equals(p.Name, previousName, StringComparison.Ordinal)
                    && string.Equals(p.HostProfileId, id, StringComparison.OrdinalIgnoreCase)) is { } other)
        {
            return new HostProfileCheck(HostProfileStatus.TakenBySetting, id, other.Name);
        }

        if (!describesProvider)
            return new HostProfileCheck(HostProfileStatus.NoProvider, id);
        if (id is null)
            return new HostProfileCheck(HostProfileStatus.NoId, null);

        return HandWritten(document, set).Any(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
            ? new HostProfileCheck(HostProfileStatus.TakenByFile, id)
            : new HostProfileCheck(HostProfileStatus.Offered, id);
    }

    /// <summary>
    /// Brings <paramref name="document"/>'s <c>Llm:Profiles</c> in line with a change of the
    /// settings from <paramref name="before"/> to <paramref name="after"/>: a renamed setting's
    /// entry moves, every key it carries with it; the entry of a setting no longer offered —
    /// removed, switched to « no model » — goes; and every offered setting the change touched, or
    /// whose entry the file lacks or carries without the reference to its key's variable
    /// (STUDIO-49), is written, without its key. <c>Orkeon:Rag:LlmProfile</c> follows a rename and
    /// falls back to the default when its profile goes: the host refuses to start on a profile it
    /// does not define. Entries written by hand are never touched.
    /// </summary>
    /// <param name="document">The settings file being edited.</param>
    /// <param name="before">The settings before the change.</param>
    /// <param name="after">The settings after it.</param>
    /// <param name="renamedFrom">The name a renamed setting had; null when nothing was renamed.</param>
    /// <param name="renamedTo">The name it has now.</param>
    /// <returns>True when the document changed.</returns>
    public static bool Mirror(
        AppSettingsDocument document,
        ModelProfileSet before,
        ModelProfileSet after,
        string? renamedFrom = null,
        string? renamedTo = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var was = Offered(before);
        var now = Offered(after);
        var section = document.Llm.Profiles;
        var changed = false;

        if (renamedFrom is not null && renamedTo is not null
            && was.TryGetValue(renamedFrom, out var oldId)
            && now.TryGetValue(renamedTo, out var newId)
            && !string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
        {
            changed |= section.Rename(oldId, newId);
            changed |= MoveRagProfile(document, oldId, newId);
        }

        var offeredNow = new HashSet<string>(now.Values, StringComparer.OrdinalIgnoreCase);
        foreach (var id in was.Values.Where(id => !offeredNow.Contains(id)))
        {
            changed |= section.Remove(id);
            changed |= MoveRagProfile(document, id, null);
        }

        foreach (var (name, id) in now)
        {
            var profile = after.Find(name)!;
            var entry = profile.ToHostEntry(id);
            // Untouched — and healed (STUDIO-49): an entry written before it named its key's
            // variable is written again at the next gesture, never at startup.
            var untouched = before.Find(name) == profile
                && was.TryGetValue(name, out var previousId)
                && string.Equals(previousId, id, StringComparison.OrdinalIgnoreCase)
                && section.Get(id) is { } written
                && string.Equals(written.ApiKeyEnvVar, entry.ApiKeyEnvVar, StringComparison.Ordinal);
            if (!untouched)
                changed |= section.Set(entry);
        }

        return changed;
    }

    /// <summary>
    /// Writes the elected <paramref name="profile"/> into the <c>Llm</c> section, whole (STUDIO-49):
    /// every field it pins, a field it leaves unset removing its key, and the reference to its key's
    /// variable — so a run outside Studio follows the election with its timeout, its thinking
    /// switch and its key, as a host profile already did. The keys Studio does not model stay
    /// (<see cref="LlmSection.Set"/>), a clear-text <c>ApiKey</c> among them: it is not Studio's.
    /// </summary>
    /// <param name="document">The settings file being edited.</param>
    /// <param name="profile">The setting elected default.</param>
    /// <returns>True when the document changed.</returns>
    public static bool ElectDefault(AppSettingsDocument document, ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profile);
        return document.Llm.Set(profile.ToHostEntry(LlmProfileNames.Default));
    }

    /// <summary>
    /// Heals the <c>Llm</c> section the election wrote before it named the key's variable
    /// (STUDIO-49): when it lacks the reference the elected setting carries — or carries another —
    /// it receives the elected setting whole, at the next gesture on the settings, never at
    /// startup. Otherwise nothing is written: only the election and an edit of the elected
    /// setting write the section, so a field edited by hand there stays.
    /// </summary>
    /// <param name="document">The settings file being edited.</param>
    /// <param name="set">The settings, the elected one among them.</param>
    /// <returns>True when the document changed.</returns>
    public static bool HealDefault(AppSettingsDocument document, ModelProfileSet set)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(set);

        if (set.Default is not { } profile)
            return false;

        var expected = profile.ToHostEntry(LlmProfileNames.Default).ApiKeyEnvVar;
        return !string.Equals(document.Llm.ApiKeyEnvVar, expected, StringComparison.Ordinal)
            && ElectDefault(document, profile);
    }

    /// <summary>
    /// The environment a launch lays over its child so that every setting offered to crews is the
    /// host profile it is in the file — endpoint, model, what it pins and its key, as
    /// <c>ORKEON_Llm__Profiles__&lt;id&gt;__*</c>, the key resolved through <paramref name="keys"/>.
    /// A launch from Studio thus never depends on the settings file having been saved, nor on
    /// which settings file it reads; a setting whose key is not remembered rides without one.
    /// </summary>
    /// <param name="set">The settings.</param>
    /// <param name="keys">Reads a key variable by name — the key store.</param>
    public static IReadOnlyDictionary<string, string> LaunchEnvironment(ModelProfileSet set, Func<string, string?> keys)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(keys);

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, id) in Offered(set))
        {
            foreach (var (key, value) in set.Find(name)!.HostEnvironment(id, keys))
                environment[key] = value;
        }

        return environment;
    }

    /// <summary>Points <c>Orkeon:Rag:LlmProfile</c> from <paramref name="from"/> to <paramref name="to"/> (null: the default) when it names <paramref name="from"/>.</summary>
    private static bool MoveRagProfile(AppSettingsDocument document, string from, string? to)
    {
        if (!string.Equals(document.Rag.LlmProfile?.Trim(), from, StringComparison.OrdinalIgnoreCase))
            return false;

        document.Rag.LlmProfile = to;
        return true;
    }
}
