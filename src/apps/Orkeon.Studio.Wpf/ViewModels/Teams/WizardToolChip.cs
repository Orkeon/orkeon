using System.Globalization;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>One tool chip: what it says, and the glyph in front of it.</summary>
/// <param name="Label">In the user's words and language — never the engine's identifier.</param>
/// <param name="Icon">A Lucide kind, resolved by <c>LucideIcon</c>.</param>
public sealed record WizardToolChip(string Label, string Icon);

/// <summary>
/// Turns the engine's tool identifiers into the chips the wizard shows (30/08 mock, T-18).
/// <para>
/// Two things the raw id cannot say. First, <c>fs.read</c> means nothing to the person
/// reading it, and <c>lire /docs</c> does — so the filesystem tools carry their SCOPE, which
/// is the only part that answers «what will this agent actually touch». Second, the label is
/// catalogued, so it follows a language switch like every other word on screen.
/// </para>
/// <para>
/// An unknown identifier keeps its own name and a neutral glyph rather than disappearing: a
/// tool the engine grew and Studio has not heard of yet must still be visible.
/// </para>
/// </summary>
public static class WizardToolChips
{
    /// <summary>
    /// Builds a chip for one tool, with the mount it is scoped to when it has one.
    /// <para>
    /// The identifiers here are the ones the engine actually emits. They were <c>fs.read</c>,
    /// <c>fs.list</c> and <c>fs.write</c> — spellings that exist nowhere else in the
    /// repository — so every real blueprint fell through to the neutral branch and the agent
    /// cards read «file_read» with a braces glyph instead of «lit /workspace». The test suite
    /// only ever fed the invented ids, which is why it stayed green.
    /// </para>
    /// </summary>
    /// <param name="toolId">The engine's own identifier.</param>
    /// <param name="strings">The catalogue — the label is never the raw id.</param>
    /// <param name="readScope">The mount a reading tool addresses; the caller knows it.</param>
    /// <param name="writeScope">The mount a writing tool addresses.</param>
    public static WizardToolChip For(
        string toolId, IStudioStrings strings, string? readScope = null, string? writeScope = null)
    {
        ArgumentNullException.ThrowIfNull(strings);

        var (patternKey, icon, scope) = (toolId ?? "") switch
        {
            "file_read" or "pdf_reader" => (StudioStringKeys.ToolReadScoped, "folder-open", readScope),
            "directory_read" or "directory_search" => (StudioStringKeys.ToolListScoped, "eye", readScope),
            "file_write" => (StudioStringKeys.ToolWriteScoped, "pencil", writeScope),
            "web_scrape" or "http_api" => (StudioStringKeys.ToolWeb, "route", null),
            "rag_search" or "rag_ingest" or "rag_eval" => (StudioStringKeys.ToolRag, "scan-search", null),
            _ => ("", "braces", null),
        };

        if (patternKey.Length == 0)
            return new WizardToolChip(toolId ?? "", icon);

        // A scoped tool whose mount the caller could not name says what it does, without
        // inventing a folder: the old code answered «/docs» whatever the blueprint said.
        return scope is { Length: > 0 }
            ? new WizardToolChip(string.Format(CultureInfo.CurrentCulture, strings[patternKey], scope), icon)
            : new WizardToolChip(strings[patternKey].Replace(" {0}", "", StringComparison.Ordinal), icon);
    }
}
