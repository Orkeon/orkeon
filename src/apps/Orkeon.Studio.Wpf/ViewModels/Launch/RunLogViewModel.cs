using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>One streamed output line, with the channel it came from.</summary>
public sealed class LogLineViewModel
{
    /// <summary>Wraps a line read from the child process.</summary>
    public LogLineViewModel(ProcessOutputLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Line = line;
    }

    /// <summary>The underlying Core record.</summary>
    public ProcessOutputLine Line { get; }

    /// <summary>The text as the CLI printed it.</summary>
    public string Text => Line.Text;

    /// <summary>Which stream it came from.</summary>
    public ProcessOutputChannel Channel => Line.Channel;

    /// <summary>Whether it came from stderr, which the view colours differently.</summary>
    public bool IsError => Channel == ProcessOutputChannel.StandardError;

    /// <summary>When it was read.</summary>
    public DateTimeOffset TimestampUtc => Line.TimestampUtc;

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

    private bool _autoScroll = true;
    private int _droppedLines;

    /// <summary>The lines currently held, oldest first.</summary>
    public ObservableCollection<LogLineViewModel> Lines { get; } = [];

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
        ? string.Create(CultureInfo.InvariantCulture, $"{Lines.Count} line(s).")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{Lines.Count} line(s); {DroppedLines} older line(s) dropped (cap {MaxLines}).");

    /// <summary>Appends a streamed line, evicting the oldest once the cap is reached.</summary>
    public void Append(ProcessOutputLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Lines.Add(new LogLineViewModel(line));

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

    /// <summary>Appends a Studio-produced error line.</summary>
    public void AppendError(string text) =>
        Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, text));

    /// <summary>Empties the panel.</summary>
    public void Clear()
    {
        Lines.Clear();
        DroppedLines = 0;
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>The whole buffer as text, for a copy-to-clipboard action.</summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        foreach (var line in Lines)
            builder.AppendLine(line.Text);

        return builder.ToString();
    }
}
