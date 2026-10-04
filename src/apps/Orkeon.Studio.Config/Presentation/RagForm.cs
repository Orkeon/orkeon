using System.Globalization;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// The <c>Orkeon:Rag</c> section: store, profile (a closed list), and the opt-in switches.
/// Every switch is tri-state — unset, on, off — because removing a key and writing
/// <c>false</c> are different things to the runtime binder.
/// </summary>
internal sealed class RagForm : ISettingsForm
{
    /// <inheritdoc />
    public string Title => "RAG";

    private const string HybridPath = "Orkeon:Rag:Retrieval:Hybrid:Enabled";
    private const string CorrectiveWebFallbackPath = "Orkeon:Rag:Corrective:WebFallback:Enabled";
    private const string WebFallbackPath = "Orkeon:Rag:WebFallback:Enabled";
    private const string MaxIterationsPath = "Orkeon:Rag:Corrective:MaxIterations";

    /// <summary>The closed list of profile names, in preset order.</summary>
    public static IReadOnlyList<string> Profiles => RagSection.KnownProfiles;

    /// <summary>Label of the "no profile key" choice, offered first in the list.</summary>
    public const string UnsetProfileLabel = "(unset — runtime default: fast)";

    /// <summary>The list a profile chooser shows: the unset choice, then the known profiles.</summary>
    public static IReadOnlyList<string> ProfileChoices { get; } =
        new List<string> { UnsetProfileLabel }.Concat(RagSection.KnownProfiles).ToList().AsReadOnly();

    /// <summary>Explains why a single web-fallback switch does nothing on its own.</summary>
    public const string WebFallbackNotice =
        "Web fallback is a double opt-in: the corrective policy switch AND the transport switch " +
        "must both be on, and the transport must be registered with AddOrkeonRagWebFallback().";

    /// <summary>Selected profile, or null for "no key".</summary>
    public string? Profile { get; set; }

    /// <summary>
    /// Document store provider type. Its connection comes from that provider's own section
    /// (<c>Orkeon:Redis</c>, <c>Orkeon:Sqlite</c>, …), not from <c>Orkeon:Rag</c>.
    /// </summary>
    public string Provider { get; set; } = "";

    /// <summary>Hybrid retrieval opt-in.</summary>
    public bool? HybridRetrievalEnabled { get; set; }

    /// <summary>Corrective-RAG web fallback policy switch.</summary>
    public bool? CorrectiveWebFallbackEnabled { get; set; }

    /// <summary>Web fallback transport switch.</summary>
    public bool? WebFallbackEnabled { get; set; }

    /// <summary>Maximum corrective iterations.</summary>
    public string CorrectiveMaxIterations { get; set; } = "";

    /// <summary>True only when both halves of the web-fallback opt-in are on.</summary>
    public bool WebFallbackFullyEnabled =>
        CorrectiveWebFallbackEnabled == true && WebFallbackEnabled == true;

    /// <summary>Index of <see cref="Profile"/> in <see cref="ProfileChoices"/> (0 = unset).</summary>
    public int ProfileChoiceIndex
    {
        get
        {
            if (Profile is not { Length: > 0 } profile)
                return 0;

            for (var i = 0; i < Profiles.Count; i++)
            {
                if (string.Equals(Profiles[i], profile, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }

            return 0;
        }
    }

    /// <summary>Selects a profile by its index in <see cref="ProfileChoices"/>.</summary>
    public void SelectProfile(int choiceIndex) =>
        Profile = choiceIndex >= 1 && choiceIndex <= Profiles.Count ? Profiles[choiceIndex - 1] : null;

    /// <summary>
    /// The text <c>Orkeon:Rag:Retrieval:Hybrid:Enabled</c> holds when it reads as no boolean, else null
    /// (STUDIO-55): kept and refused until the switch changes state — see
    /// <see cref="LlmLoggingForm.FullEmbeddingLogAsWritten"/>.
    /// </summary>
    public string? HybridRetrievalEnabledAsWritten { get; set; }

    /// <summary>The same for <c>Orkeon:Rag:Corrective:WebFallback:Enabled</c>.</summary>
    public string? CorrectiveWebFallbackEnabledAsWritten { get; set; }

    /// <summary>The same for <c>Orkeon:Rag:WebFallback:Enabled</c>.</summary>
    public string? WebFallbackEnabledAsWritten { get; set; }

    /// <inheritdoc />
    public void LoadFrom(AppSettingsDocument document)
    {
        var section = document.Rag;
        Profile = section.Profile;
        Provider = FieldText.FromString(section.Provider);
        HybridRetrievalEnabled = section.HybridRetrievalEnabled;
        CorrectiveWebFallbackEnabled = section.CorrectiveWebFallbackEnabled;
        WebFallbackEnabled = section.WebFallbackEnabled;
        HybridRetrievalEnabledAsWritten = FieldText.UnreadableSwitch(document, HybridPath);
        CorrectiveWebFallbackEnabledAsWritten = FieldText.UnreadableSwitch(document, CorrectiveWebFallbackPath);
        WebFallbackEnabledAsWritten = FieldText.UnreadableSwitch(document, WebFallbackPath);
        CorrectiveMaxIterations = document.GetWritten(MaxIterationsPath);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ApplyTo(AppSettingsDocument document)
    {
        var errors = new List<string>();
        if (!FieldText.TryReadInt32(CorrectiveMaxIterations, MaxIterationsPath, out var iterations, out var error))
            errors.Add(error!);
        FieldText.RefuseUnreadableSwitch(HybridRetrievalEnabledAsWritten, HybridPath, errors);
        FieldText.RefuseUnreadableSwitch(CorrectiveWebFallbackEnabledAsWritten, CorrectiveWebFallbackPath, errors);
        FieldText.RefuseUnreadableSwitch(WebFallbackEnabledAsWritten, WebFallbackPath, errors);
        if (errors.Count > 0)
            return errors;

        if (Profile is { Length: > 0 } profile
            && !Profiles.Any(known => string.Equals(known, profile, StringComparison.OrdinalIgnoreCase)))
        {
            return
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Orkeon:Rag:Profile: '{profile}' is not one of {string.Join(", ", Profiles)}."),
            ];
        }

        var section = document.Rag;
        section.Profile = FieldText.ToStringOrNull(Profile);
        section.Provider = FieldText.ToStringOrNull(Provider);
        section.HybridRetrievalEnabled = HybridRetrievalEnabled;
        section.CorrectiveWebFallbackEnabled = CorrectiveWebFallbackEnabled;
        section.WebFallbackEnabled = WebFallbackEnabled;
        section.CorrectiveMaxIterations = iterations;

        return [];
    }
}
