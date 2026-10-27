using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Hosting;

/// <summary>
/// Runs a crew definition - a <c>crew.ork.ts</c> that hands its crew off through
/// <c>globalThis.crew</c>, a <c>crew.yaml</c> or a multi-file crew directory - inside the calling
/// process: the in-process form of <c>orkeon run</c> (#229).
/// </summary>
public interface ICrewScriptRunner
{
    /// <summary>
    /// Loads the crew at <paramref name="crewPath"/> and runs it once, on a runner host of its own:
    /// the settings, mounts, built-in tools, permission gate and LLM provider <c>orkeon run</c>
    /// composes, built for this run and disposed when it ends.
    /// </summary>
    /// <param name="crewPath">The crew target on disk: a <c>.ork.ts</c>, a <c>.yaml</c> or a crew directory.</param>
    /// <param name="input">The crew's input - its variables and initial context; empty when null.</param>
    /// <param name="cancellationToken">Stops the run; the crew is cancelled, not abandoned.</param>
    /// <returns>
    /// The crew's output. A crew that ran and failed is an output whose
    /// <see cref="CrewOutput.Succeeded"/> is false, never an exception.
    /// </returns>
    /// <exception cref="CrewScriptRunException">
    /// The run could not start: a missing target, a setting or a mount the host refuses.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CrewOutput> RunAsync(string crewPath, CrewInput? input = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// What every run of a <see cref="CrewScriptRunner"/> is built over: the <c>orkeon run</c>
/// options an embedding application sets in code rather than on a command line.
/// </summary>
public sealed class CrewScriptRunnerOptions
{
    /// <summary>
    /// The settings file (<c>--settings</c>). Null resolves it as <c>orkeon run</c> does: an
    /// <c>appsettings.json</c> next to the crew, then the folders above it.
    /// </summary>
    public string? SettingsPath { get; set; }

    /// <summary>Extra VFS mounts, in the <c>--mount</c> syntax (<c>&lt;dir&gt;:/output:rw</c>).</summary>
    public IList<string> Mounts { get; } = [];

    /// <summary>Mounts of the settings selected by id (<c>--mount-id</c>).</summary>
    public IList<string> MountIds { get; } = [];

    /// <summary>Lets a mount reach a folder outside the working directory (<c>--allow-external-mounts</c>).</summary>
    public bool AllowExternalMounts { get; set; }

    /// <summary>The host LLM profile the run takes as its default (<c>--llm-profile</c>).</summary>
    public string? LlmProfile { get; set; }

    /// <summary>
    /// Services added to every run's host after Orkeon's own, so a registration here replaces
    /// one of them: an <c>ILlmProviderFactory</c>, a human-input provider, an extra tool.
    /// </summary>
    public Action<HostBuilderContext, IServiceCollection>? ConfigureServices { get; set; }

    /// <summary>
    /// The logging of every run's host. Null clears the providers: an embedded run writes no log
    /// line of its own unless the application asks for one here.
    /// </summary>
    public Action<HostBuilderContext, ILoggingBuilder>? ConfigureLogging { get; set; }

    /// <summary>
    /// Where the run's operator lines go - the settings file used, an LLM fallback warning, a
    /// refusal. Null discards them; a refusal still reaches the caller as the
    /// <see cref="CrewScriptRunException"/> message.
    /// </summary>
    public TextWriter? Diagnostics { get; set; }
}

/// <summary>
/// The run could not start: the target, a setting or a mount was refused before the crew ran.
/// The message is the line <c>orkeon run</c> would have written before exiting 1 or 2.
/// </summary>
public sealed class CrewScriptRunException : Exception
{
    /// <summary>Creates the exception with an empty message.</summary>
    public CrewScriptRunException()
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    /// <param name="message">Why the run could not start.</param>
    public CrewScriptRunException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/> and its cause.</summary>
    /// <param name="message">Why the run could not start.</param>
    /// <param name="innerException">The failure that stopped it.</param>
    public CrewScriptRunException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// <see cref="ICrewScriptRunner"/> over the shared runner bootstrap (<see cref="RunnerExecution"/>):
/// the guards, the mounts and the host of <c>orkeon run</c>, without its console or its exit codes.
/// </summary>
public sealed class CrewScriptRunner : ICrewScriptRunner
{
    private const string LoggerCategory = "Orkeon.Hosting.CrewScriptRunner";

    private readonly CrewScriptRunnerOptions _options;

