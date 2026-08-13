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

    /// <summary>Document store provider key.</summary>
    public string Provider { get; set; } = "";

    /// <summary>Document store connection string.</summary>
    public string ConnectionString { get; set; } = "";

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

    /// <inheritdoc />
    public void LoadFrom(AppSettingsDocument document)
    {
        var section = document.Rag;
        Profile = section.Profile;
        Provider = FieldText.FromString(section.Provider);
        ConnectionString = FieldText.FromString(section.ConnectionString);
        HybridRetrievalEnabled = section.HybridRetrievalEnabled;
        CorrectiveWebFallbackEnabled = section.CorrectiveWebFallbackEnabled;
        WebFallbackEnabled = section.WebFallbackEnabled;
        CorrectiveMaxIterations = FieldText.FromInt32(section.CorrectiveMaxIterations);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ApplyTo(AppSettingsDocument document)
    {
        if (!FieldText.TryReadInt32(
                CorrectiveMaxIterations,
                "Orkeon:Rag:Corrective:MaxIterations",
                out var iterations,
                out var error))
        {
            return [error!];
        }

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
        section.ConnectionString = FieldText.ToStringOrNull(ConnectionString);
        section.HybridRetrievalEnabled = HybridRetrievalEnabled;
        section.CorrectiveWebFallbackEnabled = CorrectiveWebFallbackEnabled;
        section.WebFallbackEnabled = WebFallbackEnabled;
        section.CorrectiveMaxIterations = iterations;

        return [];
    }
}
