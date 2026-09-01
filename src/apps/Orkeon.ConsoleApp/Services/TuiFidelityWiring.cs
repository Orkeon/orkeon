using Orkeon.Constants.Configuration;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Progress;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Services;

/// <summary>
/// Populates the TUI's <see cref="TuiIntegration"/> delegate bag and builds the startup
/// <see cref="BannerInfo"/>. This is the one place the fidelity views touch the
/// application services — the TerminalGui layer only references Cli.Abstractions, so
/// the session state bag, the cost tracker and the command-instance registry all have
/// to be closed over HERE, where the service provider exists (PLAN §3).
/// </summary>
internal static class TuiFidelityWiring
{
    /// <summary>Session-state key of the /mode default — the exp07 vocabulary.</summary>
    private const string DefaultModeStateKey = "default_permission_mode";

    /// <summary>Session-state key of exp07's /config override map (B-6 vocabulary).</summary>
    private const string ConfigMapStateKey = "config_map";

    /// <summary>exp07's persisted /config settings file (VFS path).</summary>
    private const string ConfigFilePath = "/workspace/.orkeon/config.json";

    /// <summary>The /config key carrying the spinner-verb rotation (CSV).</summary>
    private const string SpinnerVerbsKey = "spinnerVerbs";

    /// <summary>
    /// The hint bar's Shift+Tab cycle. Mirrors exp07's E-12 order exactly, `dontAsk`
    /// excluded from the cycle like the original — it is reachable by name, not by tab.
    /// </summary>
    private static readonly string[] ModeCycle = ["default", "acceptEdits", "plan", "bypassPermissions"];

    /// <summary>Builds the banner from what the host actually knows. Empty parts are omitted.</summary>
    public static BannerInfo BuildBannerInfo(IConfiguration configuration)
    {
        var version = typeof(TuiFidelityWiring).Assembly.GetName().Version?.ToString(3) ?? "dev";
        var model = configuration["Llm:Model"];
        var window = configuration[ConfigurationKeys.CliSessionContextWindowTokens];
        var baseUrl = configuration["Llm:BaseUrl"];

        var modelLine = string.IsNullOrWhiteSpace(model)
            ? "" // no Llm section: the line is omitted, and /doctor + the boot warning say why
            : model
              + (long.TryParse(window, out var w) && w > 0 ? $" ({FormatWindow(w)} context)" : "")
              + (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? $" · {uri.Host}" : "");

        return new BannerInfo
        {
            ProductLine = $"Orkeon Coding Agent — orkeon-repl {version}",
            ModelLine = modelLine,
            WorkspaceLine = "/workspace",
            Tips =
            [
                "Switch models anytime with /model. Cycle permissions with shift+tab.",
                "+more · /status",
            ],
        };
    }

