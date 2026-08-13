using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// One editable section, as text. A form is the whole of what a screen knows: it moves
/// values between an <see cref="AppSettingsDocument"/> and the strings a terminal field
/// holds, and nothing else. Every rule it applies belongs to <c>Orkeon.Studio.Core</c>.
/// </summary>
internal interface ISettingsForm
{
    /// <summary>Section title, as shown in the navigation list.</summary>
    string Title { get; }

    /// <summary>Fills the fields from the document.</summary>
    void LoadFrom(AppSettingsDocument document);

    /// <summary>
    /// Writes the fields back into the document.
    /// </summary>
    /// <returns>
    /// Field-level errors (a number field holding letters). An empty list means every
    /// value was written; a non-empty one means nothing was written for those fields.
    /// </returns>
    IReadOnlyList<string> ApplyTo(AppSettingsDocument document);
}
