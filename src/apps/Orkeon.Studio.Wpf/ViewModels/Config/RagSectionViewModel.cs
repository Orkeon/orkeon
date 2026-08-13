using System.Collections.Generic;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The <c>Orkeon:Rag</c> form (spec §4.1): store settings, the closed profile list, and the opt-in
/// switches. The web fallback is a genuine double opt-in — the corrective policy and the transport
/// are two separate keys — and the form says so rather than hiding one behind the other.
/// </summary>
public sealed class RagSectionViewModel : DocumentSectionViewModel
{
    /// <summary>Binds the form to the <c>Orkeon:Rag</c> section of the document.</summary>
    public RagSectionViewModel(Func<AppSettingsDocument> document, Action onChanged)
        : base(document, onChanged)
    {
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

    /// <summary>Opt-in BM25 + RRF hybrid retrieval.</summary>
    public bool? HybridRetrievalEnabled
    {
        get => Section.HybridRetrievalEnabled;
        set => SetValue(Section.HybridRetrievalEnabled, value, v => Section.HybridRetrievalEnabled = v);
    }

    /// <summary>First half of the web-fallback opt-in: the corrective policy.</summary>
    public bool? CorrectiveWebFallbackEnabled
    {
        get => Section.CorrectiveWebFallbackEnabled;
        set => SetValue(Section.CorrectiveWebFallbackEnabled, value, v => Section.CorrectiveWebFallbackEnabled = v);
    }

    /// <summary>Second half of the web-fallback opt-in: the transport.</summary>
    public bool? WebFallbackEnabled
    {
        get => Section.WebFallbackEnabled;
        set => SetValue(Section.WebFallbackEnabled, value, v => Section.WebFallbackEnabled = v);
    }

    /// <summary>Whether both halves of the web-fallback opt-in are on.</summary>
    public bool WebFallbackFullyEnabled => Section.WebFallbackFullyEnabled;

    /// <summary>The status line that makes the double opt-in explicit in the form.</summary>
    public string WebFallbackStatus => (CorrectiveWebFallbackEnabled == true, WebFallbackEnabled == true) switch
    {
        (true, true) => "Web fallback active: the corrective loop may fetch pages, each one validated "
            + "against prompt injection.",
        (true, false) => "Inactive: the corrective policy allows it, but the transport "
            + "(Orkeon:Rag:WebFallback) is still off.",
        (false, true) => "Inactive: the transport is on, but the corrective policy "
            + "(Orkeon:Rag:Corrective:WebFallback) is still off.",
        _ => "Off. Both switches must be turned on for the corrective loop to reach the web.",
    };

    /// <summary>Bound on the number of corrective iterations.</summary>
    public int? CorrectiveMaxIterations
    {
        get => Section.CorrectiveMaxIterations;
        set => SetValue(Section.CorrectiveMaxIterations, value, v => Section.CorrectiveMaxIterations = v);
    }

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
