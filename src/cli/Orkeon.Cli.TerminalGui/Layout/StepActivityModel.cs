using System.Text.RegularExpressions;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Telemetry;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>What an ended step leaves in the transcript: one line, the calls it stands for, and how many of each.</summary>
internal sealed record FoldedStep(
    TranscriptLine Summary, IReadOnlyList<TranscriptLine> Details, IReadOnlyDictionary<string, int> Counts);

/// <summary>
/// The tool calls of the current step: the rows shown while they run, a running count over the latest ones, and
/// what remains of them once the step ends. Pure: it is told what happens and is asked what to show.
/// </summary>
internal sealed partial class StepActivityModel : IPanelRows
{
    private const string TreeIndent = "    ";
    private readonly GlyphSet _glyphs;
    private readonly List<Call> _calls = [];

    private sealed class Call(long id, string tool, string subject)
    {
        public long Id { get; } = id;
        public string Tool { get; } = tool;
        public string Subject { get; } = subject;
        public bool Open { get; set; } = true;
        public bool Failed { get; set; }
    }

    public StepActivityModel(GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        _glyphs = glyphs;
    }

    /// <summary>True when the step holds no call.</summary>
    public bool IsEmpty => _calls.Count == 0;

    /// <summary>True while a call of the step runs.</summary>
    public bool HasOpenCall => _calls.Exists(c => c.Open);

    /// <summary>A call begins.</summary>
    public void Start(ToolCallStarted call)
    {
        ArgumentNullException.ThrowIfNull(call);
        _calls.Add(new Call(call.Id, call.Tool, call.Subject));
    }

    /// <summary>A call ended; one the step does not hold is ignored (a screen cleared since it began).</summary>
    public void Finish(ToolCallFinished call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (_calls.Find(c => c.Id == call.Id) is not { } found) return;
        found.Open = false;
        found.Failed = !call.Succeeded;
    }

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<RowSegment>> Rows(int width, int maxRows)
    {
        if (IsEmpty || maxRows <= 0) return [];
        var rows = new List<IReadOnlyList<RowSegment>> { TitleRow(width) };
        var shown = _calls.Skip(Math.Max(0, _calls.Count - (maxRows - 1))).ToList();
        for (var i = 0; i < shown.Count; i++)
            rows.Add(CallRow(shown[i], i == 0 ? $"  {_glyphs.Elbow} " : TreeIndent, width));
        return rows;
    }

    /// <summary>Empties the step and returns what stays of it.</summary>
    public FoldedStep Fold()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var call in _calls)
            counts[call.Tool] = counts.GetValueOrDefault(call.Tool) + 1;

        var details = new List<TranscriptLine>(_calls.Count);
        for (var i = 0; i < _calls.Count; i++)
            details.Add(DetailLine(_calls[i], i == 0 ? $"  {_glyphs.Elbow} " : TreeIndent));

        _calls.Clear();
        return new FoldedStep(CountLine("  " + Sentence(counts)), details, counts);
    }

    private static string Sentence(IReadOnlyDictionary<string, int> counts)
        => ToolActivityAggregator.ComposeSentence([.. counts]);

    private List<RowSegment> TitleRow(int width)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var call in _calls)
            counts[call.Tool] = counts.GetValueOrDefault(call.Tool) + 1;
        var text = TerminalText.Truncate($"{_glyphs.Bullet} {Sentence(counts)}", width, _glyphs.Ellipsis);
        var segments = new List<RowSegment>();
        foreach (var run in CountRuns(text))
            segments.Add(new RowSegment(text.Substring(run.Start, run.Length), PaletteRole.Dim, run.Bold));
        return segments;
    }

    /// <summary>A dim line whose numbers stand out.</summary>
    private static TranscriptLine CountLine(string text) => new(text) { Runs = CountRuns(text) };

    private static List<TranscriptRun> CountRuns(string text)
    {
        var runs = new List<TranscriptRun>();
        var at = 0;
        foreach (Match number in Number().Matches(text))
        {
            if (number.Index > at) runs.Add(new TranscriptRun(at, number.Index - at, PaletteRole.Dim));
            runs.Add(new TranscriptRun(number.Index, number.Length, PaletteRole.Dim, Bold: true));
            at = number.Index + number.Length;
        }
        if (at < text.Length) runs.Add(new TranscriptRun(at, text.Length - at, PaletteRole.Dim));
        return runs;
    }

    private List<RowSegment> CallRow(Call call, string head, int width)
    {
        var (verb, subject, strong) = Phrase(call);
        var segments = new List<RowSegment> { new(head, PaletteRole.Dim) };
        var role = call.Open ? PaletteRole.Dim : PaletteRole.Normal;
        var tail = call.Failed ? " (failed)" : string.Empty;
        var room = width - TerminalText.Columns(head) - TerminalText.Columns(tail);
        var fitted = TerminalText.Truncate(verb + subject, Math.Max(0, room), _glyphs.Ellipsis);
        var verbPart = fitted.Length < verb.Length ? fitted : verb;
        segments.Add(new RowSegment(verbPart, role));
        if (fitted.Length > verbPart.Length)
            segments.Add(new RowSegment(fitted[verbPart.Length..], role, strong));
        if (tail.Length > 0) segments.Add(new RowSegment(tail, PaletteRole.Error));
        return segments;
    }

    private static TranscriptLine DetailLine(Call call, string head)
    {
        var (verb, subject, strong) = Phrase(call);
        var tail = call.Failed ? " (failed)" : string.Empty;
        var runs = new List<TranscriptRun> { new(0, head.Length, PaletteRole.Dim) };
        if (verb.Length > 0) runs.Add(new TranscriptRun(head.Length, verb.Length, PaletteRole.Normal));
        if (subject.Length > 0)
            runs.Add(new TranscriptRun(head.Length + verb.Length, subject.Length, PaletteRole.Normal, strong));
        if (tail.Length > 0)
            runs.Add(new TranscriptRun(head.Length + verb.Length + subject.Length, tail.Length, PaletteRole.Error));
        return new TranscriptLine(head + verb + subject + tail) { Runs = runs, HangingIndent = TreeIndent.Length };
    }

    private static (string Verb, string Subject, bool Strong) Phrase(Call call)
    {
        var verb = call.Tool switch
        {
            "shell_command" => "$ ",
            "file_read" => "Read ",
            "file_write" => "Write ",
            "edit_file" => "Edit ",
            "directory_read" => "List ",
            "directory_search" or "codebase_search" => "Search ",
            _ => null,
        };
        if (verb is null || call.Subject.Length == 0) return (call.Tool, string.Empty, false);
        return (verb, call.Subject, call.Tool != "shell_command");
    }

    [GeneratedRegex(@"\b\d+\b")]
    private static partial Regex Number();
}
