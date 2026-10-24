using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>
/// What a log line IS, decided by whoever produced it. Never sniffed from the text: the
/// outcome sentence is localized, so a regex over it would colour the console in English
/// and leave it grey in every other language.
/// </summary>
public enum LogLineKind
{
    /// <summary>Ordinary output, from the child or from Studio.</summary>
    Output,

    /// <summary>The command about to run, echoed before it does.</summary>
    Command,

    /// <summary>The closing verdict of a run that ended well.</summary>
    Outcome,

    /// <summary>An error Studio itself reports.</summary>
    Error,
}

/// <summary>One streamed output line, with the channel it came from.</summary>
public sealed class LogLineViewModel
{
    private readonly CliLogLine _log;

    /// <summary>Wraps a line read from the child process.</summary>
    /// <param name="line">The line.</param>
    /// <param name="kind">What the producer says it is.</param>
    /// <param name="continues">
    /// The level of the stderr log entry this line follows, when it carries none itself: the rest of a
    /// multi-line message (a model answer at <c>-v 2</c>) or the block the CLI prints after its logs
    /// (« === Crew Output === »), which under <c>--events</c> go to stderr too.
    /// </param>
    public LogLineViewModel(ProcessOutputLine line, LogLineKind kind = LogLineKind.Output, CliLogLevel? continues = null)
    {
        ArgumentNullException.ThrowIfNull(line);

        Line = line;
        Kind = kind;
        _log = CliLogLine.Parse(line.Text);
        Level = _log.Level ?? (kind == LogLineKind.Output && line.Channel == ProcessOutputChannel.StandardError ? continues : null);
    }

    /// <summary>What this line is — the console colours on it.</summary>
    public LogLineKind Kind { get; }

    /// <summary>Whether it is the echoed command.</summary>
    public bool IsCommand => Kind == LogLineKind.Command;

    /// <summary>Whether it is a closing verdict.</summary>
    public bool IsOutcome => Kind == LogLineKind.Outcome;

    /// <summary>The underlying Core record.</summary>
    public ProcessOutputLine Line { get; }

    /// <summary>
    /// The text as the CLI printed it, without the console logger's own timestamp: the journal
    /// already shows <see cref="Time"/>, and two clocks a few milliseconds apart said nothing more.
    /// </summary>
    public string Text => _log.Text;

    /// <summary>The level of a CLI log line, or of the log entry a stderr line continues; null otherwise.</summary>
    public CliLogLevel? Level { get; }

    /// <summary>Which stream it came from.</summary>
    public ProcessOutputChannel Channel => Line.Channel;

    /// <summary>
    /// Whether the view colours it as an error: an error Studio reports, a <c>fail</c> or <c>crit</c>
    /// log entry, or a stderr line that follows no log entry (a fault message before any log). Never
    /// the stream alone: under <c>--events</c> every log goes to stderr, and an <c>info</c> line is
    /// not an error.
    /// </summary>
    public bool IsError => Kind == LogLineKind.Error || Level switch
    {
        CliLogLevel.Error or CliLogLevel.Critical => true,
        null => Kind == LogLineKind.Output && Channel == ProcessOutputChannel.StandardError,
        _ => false,
    };

    /// <summary>Whether it belongs to a <c>warn</c> log entry, which the view colours as a warning.</summary>
    public bool IsWarning => Level == CliLogLevel.Warning;

    /// <summary>When it was read.</summary>
    public DateTimeOffset TimestampUtc => Line.TimestampUtc;

    /// <summary>
    /// When it was read, as the journal shows it: local time to the second (STUDIO-17). The
    /// stamp was captured from the first version on and never displayed; a seven-minute run
    /// gave no way to tell when the process last spoke.
    /// </summary>
    public string Time => TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// The streamed log panel of spec §5.3. The view renders it in a virtualized list, and the collection
/// is capped at <see cref="MaxLines"/> so a chatty run cannot grow the process without bound —
/// virtualization keeps the rendering cheap, not the memory.
/// </summary>
public sealed class RunLogViewModel : ObservableObject
{
    /// <summary>How many lines are kept before the oldest are dropped.</summary>
    public const int MaxLines = 20_000;

    private readonly IStudioStrings _strings;
    private bool _autoScroll = true;
    private int _droppedLines;
    private CliLogLevel? _stderrLevel;

    /// <summary>Builds the panel over the localization port (STUDIO-11).</summary>
    public RunLogViewModel(IStudioStrings? strings = null)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    /// <summary>The lines currently held, oldest first.</summary>
    public ObservableCollection<LogLineViewModel> Lines { get; } = [];

    /// <summary>There is something to copy once the journal holds at least one line.</summary>
    public bool CanCopy => Lines.Count > 0;

    /// <summary>
    /// The whole journal as plain text for the clipboard, oldest first, each line led by the
    /// time it was read — what a bug report needs. WPF text blocks are not selectable; this
    /// is how the operator gets the log out of the window. A truncation note leads when
    /// lines were dropped.
    /// </summary>
    public string BuildText()
    {
        var lines = Lines.Select(l => $"{l.Time}  {l.Text}");
        return DroppedLines > 0
            ? string.Join(Environment.NewLine, lines.Prepend(Summary))
            : string.Join(Environment.NewLine, lines);
    }

    /// <summary>Whether the view should follow the tail.</summary>
    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    /// <summary>How many lines were dropped to stay under <see cref="MaxLines"/>.</summary>
    public int DroppedLines
    {
        get => _droppedLines;
        private set
        {
            if (SetProperty(ref _droppedLines, value))
                OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>The status line under the log panel.</summary>
    public string Summary => DroppedLines == 0
        ? _strings.Format(CultureInfo.InvariantCulture, StudioStringKeys.LogLines, Lines.Count)
        : _strings.Format(
            CultureInfo.InvariantCulture,
            StudioStringKeys.LogLinesDropped, Lines.Count, DroppedLines, MaxLines);

    /// <summary>Appends a streamed line, evicting the oldest once the cap is reached.</summary>
    public void Append(ProcessOutputLine line, LogLineKind kind = LogLineKind.Output)
    {
        ArgumentNullException.ThrowIfNull(line);

        var added = new LogLineViewModel(line, kind, _stderrLevel);
        Lines.Add(added);

        // A stderr line without a level continues the last log entry; a new command starts afresh.
        if (kind == LogLineKind.Command)
            _stderrLevel = null;
        else if (kind == LogLineKind.Output && line.Channel == ProcessOutputChannel.StandardError)
            _stderrLevel = added.Level;

        if (Lines.Count > MaxLines)
        {
            Lines.RemoveAt(0);
            DroppedLines++;
        }

        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Appends a line Studio itself produced, e.g. the command line about to run.</summary>
    public void AppendNotice(string text) =>
        Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, text));

    /// <summary>Appends the command about to run — the console reads it in its own colour.</summary>
    public void AppendCommand(string text) =>
        Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, text), LogLineKind.Command);

    /// <summary>Appends the closing verdict of a run.</summary>
    public void AppendOutcome(string text) =>
        Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, text), LogLineKind.Outcome);

    /// <summary>Appends a Studio-produced error line.</summary>
    public void AppendError(string text) =>
        Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, text), LogLineKind.Error);

    /// <summary>Empties the panel.</summary>
    public void Clear()
    {
        Lines.Clear();
        DroppedLines = 0;
        _stderrLevel = null;
        OnPropertyChanged(nameof(Summary));
    }
}
