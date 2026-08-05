using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Cli.TerminalGui.Hosting;

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
        var window = configuration["Orkeon:Cli:Session:ContextWindowTokens"];
        var baseUrl = configuration["Llm:BaseUrl"];

        var modelLine = string.IsNullOrWhiteSpace(model)
            ? "" // no Llm section: the line is omitted, and /doctor + the boot warning say why
            : model
              + (long.TryParse(window, out var w) && w > 0 ? $" ({FormatWindow(w)} context)" : "")
              + (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? $" · {uri.Host}" : "");

        return new BannerInfo
        {
            ProductLine = $"Orkéon Coding Agent — orkeon-repl {version}",
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
        }
    }

    /// <summary>
    /// The agents rows: <c>main</c> (the REPL itself) first, then one row per async
    /// command instance, running first then most-recent, capped by the pane.
    /// </summary>
    private static List<AgentRowInfo> BuildAgentRows(CommandDispatchService dispatch)
    {
        var instances = dispatch.list();
        var anyRunning = instances.Any(v => v.state is "running" or "dispatched");

        var rows = new List<AgentRowInfo>
        {
            // `main` mirrors the reference: the primary loop, active when nothing is delegated.
            new() { Name = "main", IsActive = !anyRunning, IsIdle = true },
        };

        rows.AddRange(instances
            .OrderByDescending(v => v.state is "running" or "dispatched")
            .ThenByDescending(v => v.startedAt, StringComparer.Ordinal)
            .Select(v =>
            {
                var running = v.state is "running" or "dispatched";
                return new AgentRowInfo
                {
                    Name = string.IsNullOrEmpty(v.targetAgent) ? v.name : $"{v.name}@{v.targetAgent}",
                    Description = v.intent ?? "",
                    Elapsed = v.elapsedMs is > 0 ? TimeSpan.FromMilliseconds(v.elapsedMs) : null,
                    // Per-ticket token attribution needs a correlation tag on CostUsageEvent
                    // (PLAN TUI-G2): rendered as `—` rather than a number that lies.
                    Tokens = null,
                    IsActive = running,
                    IsIdle = !running,
                };
            }));
        return rows;
    }

    private static string FormatWindow(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000}M",
        >= 1_000 => $"{tokens / 1_000}K",
        _ => tokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
