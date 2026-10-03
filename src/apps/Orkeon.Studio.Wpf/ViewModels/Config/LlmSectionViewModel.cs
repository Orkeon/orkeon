using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// What the model-settings screen writes into the <c>Llm</c> section and around it — not a form:
/// no view edits the section's fields (GAP-36). The screen writes it through its model settings:
/// the election writes the elected one into the section, whole (STUDIO-49), every setting is
/// mirrored into <c>Llm:Profiles:&lt;id&gt;</c> (STUDIO-48), and a default elected before it named
/// its key's variable is healed (STUDIO-49). It also chooses the RAG's profile (<c>Orkeon:Rag:LlmProfile</c>),
/// lists the entries of <c>Llm:Profiles</c> written by hand, and checks a name typed in the profile
/// editor against them. Every write travels through the screen's own edit-then-save cycle.
/// </summary>
public sealed class LlmSectionViewModel : DocumentSectionViewModel
{
    /// <summary>Binds the screen to the <c>Llm</c> section of the document.</summary>
    /// <param name="document">Supplies the document currently being edited.</param>
    /// <param name="onChanged">Called whenever a write lands in the document.</param>
    public LlmSectionViewModel(Func<AppSettingsDocument> document, Action onChanged)
        : base(document, onChanged)
    {
    }

    /// <inheritdoc />
    public override bool Exists => Document.Llm.Exists;

    // ── the host LLM profiles (STUDIO-48): Llm:Profiles, and the one the RAG calls ──

    /// <summary>
    /// The host LLM profile the RAG subsystem calls (<c>Orkeon:Rag:LlmProfile</c>, GAP-19) — one
    /// of <c>Llm:Profiles</c>, null for the default. It lives under <c>Orkeon:Rag</c> but is chosen
    /// among this section's profiles, on the model-settings screen; writing the default removes
    /// the key.
    /// </summary>
    public string? RagLlmProfile
    {
        get => Document.Rag.LlmProfile;
        set => SetValue(Document.Rag.LlmProfile, value is { } name && !LlmProfilesSection.IsDefault(name) ? name.Trim() : null, v => Document.Rag.LlmProfile = v);
    }

    /// <summary>
    /// Brings <c>Llm:Profiles</c> in line with a change of Studio's model settings (STUDIO-48,
    /// <see cref="HostLlmProfiles.Mirror"/>), and heals a default the election wrote before it
    /// named its key's variable (STUDIO-49, <see cref="HostLlmProfiles.HealDefault"/>): one edit of
    /// the document when anything changed, so the write travels through the screen's own
    /// edit-then-save cycle like the election.
    /// </summary>
    public void MirrorModelProfiles(ModelProfileSet before, ModelProfileSet after, string? renamedFrom = null, string? renamedTo = null)
    {
        ArgumentNullException.ThrowIfNull(after);

        var profilesChanged = HostLlmProfiles.Mirror(Document, before, after, renamedFrom, renamedTo);
        var defaultHealed = HostLlmProfiles.HealDefault(Document, after);
        if (!profilesChanged && !defaultHealed)
            return;

        // A rename or a removal may have moved the RAG's profile with it.
        OnPropertyChanged(nameof(RagLlmProfile));
        NotifyDocumentChanged();
    }

    /// <summary>
    /// Writes the elected setting into the section, whole (STUDIO-49, decision 5): every field it
    /// pins — its timeout and thinking switch among them — and the variable holding its key, so a
    /// run outside Studio follows the election. The keys Studio does not model stay. Through the
    /// screen's own edit-then-save cycle: Studio never saves the settings file behind the user's back.
    /// </summary>
    public void ElectDefault(ModelProfile profile)
    {
        if (HostLlmProfiles.ElectDefault(Document, profile))
            NotifyDocumentChanged();
    }

    /// <summary>The entries of <c>Llm:Profiles</c> no setting of <paramref name="set"/> owns: written by hand, shown read-only.</summary>
    public IReadOnlyList<LlmProfileEntry> HandWrittenProfiles(ModelProfileSet set) => HostLlmProfiles.HandWritten(Document, set);

    /// <summary>The standing a name typed in the profile editor would have, against this document's entries.</summary>
    public HostProfileCheck CheckHostProfile(string name, string? previousName, bool describesProvider, ModelProfileSet set) =>
        HostLlmProfiles.Check(name, previousName, describesProvider, set, Document);
}
