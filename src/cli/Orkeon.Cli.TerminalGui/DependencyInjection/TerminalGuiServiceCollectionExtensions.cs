using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Logging;
using Orkeon.Cli.TerminalGui.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Logging;

namespace Microsoft.Extensions.DependencyInjection;

public static class TerminalGuiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Terminal.Gui split-pane console:
    /// replaces <see cref="IConsoleAdapter"/> with the Terminal.Gui adapter,
    /// adds the Terminal.Gui logger provider to the logger factory,
    /// and registers <see cref="TerminalGuiHost"/> as singleton.
    /// Call this BEFORE registering runners so the adapter resolves correctly.
    /// </summary>
    /// <remarks>
    /// Uses a factory pattern so <see cref="TerminalGuiHost.Initialize"/> (which calls
    /// <c>Application.Init</c>) runs BEFORE any view is constructed. Registering
    /// <c>SplitPaneToplevel</c>/<c>LogsPaneView</c>/<c>ReplPaneView</c> directly as singletons
    /// would let the container resolve them before <c>Application.Init</c>, crashing the driver.
    /// </remarks>
    public static IServiceCollection AddOrkeonCliTerminalGui(
        this IServiceCollection services,
        Action<TerminalGuiOptions>? configure = null)
    {
        var options = new TerminalGuiOptions();
        configure?.Invoke(options);
        return services.AddOrkeonCliTerminalGui(options);
    }

    /// <summary>
    /// Overload taking a prebuilt <see cref="TerminalGuiOptions"/>. Preferred over the
    /// <see cref="Action{T}"/> overload because <c>TerminalGuiOptions</c> uses <c>init</c>-only
    /// properties, which cannot be mutated from inside a configure callback — build the record
    /// with an object initializer and pass it here instead.
    /// </summary>
    public static IServiceCollection AddOrkeonCliTerminalGui(
        this IServiceCollection services,
        TerminalGuiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Idempotency guard: detect a prior registration and exit early.
        if (services.Any(d => d.ServiceType == typeof(TerminalGuiHost)))
            return services;

        services.AddSingleton(options);
        services.AddSingleton<TuiIntegration>();
        services.AddSingleton<TerminalGuiHost>(sp => new TerminalGuiHost(
            sp.GetRequiredService<TerminalGuiOptions>(),
            sp.GetRequiredService<TuiIntegration>()));

        services.RemoveAll<IConsoleAdapter>();
        services.AddSingleton<IConsoleAdapter>(sp =>
        {
            var host = sp.GetRequiredService<TerminalGuiHost>();
            host.Initialize();
            // Force-resolve the logger provider here so its factory runs and publishes
            // AmbientLoggerProvider BEFORE any inner host (e.g. RunOneShotAsync) is built.
            // Otherwise the provider factory is lazy (only triggered when something asks
            // for an ILogger) and the inner host built by VerifyCommand sees a null ambient.
            sp.GetRequiredService<TerminalGuiLoggerProvider>();
            var repl = host.Toplevel.Repl;
            // Wire Tab-completion (/command + @path) when the host registered a source.
            var assist = sp.GetService<IReplInputAssist>();
            if (assist is not null)
                repl.ConfigureCompletion(assist);
            return new TerminalGuiConsoleAdapter(repl);
        });

        services.AddSingleton<TerminalGuiLoggerProvider>(sp =>
        {
            var host = sp.GetRequiredService<TerminalGuiHost>();
            host.Initialize();
            var provider = new TerminalGuiLoggerProvider(
                host.Toplevel.Logs,
                sp.GetRequiredService<TerminalGuiOptions>());
            // Install the global key bindings now that both Host and Provider exist
            // (the fidelity layout replaced the StatusBar widget with HintBarView +
            // global handlers — PLAN phase 5). Same cycle-breaking spot as before.
            var findDialog = new FindDialog(host.Toplevel.Logs);
            host.InstallKeyBindings(provider, findDialog);
            // Publish process-wide so inner Hosts (e.g. RunOneShotAsync's child host)
            // can re-route their AddSimpleConsole writes here instead of polluting stdout.
            // See Orkeon.Cli.Abstractions.Logging.AmbientLoggerProvider for the contract.
            AmbientLoggerProvider.Set(provider);
            return provider;
        });
        // Defense in depth: any ILoggerProvider added BEFORE us writes to stdout
        // (commandeered by Terminal.Gui's alt-screen) and corrupts the rendering.
        // Strip them all and re-register only ours. Callers should also call
        // builder.ClearStdoutLoggersForTerminalGui() in ConfigureLogging if they
        // want to be future-proof against late additions, but this guarantees
        // the state at the moment of registration.
        services.RemoveAll<ILoggerProvider>();
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<TerminalGuiLoggerProvider>());

        return services;
    }

    /// <summary>
    /// Sister extension for <see cref="ILoggingBuilder"/>: clears every existing
    /// <see cref="ILoggerProvider"/> registration so console-style providers don't
    /// write to the same stdout that Terminal.Gui has commandeered for the alt-screen
    /// buffer. Call this in your runner's <c>ConfigureLogging</c> WHEN you also call
    /// <see cref="AddOrkeonCliTerminalGui(IServiceCollection, Action{TerminalGuiOptions})"/>. The Terminal.Gui logger is registered
    /// separately via the service-collection extension above (after this clear runs).
    /// </summary>
    /// <remarks>
    /// We can't filter by provider type because <c>Host.CreateDefaultBuilder</c>
    /// registers Console/Debug/EventSource providers via <c>TryAddEnumerable</c> with a
    /// mix of typed and factory descriptors — filtering by <c>ImplementationType</c>
    /// misses the factory ones. <c>ILoggingBuilder.ClearProviders()</c>
    /// removes all <see cref="ILoggerProvider"/> registrations regardless of how they
    /// were added, which is exactly what we want here.
    /// </remarks>
    public static ILoggingBuilder ClearStdoutLoggersForTerminalGui(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Equivalent to LoggingBuilderExtensions.ClearProviders, but avoids pulling in
        // the Microsoft.Extensions.Logging package (we only depend on Abstractions).
        builder.Services.RemoveAll<ILoggerProvider>();
        return builder;
    }
}
