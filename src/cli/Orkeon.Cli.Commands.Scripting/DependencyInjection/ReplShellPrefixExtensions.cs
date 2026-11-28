using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Shell;

namespace Orkeon.Cli.Commands.Scripting.DependencyInjection;

/// <summary>
/// DI extension for the REPL's shell prefix: a line typed at the prompt that starts with <c>!</c> runs the rest in
/// the operator's own shell (<c>! git status</c>), without a turn of the model, and its output joins the session.
/// The <c>Orkeon:Cli:Repl:ShellPrefix</c> section turns it off (<c>Enabled</c>) and bounds it (<c>TimeoutSeconds</c>).
/// </summary>
public static class ReplShellPrefixExtensions
{
    /// <summary>The timeout a command meets when the section sets none, in seconds.</summary>
    private const int DefaultTimeoutSeconds = 120;

    /// <summary>
    /// Registers the <see cref="IOperatorShell"/> the REPL's read loop runs a <c>!</c> line with — off when
    /// <c>Orkeon:Cli:Repl:ShellPrefix:Enabled</c> is false, in which case the line is refused with a sentence naming the
    /// key. Idempotent (<c>TryAdd</c>: a host-supplied shell wins).
    /// </summary>
    /// <param name="services">The REPL's services.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <exception cref="InvalidOperationException"><c>TimeoutSeconds</c> is not a number of seconds above zero.</exception>
    public static IServiceCollection AddReplShellPrefix(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.DeclareSettingsShape(OperatorShell.SectionName, typeof(ReplShellPrefixSettingsShape));
        var section = configuration.GetSection(OperatorShell.SectionName);
        var enabled = section.GetValue("Enabled", true);
        var timeoutSeconds = section.GetValue("TimeoutSeconds", DefaultTimeoutSeconds);
        if (timeoutSeconds < 1)
        {
            throw new InvalidOperationException(
                $"{OperatorShell.SectionName}:TimeoutSeconds is '{timeoutSeconds.ToString(CultureInfo.InvariantCulture)}', which is not a timeout: " +
                $"write a number of seconds above zero, such as {DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}.");
        }

        services.TryAddSingleton(new OperatorShellSettings(enabled, TimeSpan.FromSeconds(timeoutSeconds)));
        services.TryAddSingleton<IOperatorShellRunner, ProcessOperatorShellRunner>();
        services.TryAddSingleton<IOperatorShell>(sp => new OperatorShell(
            sp.GetRequiredService<IOperatorShellRunner>(),
            sp.GetRequiredService<OperatorShellSettings>(),
            sp.GetService<ISessionBufferService>()));
        return services;
    }
}

/// <summary>Whether a line typed at the REPL's prompt that starts with <c>!</c> runs a shell command of the operator's own.</summary>
/// <remarks>Its properties are the keys, and their values on a new instance the defaults the registration applies: nothing binds it.</remarks>
internal sealed class ReplShellPrefixSettingsShape
{
    /// <summary>Whether a line typed at the prompt that starts with ! runs the rest in the operator's own shell, without a turn of the model, its output added to the session; off, the line is refused with a sentence.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The seconds after which a command typed after ! is stopped.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
