namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>
/// The values the startup banner renders. Supplied by the HOST application
/// (which knows the configuration and the provider), never probed from here:
/// this project only references <c>Orkeon.Cli.Abstractions</c>, and reaching into
/// the LLM configuration would smuggle an Application dependency into the UI layer.
/// </summary>
/// <remarks>
/// Every field is display-ready text. A null/empty field drops its line rather than
/// rendering a placeholder — the reference UI omits what it does not know (a session
/// with no context-window figure shows no <c>(1M context)</c> parenthesis; it does not
/// invent one).
/// </remarks>
public sealed record BannerInfo
{
    /// <summary>Product + version line, e.g. <c>"Orkeon Coding Agent — orkeon-repl 0.9.2-beta"</c>.</summary>
    public required string ProductLine { get; init; }

    /// <summary>Model line, e.g. <c>"kimi-k3 (1M context) · Moonshot"</c>. Empty ⇒ omitted.</summary>
    public string ModelLine { get; init; } = "";

    /// <summary>Working directory line, e.g. <c>"/workspace"</c>. Empty ⇒ omitted.</summary>
    public string WorkspaceLine { get; init; } = "";

    /// <summary>Tip lines shown under the banner, already worded. Empty ⇒ no tip block.</summary>
    public IReadOnlyList<string> Tips { get; init; } = [];
}