    /// <summary>
    /// Fills the delegate bag from the built provider. Every closure is defensive:
    /// a missing service leaves its delegate null and the view omits the readout.
    /// </summary>
    public static void Wire(IServiceProvider services)
    {
        var integration = services.GetRequiredService<TuiIntegration>();
        var buffer = services.GetService<ISessionBufferService>();
        var cost = services.GetService<ICostBudgetManager>();
        var dispatch = services.GetService<CommandDispatchService>();

        if (buffer is not null)
        {
            integration.PermissionMode = () => buffer.GetState(DefaultModeStateKey) ?? "default";
            integration.CyclePermissionMode = () =>
            {
                var current = buffer.GetState(DefaultModeStateKey) ?? "default";
                var idx = Array.IndexOf(ModeCycle, current);
                buffer.SetState(DefaultModeStateKey, ModeCycle[(idx + 1 + ModeCycle.Length) % ModeCycle.Length]);
            };
            // Chip: the session title (/rename) when set, else the workspace — session
            // styling (blue). Agent-target styling waits on addressable sub-agents (PLAN §7).
            integration.ContextChip = () =>
            {
                var title = buffer.GetMetadata().Title;
                return new ContextChipInfo(
                    string.IsNullOrWhiteSpace(title) ? "workspace" : title!,
                    IsAgentTarget: false);
            };
        }

        if (cost is not null)
            integration.SessionTokens = () => cost.GetReport().TotalTokens;

        if (dispatch is not null)
        {
            integration.AgentRows = () => BuildAgentRows(dispatch);
            integration.InterruptCurrent = () =>
            {
                // Cancel the most recent still-running async instance — the reference's
                // "esc to interrupt" acts on the turn in flight.
                var running = dispatch.list()
                    .Where(v => v.state is "running" or "dispatched")
                    .OrderByDescending(v => v.startedAt, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (running is not null) dispatch.cancel(running.ticket);
            };
            integration.DescribeAgent = ticket => DescribeInstance(dispatch.get(ticket));
        }

        if (services.GetService<ProgressBroker>() is { } broker)
        {
            integration.Progress = BuildProgressReader(broker, dispatch);
        }

        // The file layer is a closure over the VFS service so the reader itself carries
        // no (analyzer-forbidden) nullable IFileSystemService dependency.
        var fs = services.GetService<IFileSystemService>();
        integration.SpinnerVerbs = BuildSpinnerVerbsReader(
            buffer,
            fs is null ? null : ct => fs.TryReadAllTextAsync(ConfigFilePath, ct));
    }

    /// <summary>
    /// The status line's progress source, with the staleness guard the design review
    /// called for: a snapshot stamped with a ticket is cross-checked against the
    /// dispatch registry — if its instance is terminal (or gone), the slot is swept and
    /// nothing renders, so a crew that died between report and done cannot park a bar
    /// forever. Unticketed snapshots pass through with <c>FromLiveInstance=false</c>;
    /// the view then only renders them while a foreground command runs.
    /// </summary>
    internal static Func<ProgressInfo?> BuildProgressReader(ProgressBroker broker, CommandDispatchService? dispatch)
    {
        return () =>
        {
            if (broker.Current is not { } s) return null;

            var fromLiveInstance = false;
            if (!string.IsNullOrEmpty(s.Ticket))
            {
                var instance = dispatch?.get(s.Ticket!);
                if (instance is null || instance.state is not ("running" or "dispatched"))
                {
                    // Defensive sweep on top of postWork's finally-clear: the owner is
                    // gone, the snapshot is a leftover.
                    broker.ClearTicket(s.Ticket!);
                    return null;
                }
                fromLiveInstance = true;
            }

            return new ProgressInfo
            {
                Label = s.Label,
                Ratio = s.Ratio,
                Message = s.Message,
                StartedAt = s.StartedAt,
                FromLiveInstance = fromLiveInstance,
            };
        };
    }

    /// <summary>
    /// Live reader of the <c>spinnerVerbs</c> setting, layered like exp07's /config:
    /// session override map first, persisted settings file second. The status line polls
    /// several times a second, so the file layer is cached briefly; the session map is an
    /// in-memory read and stays live.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Config-read fault barrier: a malformed map or unreadable file degrades to the default verbs, never crashes the UI timer.")]
    private static Func<IReadOnlyList<string>?> BuildSpinnerVerbsReader(
        ISessionBufferService? buffer,
        Func<CancellationToken, Task<string?>>? readConfigFile)
    {
        string[]? fileVerbs = null;
        var fileReadAt = DateTimeOffset.MinValue;

        return () =>
        {
            try
            {
                if (ReadVerbsFromJsonMap(buffer?.GetState(ConfigMapStateKey)) is { Length: > 0 } session)
                    return session;

                if (readConfigFile is null) return null;
                var now = DateTimeOffset.UtcNow;
                if (now - fileReadAt > TimeSpan.FromSeconds(5))
                {
                    fileReadAt = now;
                    var content = readConfigFile(CancellationToken.None).GetAwaiter().GetResult();
                    fileVerbs = ReadVerbsFromJsonMap(content);
                }
                return fileVerbs;
            }
            catch
            {
                return null;
            }
        };
    }

    /// <summary>Extracts the CSV <c>spinnerVerbs</c> entry from a JSON object map, or null.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Config-parse fault barrier: malformed JSON reads as an unset key.")]
    private static string[]? ReadVerbsFromJsonMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(SpinnerVerbsKey, out var el) || el.ValueKind != JsonValueKind.String)
                return null;
            var verbs = (el.GetString() ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return verbs.Length > 0 ? verbs : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Transcript detail for one instance (the agents pane's Enter action).</summary>
    private static string? DescribeInstance(CommandInstanceView? view)
    {
        if (view is null) return null;
        var lines = new List<string>(6)
        {
            $"[{view.ticket}] {view.name}{(string.IsNullOrEmpty(view.targetAgent) ? "" : $" → {view.targetAgent}")}",
            $"  state   : {view.state}",
            $"  intent  : {(string.IsNullOrEmpty(view.intent) ? "—" : view.intent)}",
            $"  elapsed : {TimeSpan.FromMilliseconds(view.elapsedMs):hh\\:mm\\:ss}",
        };
        if (view.progress?.message is { Length: > 0 } pm) lines.Add($"  progress: {pm}");
        if (view.tokens > 0) lines.Add($"  tokens  : {view.tokens:N0}");
        if (!string.IsNullOrEmpty(view.error)) lines.Add($"  error   : {Truncate(view.error!, 300)}");
        else if (view.result?.payload is { Length: > 0 } payload) lines.Add($"  result  : {Truncate(payload, 300)}");
        return string.Join("\n", lines);
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    /// <summary>
    /// The agents rows, mirroring the reference's contract: LIVE work only. One row per
    /// RUNNING async instance (hollow bullet), preceded by <c>● main</c> — which appears
    /// only while at least one agent runs; with nothing delegated the list is empty and
    /// the pane collapses. Finished agents leave the pane immediately — <c>idle</c> means
    /// *waiting*, not *finished* (user ruling 2026-08-06) — and stay auditable via
    /// <c>ps</c>/<c>inspect</c>.
    /// </summary>
    internal static List<AgentRowInfo> BuildAgentRows(CommandDispatchService dispatch)
    {
        var instances = dispatch.list().Where(IsRunning).ToList();
        if (instances.Count == 0) return [];

        var rows = new List<AgentRowInfo>(instances.Count + 1)
        {
            // The primary loop: filled bullet, no description, no metrics — `● main` alone.
            new() { Name = "main", IsActive = true },
        };

        rows.AddRange(instances
            .OrderByDescending(v => v.startedAt, StringComparer.Ordinal)
            .Select(v =>
            {
                // A live progress snapshot replaces the static intent while it runs —
                // "pass 2/3 · compacting" says more than the launch wording.
                var description = v.progress is { } p
                    ? ComposeProgressDescription(p)
                    : v.intent ?? "";
                return new AgentRowInfo
                {
                    Name = string.IsNullOrEmpty(v.targetAgent) ? v.name : $"{v.name}@{v.targetAgent}",
                    Description = description,
                    Elapsed = v.elapsedMs is > 0 ? TimeSpan.FromMilliseconds(v.elapsedMs) : null,
                    Tokens = v.tokens > 0 ? v.tokens : null,
                    IsActive = false,
                    Ticket = v.ticket,
                };
            }));
        return rows;
    }

    private static bool IsRunning(CommandInstanceView v) => v.state is "running" or "dispatched";

    private static string ComposeProgressDescription(CommandProgress progress)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(progress.message)) parts.Add(progress.message!.Trim());
        if (progress.percent is { } pc) parts.Add($"{(int)Math.Floor(Math.Clamp(pc, 0, 100))}%");
        else if (progress.step is { } st) parts.Add($"step {st}");
        return string.Join(" · ", parts);
    }

    private static string FormatWindow(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000}M",
        >= 1_000 => $"{tokens / 1_000}K",
        _ => tokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