    /// <summary>Creates a runner whose runs are built over <paramref name="options"/>.</summary>
    /// <param name="options">The settings, mounts and services of every run; defaults when null.</param>
    public CrewScriptRunner(CrewScriptRunnerOptions? options = null) => _options = options ?? new CrewScriptRunnerOptions();

    /// <inheritdoc />
    public async Task<CrewOutput> RunAsync(string crewPath, CrewInput? input = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crewPath);
        cancellationToken.ThrowIfCancellationRequested();

        using var errors = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var console = new EmbeddedRunnerConsole(_options.Diagnostics ?? TextWriter.Null, errors);
        var opts = new EmbeddedRunnerOptions
        {
            ConfigPath = crewPath,
            SettingsPath = _options.SettingsPath,
            Mounts = [.. _options.Mounts],
            MountIds = [.. _options.MountIds],
            AllowExternalMounts = _options.AllowExternalMounts,
            LlmProfile = _options.LlmProfile,
            RunnerConsole = console,
        };

        if (!RunnerExecution.TryBuildHost(
                opts, LoggerCategory, _options.ConfigureServices, out var bootstrap, out _,
                _options.ConfigureLogging ?? ((_, logging) => logging.ClearProviders())))
        {
            throw new CrewScriptRunException(LastErrorLine(errors.ToString()));
        }

        using var host = bootstrap!.Host;
        // The sequence of orkeon run's kickoff: the MCP tools join the registry before the crew
        // resolves its own, and the load and the kickoff share one scope (GAP-25).
        await McpStartup.ConnectConfiguredServersAsync(host, cancellationToken).ConfigureAwait(false);
        var scope = host.Services.CreateAsyncScope();
        await using var scopeLease = scope.ConfigureAwait(false);
        var factory = scope.ServiceProvider.GetRequiredService<ICrewFactory>();

        Domain.Crew.Crew crew;
        try
        {
            crew = await RunnerExecution.LoadCrewAsync(
                host, factory, scope.ServiceProvider, bootstrap.VirtualConfigPath, bootstrap.Logger,
                cancellationToken, bootstrap.IsCrewDirectory).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CrewScriptRunException(ex.Message, ex);
        }

        var orchestrator = scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>();
        return await orchestrator.KickoffAsync(crew.Id, input ?? CrewInput.Empty(), cancellationToken).ConfigureAwait(false);
    }

    private static string LastErrorLine(string errors)
    {
        var lines = errors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var refusal = lines.LastOrDefault(l => l.StartsWith("ERROR: ", StringComparison.Ordinal));
        return refusal is not null
            ? refusal["ERROR: ".Length..]
            : lines.LastOrDefault() ?? "The crew run could not start.";
    }

    /// <summary>The <c>orkeon run</c> options the runner fills in code.</summary>
    private sealed class EmbeddedRunnerOptions : RunnerOptionsBase;

    /// <summary>
    /// The console of an embedded run: no terminal. The product and the diagnostics go to the
    /// caller's writer, the diagnostics are also kept to explain a refusal, no input is read and
    /// the interrupt key is the application's.
    /// </summary>
    private sealed class EmbeddedRunnerConsole(TextWriter diagnostics, TextWriter errors) : IRunnerConsole
    {
        public TextWriter Output { get; } = diagnostics;

        public TextWriter ErrorOutput { get; } = new TeeWriter(diagnostics, errors);

        public TextReader Input => TextReader.Null;

        public bool IsInputRedirected => true;

        public IDisposable OnInterrupt(Func<bool> handler) => NoRegistration.Instance;
    }

    private sealed class NoRegistration : IDisposable
    {
        public static NoRegistration Instance { get; } = new();

        public void Dispose()
        {
            // Nothing was registered: the embedding application owns the interrupt key.
        }
    }

    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        public override System.Text.Encoding Encoding => first.Encoding;

        public override void Write(char value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void Write(string? value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void WriteLine(string? value)
        {
            first.WriteLine(value);
            second.WriteLine(value);
        }
    }
}

/// <summary>DI registration of the in-process crew runner.</summary>
public static class CrewScriptRunnerServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICrewScriptRunner"/>: a .NET application resolves it and calls
    /// <see cref="ICrewScriptRunner.RunAsync"/> to run a <c>crew.ork.ts</c> without starting a
    /// process. Each run builds its own runner host - Orkeon's services stay out of the
    /// application's container.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">Sets the options every run is built over.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddOrkeonScripting(
        this IServiceCollection services, Action<CrewScriptRunnerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new CrewScriptRunnerOptions();
        configure?.Invoke(options);
        services.TryAddSingleton<ICrewScriptRunner>(new CrewScriptRunner(options));
        return services;
    }
}
