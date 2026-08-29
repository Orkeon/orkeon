using Orkeon.Constants.Configuration;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over the <c>Orkeon:Rag</c> section: profile plus the opt-in switches
/// the UIs surface as check boxes (hybrid retrieval, and the double opt-in web
/// fallback — policy under <c>Corrective</c> and transport under <c>WebFallback</c>,
/// both of which must be on for a web fallback to happen).
/// </summary>
public sealed class RagSection
{
    /// <summary>Configuration path of the section.</summary>
    public const string SectionPath = ConfigurationKeys.Rag;

    private readonly AppSettingsDocument _document;

    internal RagSection(AppSettingsDocument document) => _document = document;

    /// <summary>The closed list of profile names, in preset order (<c>fast</c> first, the default).</summary>
    public static IReadOnlyList<string> KnownProfiles => RagProfilePresets.KnownProfileNames;

    /// <summary>True when the section carries at least one key.</summary>
    public bool Exists => _document.SectionExists(SectionPath);

    /// <summary>Retrieval profile (<c>Orkeon:Rag:Profile</c>); one of <see cref="KnownProfiles"/>.</summary>
    public string? Profile
    {
        get => _document.GetString($"{SectionPath}:Profile");
        set => _document.SetString($"{SectionPath}:Profile", value);
    }

    /// <summary>True when <see cref="Profile"/> is absent or names a known profile.</summary>
    public bool HasValidProfile =>
        Profile is not { Length: > 0 } profile || RagProfilePresets.TryParse(profile, out _);

    /// <summary>Document store provider key (<c>Orkeon:Rag:Provider</c>).</summary>
    public string? Provider
    {
        get => _document.GetString($"{SectionPath}:Provider");
        set => _document.SetString($"{SectionPath}:Provider", value);
    }

    /// <summary>Document store connection string (<c>Orkeon:Rag:ConnectionString</c>).</summary>
    public string? ConnectionString
    {
        get => _document.GetString($"{SectionPath}:ConnectionString");
        set => _document.SetString($"{SectionPath}:ConnectionString", value);
    }

    /// <summary>Opt-in hybrid retrieval (<c>Orkeon:Rag:Retrieval:Hybrid:Enabled</c>).</summary>
    public bool? HybridRetrievalEnabled
    {
        get => _document.GetBoolean($"{SectionPath}:Retrieval:Hybrid:Enabled");
        set => _document.SetBoolean($"{SectionPath}:Retrieval:Hybrid:Enabled", value);
    }

    /// <summary>
    /// Corrective-RAG web fallback <i>policy</i> switch
    /// (<c>Orkeon:Rag:Corrective:WebFallback:Enabled</c>) — half of the double opt-in.
    /// </summary>
    public bool? CorrectiveWebFallbackEnabled
    {
        get => _document.GetBoolean($"{SectionPath}:Corrective:WebFallback:Enabled");
        set => _document.SetBoolean($"{SectionPath}:Corrective:WebFallback:Enabled", value);
    }

    /// <summary>
    /// Web fallback <i>transport</i> switch (<c>Orkeon:Rag:WebFallback:Enabled</c>) —
    /// the other half of the double opt-in.
    /// </summary>
    public bool? WebFallbackEnabled
    {
        get => _document.GetBoolean($"{SectionPath}:WebFallback:Enabled");
        set => _document.SetBoolean($"{SectionPath}:WebFallback:Enabled", value);
    }

    /// <summary>True only when both halves of the web-fallback opt-in are on.</summary>
    public bool WebFallbackFullyEnabled =>
        CorrectiveWebFallbackEnabled == true && WebFallbackEnabled == true;

    /// <summary>Maximum corrective iterations (<c>Orkeon:Rag:Corrective:MaxIterations</c>).</summary>
    public int? CorrectiveMaxIterations
    {
        get => _document.GetInt32($"{SectionPath}:Corrective:MaxIterations");
        set => _document.SetInt32($"{SectionPath}:Corrective:MaxIterations", value);
    }

    /// <summary>Removes the whole section.</summary>
    public void Remove() => _document.Remove(SectionPath);
}
