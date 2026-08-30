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
    /// <summary>Builds a chip for one tool, with the folder it is scoped to when it has one.</summary>
    public static WizardToolChip For(string toolId, IStudioStrings strings, string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(strings);

        var (patternKey, icon, scoped) = (toolId ?? "") switch
        {
            "fs.read" => (StudioStringKeys.ToolReadScoped, "folder-open", true),
            "fs.list" => (StudioStringKeys.ToolListScoped, "eye", true),
            "fs.write" => (StudioStringKeys.ToolWriteScoped, "pencil", true),
            "llm.complete" => (StudioStringKeys.ToolLlm, "sparkles", false),
            "web.fetch" => (StudioStringKeys.ToolWeb, "route", false),
            "rag.search" => (StudioStringKeys.ToolRag, "scan-search", false),
            _ => ("", "braces", false),
        };

        if (patternKey.Length == 0)
            return new WizardToolChip(toolId ?? "", icon);

        return scoped
            ? new WizardToolChip(
                string.Format(CultureInfo.CurrentCulture, strings[patternKey], scope ?? DefaultScope(toolId!)),
                icon)
            : new WizardToolChip(strings[patternKey], icon);
    }

    /// <summary>
    /// Where a filesystem tool points when the blueprint says nothing: reading and listing
    /// look at the documents, writing goes to the output — the two mounts the wizard derives.
    /// </summary>
    private static string DefaultScope(string toolId) =>
        string.Equals(toolId, "fs.write", StringComparison.Ordinal) ? "/output" : "/docs";
}
