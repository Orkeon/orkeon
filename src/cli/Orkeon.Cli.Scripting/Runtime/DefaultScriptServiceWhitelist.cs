using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools;

namespace Orkeon.Cli.Scripting.Runtime;

/// <summary>
/// Builds the default <see cref="ScriptServiceWhitelist"/> used when the host does not
/// supply its own. Only entries that map to interfaces present in the repo are added —
/// no speculative <c>ICrewLoader</c> (which doesn't exist) per spec §8.3.
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
            .AddOptional(ScriptServiceKeys.Llm, sp => sp.GetService<ILlmProvider>())
            .AddOptional(ScriptServiceKeys.Logger, sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger("script"))
            // Optional: present only when the host wired the command-dispatch substrate (AddScriptCommands).
            .AddOptional(ScriptServiceKeys.Commands, sp => sp.GetService<CommandDispatchService>())
            // Optional: the crew-launching façade (exp 07 SPEC §6) — present when AddScriptCommands ran.
            .AddOptional(ScriptServiceKeys.ScriptHost, sp => sp.GetService<ScriptHostFacade>());
    }

    /// <summary>
    /// Resolver behind the <c>tools</c> entry — cached once: transient IDisposable tools must
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
        private volatile IBaseTool[]? _tools;

        public IBaseTool[] Resolve(IServiceProvider sp)
        {
            var tools = _tools;
            if (tools is null)
            {
                lock (_gate)
                {
                    tools = _tools ??= sp.GetServices<IBaseTool>().ToArray();
                }
            }
            // Fresh array shell per get, same cached instances inside.
            return tools[..];
        }
    }
}
