using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Interop.AgentCli.Engine;
using Orkeon.Interop.AgentCli.Process;
using Orkeon.Interop.AgentCli.Sessions;

namespace Orkeon.Interop.AgentCli;

/// <summary>
/// The provider module of <c>Llm:Provider = agent-cli</c> (ADR-020): the factory hands it every section that
/// names the key, and wraps what it builds like any other provider (cassette, call mode, meter, budget). The
/// module owns the agent CLI processes of the host: disposing it ends them.
/// </summary>
[Experimental(AgentCliDiagnostics.ExperimentalId, UrlFormat = AgentCliDiagnostics.ExperimentalUrl)]
public sealed class AgentCliProviderModule : ILlmProviderModule, IAsyncDisposable
{
    private readonly AgentCliOptions _options;
    private readonly Lock _gate = new();
    private AgentCliProcessHost? _host;

    /// <summary>A module on <paramref name="options"/>, the <c>Llm:AgentCli</c> section.</summary>
    public AgentCliProviderModule(AgentCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string Key => AgentCliDiagnostics.ProviderKey;

    /// <inheritdoc />
    public ILlmProvider Create(LlmConfig config, LlmProviderModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(context);

        var logger = context.LoggerFactory.CreateLogger("Orkeon.Interop.AgentCli");
        AgentCliProcessHost host;
        lock (_gate)
        {
            host = _host ??= new AgentCliProcessHost(
                new AgentCliProcessLauncher(logger), EngineOf(_options), _options, logger);
        }

        return new AgentCliLlmProvider(host, config, context.LoggerFactory.CreateLogger<AgentCliLlmProvider>());
    }

    private static ClaudeCodeEngine EngineOf(AgentCliOptions options) =>
        string.Equals(options.Engine, AgentCliEngines.Claude, StringComparison.OrdinalIgnoreCase)
            ? new ClaudeCodeEngine(options)
            : throw new NotSupportedException(
                $"Llm:AgentCli:Engine '{options.Engine}' is not an engine of the agent-cli provider: '{AgentCliEngines.Claude}' is the only one.");

    /// <summary>Ends every agent CLI process the module started.</summary>
    public async ValueTask DisposeAsync()
    {
        AgentCliProcessHost? host;
        lock (_gate)
        {
            host = _host;
            _host = null;
        }

        if (host is not null)
            await host.DisposeAsync().ConfigureAwait(false);
    }
}
