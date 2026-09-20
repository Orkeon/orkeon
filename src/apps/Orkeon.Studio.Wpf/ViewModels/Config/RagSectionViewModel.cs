using System.Collections.Generic;
using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The <c>Orkeon:Rag</c> form (spec §4.1): store settings, the closed profile list, and the opt-in
/// switches. The web fallback is a genuine double opt-in — the corrective policy and the transport
/// are two separate keys — and the form says so rather than hiding one behind the other. The
/// switches are plain booleans resolving an absent key to the engine's off (STUDIO-22): a nullable
/// bound to a check box swallows the first click, and a switch turned back off removes its key.
/// </summary>
public sealed class RagSectionViewModel : DocumentSectionViewModel
{
    private readonly IStudioStrings _strings;

    /// <summary>Binds the form to the <c>Orkeon:Rag</c> section of the document.</summary>
    public RagSectionViewModel(Func<AppSettingsDocument> document, Action onChanged, IStudioStrings? strings = null)
        : base(document, onChanged)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
    }

    private RagSection Section => Document.Rag;

    /// <inheritdoc />
    public override bool Exists => Section.Exists;

    /// <summary>The closed list of profile names, straight from <c>RagProfilePresets</c>.</summary>
    public static IReadOnlyList<string> KnownProfiles => RagSection.KnownProfiles;

    /// <summary>The selected retrieval profile.</summary>
    public string? Profile
    {
        get => Section.Profile;
        set => SetValue(Section.Profile, Blank(value), v => Section.Profile = v);
    }

    /// <summary>The watermark of the profile list: the engine's default profile.</summary>
    public static string ProfileDefault => RagSection.DefaultProfile;

    /// <summary>Whether the profile currently in the file is one the runtime knows.</summary>
    public bool HasValidProfile => Section.HasValidProfile;

    /// <summary>The vector-store provider.</summary>
    public string? Provider
    {
        get => Section.Provider;
        set => SetValue(Section.Provider, Blank(value), v => Section.Provider = v);
    }

    /// <summary>The vector-store connection string.</summary>
    public string? ConnectionString
    {
        get => Section.ConnectionString;
        set => SetValue(Section.ConnectionString, Blank(value), v => Section.ConnectionString = v);
    }

    /// <summary>Opt-in BM25 + RRF hybrid retrieval (engine default: off).</summary>
    public bool HybridRetrievalEnabled
    {
        get => Section.HybridRetrievalEnabled ?? RagSection.DefaultHybridRetrievalEnabled;
        set => SetValue(HybridRetrievalEnabled, value,
            v => Section.HybridRetrievalEnabled = v == RagSection.DefaultHybridRetrievalEnabled ? null : v);
    }

    /// <summary>First half of the web-fallback opt-in: the corrective policy (engine default: off).</summary>
    public bool CorrectiveWebFallbackEnabled
    {
        get => Section.CorrectiveWebFallbackEnabled ?? RagSection.DefaultCorrectiveWebFallbackEnabled;
        set => SetValue(CorrectiveWebFallbackEnabled, value,
            v => Section.CorrectiveWebFallbackEnabled = v == RagSection.DefaultCorrectiveWebFallbackEnabled ? null : v);
    }

    /// <summary>Second half of the web-fallback opt-in: the transport (engine default: off).</summary>
    public bool WebFallbackEnabled
    {
        get => Section.WebFallbackEnabled ?? RagSection.DefaultWebFallbackEnabled;
        set => SetValue(WebFallbackEnabled, value,
            v => Section.WebFallbackEnabled = v == RagSection.DefaultWebFallbackEnabled ? null : v);
    }

    /// <summary>Whether both halves of the web-fallback opt-in are on.</summary>
    public bool WebFallbackFullyEnabled => Section.WebFallbackFullyEnabled;

    /// <summary>The status line that makes the double opt-in explicit in the form.</summary>
    public string WebFallbackStatus => _strings[(CorrectiveWebFallbackEnabled, WebFallbackEnabled) switch
    {
        (true, true) => StudioStringKeys.RagWebFallbackActive,
        (true, false) => StudioStringKeys.RagWebFallbackTransportOff,
        (false, true) => StudioStringKeys.RagWebFallbackPolicyOff,
        _ => StudioStringKeys.RagWebFallbackOff,
    }];

    /// <summary>Bound on the number of corrective iterations.</summary>
    public int? CorrectiveMaxIterations
    {
        get => Section.CorrectiveMaxIterations;
        set => SetValue(Section.CorrectiveMaxIterations, value, v => Section.CorrectiveMaxIterations = v);
    }

    /// <summary>The watermark of <see cref="CorrectiveMaxIterations"/>.</summary>
    public static string CorrectiveMaxIterationsDefault =>
        RagSection.DefaultCorrectiveMaxIterations.ToString(CultureInfo.InvariantCulture);

    /// <summary>Drops the whole section from the document.</summary>
    public void RemoveSection()
    {
        Section.Remove();
        Refresh();
    }

    /// <inheritdoc />
    protected override void OnSectionChanged() => OnPropertiesChanged(
        nameof(Exists),
        nameof(HasValidProfile),
        nameof(WebFallbackFullyEnabled),
        nameof(WebFallbackStatus));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
