using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Session;
using Orkeon.Scripting;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

/// <summary>
/// Builds the default <see cref="ScriptServiceWhitelist"/> used when the host does not
/// supply its own. Only entries that map to interfaces present in the repo are added —
/// no speculative <c>ICrewLoader</c> (which doesn't exist).
/// </summary>
public static class DefaultScriptServiceWhitelist
{
    /// <summary>
    /// Returns a builder pre-populated with the canonical defaults. Hosts add their own
    /// services via <see cref="ScriptServiceWhitelist.Add"/> / <see cref="ScriptServiceWhitelist.AddOptional"/>.
    /// </summary>
    public static ScriptServiceWhitelist Build()
    {
        var tools = new CachedToolsResolver();
        return new ScriptServiceWhitelist()
            .Add(ScriptServiceKeys.FileSystem, sp => sp.GetRequiredService<IFileSystemService>())
            // R2.6 / SEC-009: IConfiguration is deliberately NOT exposed to untrusted scripts.
            // The raw config root carries API keys and connection strings; surfacing it via
            // ctx.services.get('configuration') would leak secrets to .ork.ts code of unknown
            // provenance. A host that genuinely needs to expose select, non-sensitive config
            // keys must add a *filtered* view explicitly via ScriptServiceWhitelist.Add.
            .Add(ScriptServiceKeys.Tools, tools.Resolve)
            // The runtime's version and install channel (#225): the host's own registration, else the entry
            // assembly's version with an unknown channel.
            .Add(ScriptServiceKeys.Runtime, sp => sp.GetService<ScriptRuntimeInfo>()
                ?? ScriptRuntimeInfo.FromAssembly(Assembly.GetEntryAssembly(), ScriptRuntimeInfo.Unknown))
            .AddOptional(ScriptServiceKeys.Llm, sp => sp.GetService<ILlmProvider>())
            .AddOptional(ScriptServiceKeys.Logger, sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger("script"))
            // Optional: present only when the host wired the command-dispatch substrate (AddScriptCommands).
            .AddOptional(ScriptServiceKeys.Commands, sp => sp.GetService<CommandDispatchService>())
            // Optional: the session's tool-call rules — present when the host keeps them (AddConsoleToolApproval).
            // `enabled` reads whether a gate is registered at all: AddOrkeonPermissionGate registers none unless
            // Orkeon:Security:PermissionGate:Enabled is true, and the rules then check nothing (#128).
            .AddOptional(ScriptServiceKeys.Permissions, sp => sp.GetService<SessionToolPermissionRules>() is { } rules
                ? new PermissionRulesFacade(rules, gateEnabled: sp.GetService<IPermissionGate>() is not null)
                : null)
            // Optional: the crew-launching facade — present when AddScriptCommands ran.
            .AddOptional(ScriptServiceKeys.ScriptHost, sp => sp.GetService<ScriptHostFacade>())
            // Optional, read-only: the tool approvals waiting for the operator — present when the host watches
            // them (AddConsoleToolApproval registers IToolApprovalWatch).
            .AddOptional(ScriptServiceKeys.Approvals, sp => sp.GetService<IToolApprovalWatch>() is { } watch
                ? new ScriptPendingApprovals(watch)
                : null)
            // Optional: the persisted session (Orkeon:Cli:Session:Persist), saved now or swapped for a saved one (#227).
            .AddOptional(ScriptServiceKeys.Session, sp => sp.GetService<ISessionBufferService>() is FileSessionBufferService session
                ? new SessionFacade(session)
                : null)
            // Optional, read-only: the session's usage by model (the cost manager's report) and by command
            // instance (what InstanceAttributingUsageSink credited each one) — present when either is kept.
            .AddOptional(ScriptServiceKeys.Usage, sp =>
                (sp.GetService<ICostBudgetManager>(), sp.GetService<CommandInstanceRegistry>()) is var (cost, instances)
                && (cost is not null || instances is not null)
                    ? new ScriptSessionUsage(cost, instances)
                    : null);
    }

    /// <summary>
    /// Resolver behind the <c>tools</c> entry: the container's tools, cached once, then the tools the
    /// registry holds besides them (an MCP server's), read at each get. Cached: transient IDisposable tools must
    /// not be re-materialized per get (ANT-005). Tools are registered transient and
    /// <c>ToolBase</c> is <see cref="IDisposable"/>, so each <c>GetServices&lt;IBaseTool&gt;()</c>
    /// materializes a fresh set that the root container retains until shutdown — over a long
    /// REPL session every <c>ctx.services.get("tools")</c> grew memory without bound. The
    /// container is immutable post-build (the de facto contract), so the first resolution is
    /// authoritative. Each get hands out a defensive shallow copy of the cached array, so no
    /// script can mutate the snapshot shared with other scripts.
    /// </summary>
    private sealed class CachedToolsResolver
    {
        private readonly object _gate = new();
        private volatile ScriptToolCatalogue? _tools;

        public IBaseTool[] Resolve(IServiceProvider sp)
        {
            var tools = _tools;
            if (tools is null)
            {
                lock (_gate)
                {
                    tools = _tools ??= new ScriptToolCatalogue(sp.GetServices<IBaseTool>(), sp.GetService<IToolRegistry>());
                }
            }
            // Fresh array shell per get, same cached instances inside.
            return [.. tools];
        }
    }
}
