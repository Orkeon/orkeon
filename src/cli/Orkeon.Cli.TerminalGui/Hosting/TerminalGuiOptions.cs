using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Hosting;

public sealed record TerminalGuiOptions
{
    /// <summary>Logs drawer height ratio when opened (drawer rows / total). Default 0.5.</summary>
    public double InitialSplitRatio { get; init; } = 0.5;

    /// <summary>Minimum log level surfaced in the logs pane (UI filter, runtime-tweakable). Default Information.</summary>
    public LogLevel DefaultMinimumLogLevel { get; init; } = LogLevel.Information;

    /// <summary>Max number of log entries kept in the in-memory ring buffer. Default 5000.</summary>
    public int LogsBufferCapacity { get; init; } = 5000;

    /// <summary>Title shown in the logs drawer frame (the one pane that keeps a frame).</summary>
    public string LogsPaneTitle { get; init; } = "Logs";

    /// <summary>
    /// Whether the logs drawer is open at startup. Default FALSE since the fidelity layout
    /// (PLAN phase 1): the reference UI has no log pane — ours is the deliberate addition,
    /// hidden until toggled (Ctrl+G) and announced by the hint bar.
    /// </summary>
    public bool LogsVisibleAtStartup { get; init; }

    /// <summary>
    /// When true (default), the REPL history wraps long lines automatically and the horizontal
    /// scrollbar is hidden. The wrap is display-only: copy/paste returns the original text with no
    /// injected carriage return. When false, the horizontal scrollbar reappears (lines are not wrapped).
    /// </summary>
    public bool ReplWordWrap { get; init; } = true;

    /// <summary>Whether the startup banner is written into the transcript. Default true.</summary>
    public bool BannerEnabled { get; init; } = true;

    /// <summary>
    /// Banner content, supplied by the host application — it knows the model and the
    /// configuration, this layer does not (Cli.Abstractions is its only Orkeon reference).
    /// Null with <see cref="BannerEnabled"/> true renders a product-only line.
    /// </summary>
    public BannerInfo? Banner { get; init; }

    /// <summary>Unicode-vs-ASCII marker policy. Default <see cref="GlyphMode.Auto"/>.</summary>
    public GlyphMode Glyphs { get; init; } = GlyphMode.Auto;

    /// <summary>
    /// Boot-time spinner-verb rotation for the status line (the <c>spinnerVerbs</c>
    /// setting; "thinking verbs" in the tweakcc vocabulary). Null/empty ⇒ the built-in
    /// gerunds. A live value provided through <c>TuiIntegration.SpinnerVerbs</c>
    /// (e.g. exp07's <c>/config set spinnerVerbs …</c>) wins over this.
    /// </summary>
    public IReadOnlyList<string>? SpinnerVerbs { get; init; }
}
