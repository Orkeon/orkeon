using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Configuration;
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
    /// <summary>The section streaming is configured by.</summary>
    private const string SectionName = "Orkeon:Cli:ConsoleStreaming";

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

        // Read here, at registration; its keys are judged with every declared section's when the
        // host starts (GAP-40), whether streaming is on or not.
        services.DeclareSettingsShape(SectionName, typeof(ConsoleStreamingSettingsShape));
        var section = configuration.GetSection(SectionName);
        if (!section.GetValue("Enabled", new ConsoleStreamingSettingsShape().Enabled))
            return services;

        services.TryAddSingleton<ILlmDeltaSink>(sp =>
            new ConsoleLlmDeltaSink(sp.GetRequiredService<IConsoleAdapter>()));
        return services;
    }
}

/// <summary>Whether the interactive console prints a model's answer as it arrives.</summary>
/// <remarks>Its property is the key, and its value on a new instance the default the registration applies: nothing binds it.</remarks>
internal sealed class ConsoleStreamingSettingsShape
{
    /// <summary>Whether each piece of an answer is written to the console as the model sends it, instead of the whole answer at its end.</summary>
    public bool Enabled { get; set; }
}
