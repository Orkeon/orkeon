namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>The keys of the prompt, as <c>?</c> shows them on an empty prompt.</summary>
internal static class ShortcutsHelp
{
    /// <summary>The lines of the help, the keys aligned, in the secondary colour.</summary>
    public static IReadOnlyList<TranscriptLine> Lines(GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        (string Key, string Effect)[] keys =
        [
            ("/", "commands"),
            ("@", "files"),
            ("shift+enter", "a new line"),
            ("shift+tab", "cycle the permission mode"),
            (glyphs.Down, "the tasks list"),
            ("ctrl+o", "what the last step did, on an empty prompt"),
            ("ctrl+g", "logs"),
            ("ctrl+end", "back to the end of the transcript"),
            ("esc esc", "interrupt"),
        ];
        var width = keys.Max(k => TerminalText.Columns(k.Key));
        return keys
            .Select(k =>
            {
                var text = $"{TranscriptModel.Indent}{k.Key.PadRight(width)}  {k.Effect}";
                return new TranscriptLine(text) { Runs = [new TranscriptRun(0, text.Length, PaletteRole.Dim)] };
            })
            .ToList();
    }
}
