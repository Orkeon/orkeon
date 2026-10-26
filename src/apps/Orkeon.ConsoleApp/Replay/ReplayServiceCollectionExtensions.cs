using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Registry;

namespace Orkeon.ConsoleApp.Replay;

/// <summary>The REPL's replay: its console decorator, its outcome hook and its <c>/replay</c> command.</summary>
internal static class ReplayServiceCollectionExtensions
{
    private const string InnerConsoleKey = "orkeon.repl.replay-inner-console";

    /// <summary>
    /// Decorates the console registered last — the plain, line-editing or Terminal.Gui adapter — with the
    /// <see cref="ReplayConsoleAdapter"/>, so the read loop, the approval channel and <c>human_input</c>, which
    /// all resolve <see cref="IConsoleAdapter"/>, read the replayed lines; registers it as the runners'
    /// <see cref="ICommandOutcomeObserver"/>; and adds <c>replay</c> to the universal commands. Call after the
    /// console choice.
    /// </summary>
    /// <param name="services">The REPL's services.</param>
    /// <param name="echoPrefix">Written before an echoed line: empty after a plain prompt, <c>"&gt; "</c> in the TUI.</param>
    public static IServiceCollection AddReplReplay(this IServiceCollection services, string echoPrefix)
    {
        ArgumentNullException.ThrowIfNull(services);

        var index = services.ToList().FindLastIndex(static d => d.ServiceType == typeof(IConsoleAdapter) && !d.IsKeyedService);
        if (index < 0)
            throw new InvalidOperationException("The replay decorates the REPL's console: register an IConsoleAdapter first.");

        var existing = services[index];
        services.RemoveAt(index);
        services.AddKeyedSingleton<IConsoleAdapter>(InnerConsoleKey, (sp, _) =>
            existing.ImplementationFactory?.Invoke(sp) as IConsoleAdapter
            ?? existing.ImplementationInstance as IConsoleAdapter
            ?? (IConsoleAdapter)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType!));

        services.AddSingleton(sp => new ReplayIdleGate(
            () => sp.GetService<CommandDispatchService>()?.Registry,
            () => sp.GetService<IToolApprovalWatch>(),
            // A human_input question raises no signal: the interceptor's prompt says one waits.
            () => sp.GetService<IReplLineInterceptor>()?.PromptOverride is not null));
        services.AddSingleton(sp => new ReplayConsoleAdapter(
            sp.GetRequiredKeyedService<IConsoleAdapter>(InnerConsoleKey),
            sp.GetRequiredService<ReplayIdleGate>(),
            echoPrefix));
        services.AddSingleton<IConsoleAdapter>(sp => sp.GetRequiredService<ReplayConsoleAdapter>());
        services.AddSingleton<ICommandOutcomeObserver>(sp => sp.GetRequiredService<ReplayConsoleAdapter>());

        services.AddSingleton<ReplayCommand>();
        services.AddSingleton(sp => new DefaultCommandRegistry(
            sp.GetRequiredService<HelpCommand>(),
            sp.GetRequiredService<ExitCommand>(),
            sp.GetRequiredService<ClearCommand>(),
            [sp.GetRequiredService<ReplayCommand>()]));
        return services;
    }
}
