using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Streaming;

namespace Orkeon.Cli.Commands.Scripting.DependencyInjection;

/// <summary>
/// DI extension for native incremental rendering of streamed LLM output.
/// Config opt-in: without <c>Orkeon:Cli:ConsoleStreaming:Enabled = true</c> nothing is
/// registered and <c>ctx.llm.act</c> keeps its buffered rendering — scripts can still
/// stream explicitly via the <c>onDelta</c> act option.
/// </summary>
public static class ConsoleStreamingExtensions
{
    /// <summary>
    /// Registers <see cref="ConsoleLlmDeltaSink"/> as the <see cref="ILlmDeltaSink"/> when
    /// the <c>Orkeon:Cli:ConsoleStreaming</c> section enables it. Requires an
    /// <see cref="IConsoleAdapter"/> in the container (any CLI host has one). Idempotent
    /// (<c>TryAddSingleton</c> — a host-supplied sink wins).
    /// </summary>
    public static IServiceCollection AddLlmConsoleStreaming(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("Orkeon:Cli:ConsoleStreaming");
        if (!section.GetValue("Enabled", false))
            return services;

        services.TryAddSingleton<ILlmDeltaSink>(sp =>
            new ConsoleLlmDeltaSink(sp.GetRequiredService<IConsoleAdapter>()));
        return services;
    }
}
