using Orkeon.Constants.Configuration;
using Orkeon.Constants.FileSystem;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Orkeon.Application.Configuration.SettingsDeclarationExtensions;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.EventHub.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Telemetry;
using Orkeon.Infrastructure.Logging;
using Orkeon.Infrastructure.MCP;
using Orkeon.Scripting.Runtime;
using Orkeon.Analysis.DependencyInjection;
using Orkeon.Compliance.Vfs;
using Orkeon.Tools.Abstractions.DependencyInjection;
using Orkeon.Tools.Analysis.DependencyInjection;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;
using Orkeon.Tools.Code.DependencyInjection;
using Orkeon.Tools.Data.DependencyInjection;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.DependencyInjection;
using Orkeon.Tools.EventHub.DependencyInjection;
using Orkeon.Tools.FileSystem.DependencyInjection;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Tools.Rag.DependencyInjection;
using Orkeon.Tools.Web.DependencyInjection;

namespace Orkeon.Hosting;

/// <summary>
/// The VFS surface a runner host is built over. One object rather than four loose
/// parameters on <see cref="RunnerHost.Build"/>: the four decide the same thing together —
/// which physical directories the run can reach, under which virtual names, and with which
/// rights — and a caller that sets one almost always has an opinion on its neighbours.
/// </summary>
public sealed record RunnerMountPlan
{
    /// <summary>The agent-facing mounts, typically the CLI's <c>--mount</c> arguments.</summary>
    public IReadOnlyList<string> CliMounts { get; init; } = [];

    /// <summary>
    /// Mounts registered with <c>MountVisibility.Internal</c>: resolvable by the VFS, absent
    /// from <c>GetAvailableMounts()</c> and therefore invisible to agents. Same grammar as
    /// <see cref="CliMounts"/>.
    /// </summary>
    public IReadOnlyList<string> InternalMounts { get; init; } = [];

    /// <summary>
    /// When <see langword="true"/>, mount base paths are added to the PathSecurity whitelist
    /// (<c>AdditionalAllowedDirectories</c>), allowing mounts from directories outside the
    /// workspace root.
    /// </summary>
    public bool AllowExternalMounts { get; init; }

    /// <summary>
    /// The settings entries this run selects by id — the CLI's <c>--mount-id</c> values, already
    /// parsed (VFS-90, D-04). Needed only when several entries of <c>Orkeon:FileSystem:Mounts</c>
    /// declare one virtual root; the other entries of that root are withdrawn for the run.
    /// </summary>
    public IReadOnlyList<MountId> SelectedMountIds { get; init; } = [];

    /// <summary>
    /// The mounts the crew names in its definition (<c>mounts:</c>, VFS-90 D-02): each a virtual
    /// root, optionally pinned to one settings entry by its id. A reference selects and
    /// validates; it never restricts what else is mounted (D-05).
    /// </summary>
    public IReadOnlyList<MountReference> CrewMountReferences { get; init; } = [];

    /// <summary>
    /// When non-null, enables LLM exchange logging under this <b>virtual</b> path — the caller
    /// is responsible for having mounted it (the runners pass
    /// <see cref="RunnerVirtualRoots.LlmLogs"/> in <see cref="InternalMounts"/>). All HTTP
    /// request/response headers and payloads are captured as JSON Lines (.jsonl) files,
    /// written through <c>IFileSystemService</c> like every other file the framework touches.
    /// </summary>
    public string? LlmLogVirtualPath { get; init; }
}

/// <summary>
/// Shared host builder for Orkeon runners and CLIs.
/// Configures LLM, Orkeon services, tools, file system, and tool registry.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: resolves user-supplied settings paths and provisions VFS mounts before the DI container (and thus IFileSystemService) exists.")]
public static partial class RunnerHost
{
    /// <summary>
    /// WIN-01: the actionable warning emitted once per host build when no <c>Llm</c>
    /// section is configured and the runtime will fall back to the echo provider. Public
    /// because Orkeon Studio reproduces this wording in its own pre-save validation and pins
    /// the two texts together — a UI that warns differently from the runtime is worse than
    /// one that does not warn at all.
    /// </summary>
    public const string LlmNotConfiguredMessage = OperatorMessages.LlmNotConfigured;

    /// <summary>
    /// Builds a fully-configured host with all Orkeon services.
    /// </summary>
    /// <param name="settingsPath">Resolved appsettings.json path (nullable).</param>
    /// <param name="mounts">The VFS surface the host is built over — see <see cref="RunnerMountPlan"/>.</param>
    /// <param name="configureLogging">Optional callback to customize logging (default: a single-line console at Warning level; --verbose raises it).</param>
    /// <param name="configureServices">Optional callback to register additional services.</param>
    /// <param name="configureBuilder">
    /// Optional callback on the builder itself, before it is built. The service host uses it
    /// for <c>UseSystemd()</c> and <c>UseWindowsService()</c>: a long-lived daemon needs to
    /// tell its supervisor it is up, and that is decided on the builder, not in the services.
    /// </param>
    /// <param name="llmProfile">
    /// The host LLM profile the run takes as its default (<c>--llm-profile</c>, STUDIO-50): the
    /// <c>Llm</c> section becomes <c>Llm:Profiles:&lt;id&gt;</c>, whole — a field the profile leaves
    /// unset is unset, the default's key and the variable holding it included —, laid over the
    /// settings file and the <c>ORKEON_</c> environment. Null or <c>default</c> keeps the section.
    /// </param>
    /// <exception cref="RunnerSettingsException">
    /// A setting the host refuses (GAP-35, GAP-40): a value the configuration binder cannot convert
    /// or a rule of its section refuses, a key no section carries — a key that is no setting any
    /// more among them —, a section or a component's name the host does not know, a log level that
    /// is none, an LLM profile it cannot build or does not offer (<paramref name="llmProfile"/>
    /// included — the message lists those the configuration defines), a settings file it cannot
    /// read, an address that is no address, mounts that cannot be honoured. Every setting the host
    /// reads is judged, whether the run uses it or not (<see cref="ValidateSettings(IServiceProvider)"/>);
    /// the first refusal is raised, naming the key, or the file and the place in it. A host already
    /// built is disposed first. Anything else the callbacks raise keeps its own type and its stack.
    /// </exception>
    public static IHost Build(
        string? settingsPath,
        RunnerMountPlan mounts,
        Action<HostBuilderContext, ILoggingBuilder>? configureLogging = null,
        Action<HostBuilderContext, IServiceCollection>? configureServices = null,
        Action<IHostBuilder>? configureBuilder = null,
        string? llmProfile = null)
    {
        ArgumentNullException.ThrowIfNull(mounts);

        // Collected while the configuration is composed, logged once the host exists: no
        // logger is available inside ConfigureAppConfiguration, and the replacement of a
        // settings entry by a --mount — or its selection by id, or its withdrawal — is exactly
        // the kind of decision an operator reading the log must be able to see (STUDIO-15 D-01,
        // VFS-90 D-10).
        var decisions = new MountDecisions();
        var builder = CreateBuilder(settingsPath, mounts, configureLogging, configureServices, llmProfile, decisions);

        configureBuilder?.Invoke(builder);

        var host = builder.Build();
        try
        {
            // First, before a warning is printed or the telemetry starts (GAP-40): a host refused
            // on its settings says nothing else.
            if (ValidateSettings(host.Services) is [var refusal, ..])
                throw new RunnerSettingsException(refusal);

            LogMountDecisions(host, decisions);
            WarnIfEmailTokensUnavailable(host, decisions);
            WarnIfLlmNotConfigured(host, decisions.ElectedLlmProfile);
            ActivateTelemetry(host);
            return host;
        }
        catch
        {
            // The caller never receives the host to dispose: disposing it here flushes its
            // console logger, so the refusal the caller writes next is the last line.
            host.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The builder of a runner host: the configuration every runner composes, then the runner's
    /// services, then the caller's. One composition for <see cref="Build"/> and for
    /// <see cref="ValidateSettings(string?, Action{HostBuilderContext, IServiceCollection}?)"/>, so
    /// <c>orkeon doctor</c> judges the host a run builds, not a copy of it.
    /// </summary>
    private static IHostBuilder CreateBuilder(
        string? settingsPath,
        RunnerMountPlan mounts,
        Action<HostBuilderContext, ILoggingBuilder>? configureLogging,
        Action<HostBuilderContext, IServiceCollection>? configureServices,
        string? llmProfile,
        MountDecisions decisions) =>
        Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, b) => ConfigureAppConfiguration(b, settingsPath, mounts, llmProfile, decisions))
            .ConfigureServices((context, services) =>
                ConfigureRunnerServices(context, services, mounts.LlmLogVirtualPath, configureLogging, configureServices));

    /// <summary>
    /// The start validation of a built host (GAP-40), shared by <see cref="Build"/>, the REPL and
    /// <c>orkeon doctor</c>: every refusal, in order, each one sentence naming its key — the values
    /// of every declared section (the binder, its rules, the names they hold), the section names
    /// under <c>Orkeon:</c> and the groups, the keys of every section the host reads, and
    /// <c>Orkeon:Rag:LlmProfile</c> against the profiles the host offers (GAP-19). Empty when the
    /// host may start. Reads options and builds the named factories only: no store, provider, model
    /// or connection is created.
    /// </summary>
    /// <param name="services">The built container.</param>
    internal static IReadOnlyList<string> ValidateSettings(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var refusals = SettingsValidation.Refusals(services).ToList();
        try
        {
            RagLlm.EnsureProfileIsKnown(
                services.GetRequiredService<IConfiguration>(),
                services.GetService<ILlmProfileRegistry>());
        }
        catch (InvalidOperationException ex)
        {
            refusals.Add(ex.Message);
        }

        return refusals;
    }

    /// <summary>
    /// What <c>orkeon doctor</c> asks (GAP-40): the start validation of the host <c>orkeon run</c>
    /// builds on <paramref name="settingsPath"/> with <paramref name="configureServices"/> — no crew,
    /// no command-line mount, no MCP connection —, every refusal at once. A refusal the build itself
    /// raises (a key retired, a file it cannot read, a log level) stops it and is the only one, as a
    /// run reports it. The host is disposed: nothing is printed, no telemetry starts.
    /// </summary>
    /// <param name="settingsPath">The settings file a run would read, or null.</param>
    /// <param name="configureServices">What the runner adds to the host, as for a run.</param>
    internal static IReadOnlyList<string> ValidateSettings(
        string? settingsPath,
        Action<HostBuilderContext, IServiceCollection>? configureServices)
    {
        IHost host;
        try
        {
            host = CreateBuilder(
                    settingsPath,
                    new RunnerMountPlan(),
                    (_, logging) => logging.ClearProviders(),
                    configureServices,
                    llmProfile: null,
                    new MountDecisions())
                .Build();
        }
        catch (RunnerSettingsException ex)
        {
            return [ex.Message];
        }

        using (host)
            return ValidateSettings(host.Services);
    }

    /// <summary>A setting a registration or a check refused, as the one type every entry point translates (GAP-35).</summary>
    private static RunnerSettingsException Refused(InvalidOperationException ex) => new(ex.Message, ex);

    /// <summary>
    /// One <c>Information</c> line per decision taken on the settings' mounts: an entry a
    /// <c>--mount</c> took the place of, an entry selected by id, an entry withdrawn for the
    /// run. The run's own intent won over the machine's default, and the log says so by root
    /// name and id — the same words for every runner, the daemon included.
    /// </summary>
    private static void LogMountDecisions(IHost host, MountDecisions decisions)
    {
        var plan = decisions.Plan;
        if (decisions.ReplacedRoots.Count == 0 && plan.Selected.Count == 0 && plan.Withdrawn.Count == 0 && plan.Warnings.Count == 0)
            return;

        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Orkeon.Hosting.RunnerHost");
        if (!logger.IsEnabled(LogLevel.Information))
            return;

        foreach (var root in decisions.ReplacedRoots)
            LogMountReplacesSettingsEntry(logger, root);
        foreach (var entry in plan.Selected)
        {
            var selector = Describe(entry.Selector);
            LogMountSelected(logger, entry.VirtualRoot, entry.Id, selector);
            if (entry.OverridesCrewChoice is { } crewChoice)
                LogMountOptionOverridesCrew(logger, entry.VirtualRoot, entry.Id, crewChoice);
        }

        foreach (var entry in plan.Withdrawn)
        {
            var id = entry.Id?.ToString() ?? "(no id)";
            LogMountWithdrawn(logger, entry.VirtualRoot, id);
        }

        foreach (var warning in plan.Warnings)
            LogMountSelectionWarning(logger, warning);
    }

    private static string Describe(MountSelector selector) => selector switch
    {
        MountSelector.MountIdOption => "--mount-id",
        MountSelector.CrewMounts => "the crew's mounts:",
        _ => selector.ToString(),
    };

    /// <summary>What <see cref="ConfigureAppConfiguration"/> decided, for the log once a logger exists.</summary>
    private sealed class MountDecisions
    {
        public List<string> ReplacedRoots { get; } = [];

        public MountSelectionPlan Plan { get; set; } = MountSelectionPlan.Empty;

        /// <summary>Why the OAuth accounts of the settings have no token store in this run, when they have none.</summary>
        public string? EmailTokensUnavailable { get; set; }

        /// <summary>The profile <c>--llm-profile</c> elected as the run's default, by its key in the settings; null when none.</summary>
        public string? ElectedLlmProfile { get; set; }
    }

    /// <summary>
    /// An OAuth e-mail account is declared, but the runner could not provide its token store:
    /// said once, on the log and on stderr, because the run goes on — a crew that sends no mail
    /// must not fail for it — and each e-mail call of such an account will then refuse.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static void WarnIfEmailTokensUnavailable(IHost host, MountDecisions decisions)
    {
        if (decisions.EmailTokensUnavailable is not { } reason)
            return;

        Console.Error.WriteLine("WARNING: " + reason);
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Orkeon.Hosting.RunnerHost");
        LogEmailTokensUnavailable(logger, reason);
    }

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "{Reason}")]
    private static partial void LogEmailTokensUnavailable(ILogger logger, string reason);

    /// <summary>
    /// OpenTelemetry creates its tracer and meter providers in a hosted service, and the
    /// runners never start the host -- they resolve services and run one command. Resolving
    /// the two providers here is what creates them: the ActivitySource listeners come alive,
    /// the OTLP exporters (from the environment or the settings) attach, and the container
    /// disposes them with the host, which flushes the last batch.
    /// </summary>
    private static void ActivateTelemetry(IHost host)
    {
        _ = host.Services.GetService<OpenTelemetry.Trace.TracerProvider>();
        _ = host.Services.GetService<OpenTelemetry.Metrics.MeterProvider>();
    }

    /// <summary>
    /// WIN-01: when <see cref="RegisterLlmProvider"/> found no <c>Llm</c> section, the
    /// runtime silently degrades to the <c>&lt;undefined-llm&gt;</c> echo provider — the
    /// number one onboarding trap on a fresh install. Emit one actionable warning per host
    /// build: on the host logger AND on stderr (the host may reconfigure the logger below
    /// Warning, so the stderr line is the guarantee). Runs after <c>Build()</c> because no
    /// logger exists yet at service-registration time. The same pass says where each section's
    /// API key came from, and warns — logger and stderr, once per section — about an
    /// <c>ApiKeyEnvVar</c> that names a variable set nowhere (STUDIO-49). A run that elected a
    /// profile (<c>--llm-profile</c>, STUDIO-50) says which, and tells the default's key by that
    /// profile's path: the section is the profile, warned about once.
    /// <para>
    /// The profiles are those the host offers crews (GAP-36): <see cref="ILlmProfileRegistry.Names"/>,
    /// a host's allow-list applied — <c>orkeon-host</c>'s <c>Orkeon:Host:LlmProfiles</c> —, the file's
    /// when no registry is registered. A profile the list hides is named on a line of its own, so the
    /// operator sees the list at work, and never warned about: no crew can name it. Without a list,
    /// every profile of the file is offered.
    /// </para>
    /// </summary>
    private static void WarnIfLlmNotConfigured(IHost host, string? electedProfile)
    {
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Orkeon.Hosting.RunnerHost");

        var llmSection = configuration.GetSection(ConfigurationKeys.LlmSection);
        var profilesSection = llmSection.GetSection(ConfigurationKeys.LlmProfiles);
        var defined = LlmSettings.ProfileNames(configuration);
        var offered = OfferedProfiles(host, defined);
        var hidden = defined.Where(name => !offered.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (offered.Count > 0)
            LogLlmProfiles(logger, offered);
        if (hidden.Count > 0)
            LogLlmProfilesHidden(logger, hidden);

        // STUDIO-50: the Llm section IS the elected profile for this run, so its key is told by
        // the profile's own path — the one the settings were written with — and a reference that
        // resolves nothing is warned about once, there.
        var keySection = electedProfile is null
            ? llmSection
            : profilesSection.GetSection(electedProfile);
        if (electedProfile is not null)
            LogLlmProfileElected(logger, electedProfile);
        if (logger.IsEnabled(LogLevel.Information))
        {
            // The file's profiles the host offers: one a C# host registers in code has no section
            // to tell its key by.
            foreach (var profile in offered.Where(name => defined.Contains(name, StringComparer.OrdinalIgnoreCase)))
            {
                var source = LlmSettings.DescribeApiKey(profilesSection.GetSection(profile));
                LogLlmProfileKey(logger, profile, source);
            }
        }

        // STUDIO-49: an ApiKeyEnvVar naming a variable set nowhere — once per host build and per
        // section, by its configuration path, never the name it holds (a key pasted in the wrong
        // field must not reach a log). The calls on that profile answer "API key is required".
        // A hidden profile's is no reason to warn, unless the run elected it as its default.
        var defaultReference = $"{ConfigurationKeys.LlmSection}:{ConfigurationKeys.LlmApiKeyEnvVar}";
        var silenced = hidden
            .Where(name => !string.Equals(name, electedProfile, StringComparison.OrdinalIgnoreCase))
            .Select(name => $"{profilesSection.Path}:{name}:{ConfigurationKeys.LlmApiKeyEnvVar}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in LlmSettings.UnresolvedApiKeyReferences(configuration))
        {
            if (electedProfile is not null && string.Equals(reference, defaultReference, StringComparison.OrdinalIgnoreCase))
                continue;
            if (silenced.Contains(reference))
                continue;

            var warning = UnresolvedApiKeyReferenceMessage(reference);
            Console.Error.WriteLine("WARNING: " + warning);
            LogApiKeyReferenceUnresolved(logger, warning);
        }

        if (LlmSettings.HasDefault(configuration))
        {
            // One line of truth about what was actually resolved (file + ORKEON_ overlay):
            // when a run behaves as if a setting never arrived — a timeout still at its
            // default, a temperature the vendor rejects, a key that never came — this line
            // settles where the chain broke. Where the key came from, never the key.
            if (logger.IsEnabled(LogLevel.Information))
            {
                var model = Shown(llmSection["Model"], "(default)");
                var baseUrl = Shown(llmSection["BaseUrl"], "(provider default)");
                var temperature = Shown(llmSection["Temperature"], "(not set: the model's own)");
                var timeout = Shown(llmSection["TimeoutSeconds"], "(default 30)");
                var source = LlmSettings.DescribeApiKey(keySection);
                LogLlmResolved(logger, model, baseUrl, temperature, timeout, source);
            }

            return;
        }

        Console.Error.WriteLine("WARNING: " + LlmNotConfiguredMessage);
        LogLlmNotConfigured(logger);
    }

    /// <summary>
    /// The profiles the host offers crews: the registry's names — a host's allow-list applied —, else
    /// the file's. A registry the container cannot build is a setting the host refuses (GAP-35).
    /// </summary>
    private static IReadOnlyList<string> OfferedProfiles(IHost host, IReadOnlyList<string> defined)
    {
        try
        {
            return host.Services.GetService<ILlmProfileRegistry>()?.Names ?? defined;
        }
        catch (InvalidOperationException ex) when (ex is not RunnerSettingsException)
        {
            throw Refused(ex);
        }
    }

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "LLM profiles offered to crews besides the default: {Profiles}")]
    private static partial void LogLlmProfiles(ILogger logger, IReadOnlyList<string> profiles);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "LLM profiles hidden from crews by the host's allow-list: {Profiles}")]
    private static partial void LogLlmProfilesHidden(ILogger logger, IReadOnlyList<string> profiles);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = LlmNotConfiguredMessage)]
    private static partial void LogLlmNotConfigured(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message =
        "LLM resolved: model={Model} baseUrl={BaseUrl} temperature={Temperature} timeoutSeconds={TimeoutSeconds} apiKey={ApiKey}")]
    private static partial void LogLlmResolved(
        ILogger logger, string model, string baseUrl, string temperature, string timeoutSeconds, string apiKey);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "LLM profile {Profile}: apiKey={ApiKey}")]
    private static partial void LogLlmProfileKey(ILogger logger, string profile, string apiKey);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "{Warning}")]
    private static partial void LogApiKeyReferenceUnresolved(ILogger logger, string warning);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "LLM profile {Profile} elected as the run's default (--llm-profile)")]
    private static partial void LogLlmProfileElected(ILogger logger, string profile);

    /// <summary>What the host says, on its logger and on stderr, of an <c>ApiKeyEnvVar</c> set nowhere — its path, never its value.</summary>
    private static string UnresolvedApiKeyReferenceMessage(string reference) =>
        string.Format(CultureInfo.InvariantCulture, UnresolvedApiKeyReferenceFormat, reference);

    private static readonly CompositeFormat UnresolvedApiKeyReferenceFormat =
        CompositeFormat.Parse(OperatorMessages.LlmApiKeyReferenceUnresolved);

    /// <summary>A configured value for the startup line; blank reads as absent, like <see cref="LlmSettings"/> reads it.</summary>
    private static string Shown(string? value, string absent) => string.IsNullOrWhiteSpace(value) ? absent : value;

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message =
        "mount {VirtualPath}: --mount replaces the settings entry")]
    private static partial void LogMountReplacesSettingsEntry(ILogger logger, string virtualPath);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message =
        "mount {VirtualPath}: settings entry {MountId} selected by {Selector}")]
    private static partial void LogMountSelected(ILogger logger, string virtualPath, MountId mountId, string selector);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message =
        "mount {VirtualPath}: settings entry {MountId} not mounted for this run")]
    private static partial void LogMountWithdrawn(ILogger logger, string virtualPath, string mountId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message =
        "mount {VirtualPath}: --mount-id {MountId} overrides the crew's choice {CrewMountId}")]
    private static partial void LogMountOptionOverridesCrew(ILogger logger, string virtualPath, MountId mountId, MountId crewMountId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "{Warning}")]
    private static partial void LogMountSelectionWarning(ILogger logger, string warning);

    /// <summary>The configuration section <c>--allow-external-mounts</c> extends.</summary>
    private const string PathSecurityWhitelistSection = "PathSecurity:AdditionalAllowedDirectories";

    /// <summary>
    /// Composes the configuration a runner host reads its mounts from: the sources every Orkeon
    /// host reads (<see cref="RunnerSettings.ComposeSources(IConfigurationBuilder, string?)"/> — the
    /// environment without a prefix, the settings file, the <c>ORKEON_</c> environment; never the
    /// default host's files of the current directory, nor its user secrets), then this method's own
    /// in-memory source, which is added last and therefore wins on an identical key.
    /// <para>
    /// A <c>--mount</c> is placed <b>by virtual root</b> (STUDIO-15 D-01). On a root the
    /// declared array — settings file AND environment, read from the builder's own snapshot —
    /// already holds, it is written at that entry's index: the mount of this run replaces the
    /// machine's default for that root, which is the most specific intent there is. On a new
    /// root it is appended after the highest declared index, so nothing the operator declared
    /// is lost. Two earlier shapes of this method each got one of those halves wrong: writing
    /// from index 0 replaced the operator's first entry on every run (the crew mount is always
    /// there), and appending unconditionally produced "Duplicate virtual paths" out of a DI
    /// factory the moment a settings entry and a <c>--mount</c> named the same root — which
    /// is exactly what Studio's team flow produces by design, since a team associates a folder
    /// the settings already declare.
    /// </para>
    /// <para>
    /// <c>InternalMounts</c> keep appending: they ride their own key so they can carry
    /// Internal visibility (the mount-string grammar has no room for it), and configuration
    /// rather than a hosted service because the runners never start the host — an
    /// IHostedService would silently never fire under <c>--validate</c> or <c>--list-tools</c>.
    /// </para>
    /// <para>
    /// A mount the settings declare is the machine owner's explicit intent, so its base path
    /// is always whitelisted for <c>PathValidator</c> (STUDIO-12 C4): until then such a folder
    /// was mounted and every access to it refused as "outside the workspace root", with no
    /// flag able to rescue it — <c>--allow-external-mounts</c> only ever whitelisted the
    /// <c>--mount</c> arguments, and still does exactly that, and only that.
    /// </para>
    /// <para>
    /// Since VFS-90 the declared array may hold several entries of one root, told apart by
    /// their ids. <see cref="MountSelection.Resolve"/> decides here, on the same snapshot,
    /// which of them this run keeps: a <c>--mount</c> on the root replaces them all, else
    /// <see cref="RunnerMountPlan.SelectedMountIds"/>, else
    /// <see cref="RunnerMountPlan.CrewMountReferences"/>. Every other entry of that root is
    /// <b>withdrawn</b>: its key is written to <c>null</c> at its own index (the binder binds
    /// a null element there, and the registration leaves a null or blank entry out) and its
    /// base path is not whitelisted. A selection that cannot be resolved is thrown, with the same text the
    /// runners' guards print — this is the closed net for the hosts built without them.
    /// </para>
    /// </summary>
    /// <param name="builder">The host's configuration builder.</param>
    /// <param name="settingsPath">Resolved appsettings.json path, or null.</param>
    /// <param name="mounts">The VFS surface: agent-facing mounts (the crew mount included),
    /// Internal mounts, the selection inputs, the external-mounts opt-in.</param>
    /// <param name="llmProfile">The profile <c>--llm-profile</c> elects as the run's default, or null.</param>
    /// <param name="decisions">Receives what was decided, for the caller to log once a logger
    /// exists.</param>
    private static void ConfigureAppConfiguration(
        IConfigurationBuilder builder,
        string? settingsPath,
        RunnerMountPlan mounts,
        string? llmProfile,
        MountDecisions decisions)
    {
        RunnerSettings.ComposeSources(builder, settingsPath);

        var overrides = new Dictionary<string, string?>();
        using var declared = DeclaredConfiguration.Snapshot(builder);

        ElectLlmProfile(overrides, declared, llmProfile, decisions);

        var declaredMounts = declared.Entries(ConfigurationKeys.FileSystemMounts);
        var plan = MountSelection.Resolve(
            declaredMounts
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => new DeclaredMountEntry(entry.Index, entry.Value!))
                .ToList(),
            mounts.CliMounts,
            mounts.SelectedMountIds,
            mounts.CrewMountReferences,
            settingsPath);
        if (plan.Errors.Count > 0)
            throw new RunnerSettingsException(string.Join(Environment.NewLine, plan.Errors));

        decisions.Plan = plan;
        var withdrawnIndices = new HashSet<int>(plan.WithdrawnIndices());
        foreach (var index in withdrawnIndices)
            overrides[$"{ConfigurationKeys.FileSystemMounts}:{index}"] = null;

        var replacedIndices = ApplyCliMounts(overrides, declaredMounts, mounts.CliMounts, decisions);

        var declaredInternal = declared.Entries(ConfigurationKeys.FileSystemInternalMounts);
        var nextInternalIndex = NextIndex(declaredInternal);
        foreach (var mount in mounts.InternalMounts)
            overrides[$"{ConfigurationKeys.FileSystemInternalMounts}:{nextInternalIndex++}"] = mount;

        var credentialsDirectory = PlanEmailCredentials(declared, declaredMounts, declaredInternal, mounts.CliMounts, settingsPath, decisions);
        if (credentialsDirectory is not null)
        {
            overrides[$"{ConfigurationKeys.FileSystemInternalMounts}:{nextInternalIndex++}"] =
                $"{FileSystemMount.Quote(credentialsDirectory)}:{RunnerVirtualRoots.Credentials}:rw";
        }

        // The whitelist "additionally" extends what the settings declare (the flag's own
        // documented wording), so every entry continues AFTER the declared ones: starting the
        // count at 0 silently replaced the operator's first allowed directory instead of
        // adding to it.
        var nextWhitelistIndex = NextIndex(declared.Entries(PathSecurityWhitelistSection));
        // A declared entry a --mount replaced, or the selection withdrew, is no longer
        // mounted; whitelisting its base would open a folder nothing reaches, so only the
        // entries still in force count.
        var inForce = declaredMounts
            .Where(entry => !replacedIndices.Contains(entry.Index) && !withdrawnIndices.Contains(entry.Index))
            .Select(entry => entry.Value);
        nextWhitelistIndex = WhitelistBases(overrides, inForce, nextWhitelistIndex);
        nextWhitelistIndex = WhitelistBases(overrides, declaredInternal.Select(entry => entry.Value), nextWhitelistIndex);
        if (credentialsDirectory is not null)
            overrides[$"{PathSecurityWhitelistSection}:{nextWhitelistIndex++}"] = credentialsDirectory;

        // --allow-external-mounts: the --mount arguments (and the runner's own internal
        // mounts) may point outside the working directory. Unchanged: a folder named on the
        // command line is still gated behind the explicit opt-in.
        if (mounts.AllowExternalMounts)
            WhitelistBases(overrides, mounts.CliMounts.Concat(mounts.InternalMounts), nextWhitelistIndex);

        if (overrides.Count > 0)
            builder.AddInMemoryCollection(overrides);
    }

    /// <summary>
    /// <c>--llm-profile</c> (STUDIO-50): the <c>Llm</c> section becomes the profile the run elects —
    /// every key the section declares besides <c>Profiles</c> unset, the profile's own keys written
    /// over them —, in the in-memory source laid last. Every reader of the section then reads the
    /// profile: the default provider, the startup line, the endpoint probe before a kickoff. A field
    /// the profile leaves unset stays unset, so neither the default's key nor the variable holding
    /// it ever reaches the profile's endpoint — the rule a Studio launch on a team's setting follows
    /// (STUDIO-49, decision 4). The profiles stay as declared: the elected one is still offered by its
    /// name. Nothing happens when no snapshot could be read; the build itself reports that file.
    /// </summary>
    /// <exception cref="RunnerSettingsException">The settings and the environment define no such profile.</exception>
    private static void ElectLlmProfile(
        Dictionary<string, string?> overrides,
        DeclaredConfiguration declared,
        string? llmProfile,
        MountDecisions decisions)
    {
        if (llmProfile is null || LlmProfiles.IsDefault(llmProfile) || declared.Section(ConfigurationKeys.LlmSection) is not { } llm)
            return;

        var name = llmProfile.Trim();
        var profiles = llm.GetSection(ConfigurationKeys.LlmProfiles).GetChildren().ToList();
        var profile = profiles.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new RunnerSettingsException(UnknownElectedProfileMessage(name, profiles.Select(p => p.Key)));

        // Case-insensitive like the configuration itself: a default written "model" and a profile
        // written "Model" are one key, and the profile's value must be the one that lands.
        var election = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in llm.AsEnumerable(makePathsRelative: true))
        {
            if (value is not null && !IsProfilesKey(key))
                election[$"{ConfigurationKeys.LlmSection}:{key}"] = null;
        }

        foreach (var (key, value) in profile.AsEnumerable(makePathsRelative: true))
        {
            if (value is not null)
                election[$"{ConfigurationKeys.LlmSection}:{key}"] = value;
        }

        foreach (var (key, value) in election)
            overrides[key] = value;
        decisions.ElectedLlmProfile = profile.Key;

        static bool IsProfilesKey(string key) =>
            string.Equals(key, ConfigurationKeys.LlmProfiles, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(ConfigurationKeys.LlmProfiles + ConfigurationPath.KeyDelimiter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What a run says of an <c>--llm-profile</c> the configuration does not define: the sentence a
    /// crew naming an unknown profile fails its load with, the known profiles listed. One wording
    /// for the runners' guard (<see cref="RunnerExecution.EnsureLlmProfileIsKnown"/>) and for a host
    /// built without it.
    /// </summary>
    internal static string UnknownElectedProfileMessage(string name, IEnumerable<string> known) =>
        LlmProfiles.UnknownMessage(name, "--llm-profile", known);

    /// <summary>
    /// Writes every <c>--mount</c> into the agent-facing section: at the index of the declared
    /// entry of the same root when there is one (the replacement), else at the next free index.
    /// Returns the indices that were replaced.
    /// </summary>
    private static HashSet<int> ApplyCliMounts(
        Dictionary<string, string?> overrides,
        List<(int Index, string? Value)> declaredMounts,
        IReadOnlyList<string> cliMounts,
        MountDecisions decisions)
    {
        var replacedIndices = new HashSet<int>();
        var nextMountIndex = NextIndex(declaredMounts);
        foreach (var mount in cliMounts)
        {
            var root = MountSelection.TryGetVirtualRoot(mount);
            var declaredIndex = root is null ? null : IndexOfRoot(declaredMounts, root);
            if (declaredIndex is { } index)
            {
                overrides[$"{ConfigurationKeys.FileSystemMounts}:{index}"] = mount;
                if (replacedIndices.Add(index))
                    decisions.ReplacedRoots.Add(root!);
            }
            else
            {
                overrides[$"{ConfigurationKeys.FileSystemMounts}:{nextMountIndex++}"] = mount;
            }
        }

        return replacedIndices;
    }

    /// <summary>
    /// Whitelists the base path of every mount string that has one, from
    /// <paramref name="nextIndex"/> on. Returns the next free whitelist index.
    /// </summary>
    private static int WhitelistBases(Dictionary<string, string?> overrides, IEnumerable<string?> mountStrings, int nextIndex)
    {
        foreach (var mount in mountStrings)
        {
            if (TryExtractMountBasePath(mount) is { } basePath)
                overrides[$"{PathSecurityWhitelistSection}:{nextIndex++}"] = basePath;
        }

        return nextIndex;
    }

    /// <summary>
    /// Where the OAuth tokens of the declared e-mail accounts live, creating the directory and
    /// its <c>email</c> subdirectory, or null when no account signs in with OAuth2 — no mount,
    /// then, and nothing created. The directory sits next to the per-user settings unless the
    /// operator names one (<c>Orkeon:Tools:Email:CredentialsDirectory</c>, for a service
    /// account; a relative one is read from the settings file's directory). On Unix the
    /// subdirectory holding the tokens is owner-only, narrowed to it if it already existed wider.
    /// A directory that cannot be prepared is reported in <paramref name="decisions"/>, not
    /// thrown: the run goes on and only the OAuth accounts refuse. A user mount claiming
    /// <see cref="RunnerVirtualRoots.Credentials"/> is refused whatever the accounts: the root
    /// is reserved, like the other internal ones.
    /// </summary>
    private static string? PlanEmailCredentials(
        DeclaredConfiguration declared,
        List<(int Index, string? Value)> declaredMounts,
        List<(int Index, string? Value)> declaredInternal,
        IReadOnlyList<string> cliMounts,
        string? settingsPath,
        MountDecisions decisions)
    {
        var claimed = declaredMounts.Select(entry => entry.Value)
            .Concat(declaredInternal.Select(entry => entry.Value))
            .Concat(cliMounts)
            .Any(mount => mount is { Length: > 0 }
                && string.Equals(MountSelection.TryGetVirtualRoot(mount), RunnerVirtualRoots.Credentials, StringComparison.Ordinal));
        if (claimed)
        {
            throw new RunnerSettingsException(
                $"The virtual root {RunnerVirtualRoots.Credentials} is reserved: the runner keeps the OAuth tokens of e-mail accounts there. Mount the folder under another name.");
        }

        var section = declared.Section(ConfigurationKeys.ToolsEmail);
        if (section is null || !section.Exists() || !EmailCredentialsLocation.NeedsTokenStore(section))
            return null;

        var directory = EmailCredentialsLocation.ConfiguredDirectory(section) ?? DefaultCredentialsDirectory();
        if (directory is null)
        {
            decisions.EmailTokensUnavailable =
                $"No per-user settings directory can be found to keep the OAuth tokens of e-mail accounts: set {ConfigurationKeys.ToolsEmail}:CredentialsDirectory.";
            return null;
        }

        try
        {
            // Relative to the settings file that names it, so a login and a run started from
            // different directories share one token store.
            var settingsDirectory = settingsPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(settingsPath));
            directory = Path.GetFullPath(settingsDirectory is null ? directory : Path.Combine(settingsDirectory, directory));
            var tokens = Path.Combine(directory, EmailCredentialsLocation.TokenSubdirectory);
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(tokens);
            }
            else
            {
                // The mode applies to the one directory each call creates, never to its parents.
                const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                Directory.CreateDirectory(directory, ownerOnly);
                Directory.CreateDirectory(tokens, ownerOnly);
                if ((File.GetUnixFileMode(tokens) & ~ownerOnly) != 0)
                    File.SetUnixFileMode(tokens, ownerOnly);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            decisions.EmailTokensUnavailable =
                $"The directory of the e-mail OAuth tokens, {directory}, cannot be prepared ({ex.Message}): OAuth e-mail accounts have no token store in this run.";
            return null;
        }

        return directory;
    }

    /// <summary>
    /// <c>credentials</c> next to the per-user settings, or null when no per-user directory can be
    /// determined (a bare container without HOME): the run goes on, says why at start, and OAuth
    /// accounts report that no token store is available.
    /// </summary>
    private static string? DefaultCredentialsDirectory()
    {
        try
        {
            var settingsDirectory = Path.GetDirectoryName(RunnerSettings.GetGlobalSettingsPath());
            return settingsDirectory is null ? null : Path.Combine(settingsDirectory, "credentials");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The index a new entry of an indexed section lands on: one past the highest declared
    /// index, not the count. Configuration is a sparse key/value space and an operator may
    /// legitimately have declared 0 and 2.
    /// </summary>
    private static int NextIndex(List<(int Index, string? Value)> entries) =>
        entries.Count == 0 ? 0 : entries.Max(entry => entry.Index) + 1;

    /// <summary>
    /// The lowest declared index whose entry claims <paramref name="root"/>, or null. A root
    /// declared several times (each entry with its id, VFS-90) has its first declaration
    /// replaced by the <c>--mount</c> and the others withdrawn by the selection plan, so the
    /// answer is deterministic and the run ends with one mount under that name.
    /// </summary>
    private static int? IndexOfRoot(List<(int Index, string? Value)> entries, string root)
    {
        foreach (var (index, value) in entries)
        {
            if (value is not null && string.Equals(MountSelection.TryGetVirtualRoot(value), root, StringComparison.Ordinal))
                return index;
        }

        return null;
    }

    /// <summary>
    /// The entries the sources added so far declare — settings file and <c>ORKEON_</c>
    /// environment alike — read from one snapshot of the builder. What this class adds must
    /// be placed against what is really declared, never against the file alone: an
    /// <c>ORKEON_Orkeon__FileSystem__Mounts__0</c> is a declared mount too.
    /// </summary>
    private sealed class DeclaredConfiguration : IDisposable
    {
        private readonly IConfigurationRoot? _snapshot;

        private DeclaredConfiguration(IConfigurationRoot? snapshot) => _snapshot = snapshot;

        public void Dispose() => (_snapshot as IDisposable)?.Dispose();

        /// <summary>
        /// Builds the sources added so far — the first read of the settings, so the place where
        /// a file the configuration cannot read is refused, naming the file and, for JSON it
        /// cannot parse, the line and the position (GAP-35). The snapshot used to swallow it on
        /// the word that "the real Build() a few lines later reports it properly"; that Build()
        /// threw instead, and nothing caught it.
        /// </summary>
        /// <exception cref="RunnerSettingsException">A settings file cannot be read.</exception>
        public static DeclaredConfiguration Snapshot(IConfigurationBuilder builder)
        {
            try
            {
                return new DeclaredConfiguration(builder.Build());
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException or UnauthorizedAccessException)
            {
                throw new RunnerSettingsException(RunnerSettings.DescribeUnreadableSettings(ex), ex);
            }
        }

        /// <summary>The declared section at <paramref name="key"/>, or null when nothing could be read.</summary>
        public IConfigurationSection? Section(string key) => _snapshot?.GetSection(key);

        /// <summary>The numerically-keyed children of <paramref name="section"/>, by ascending index.</summary>
        public List<(int Index, string? Value)> Entries(string section)
        {
            if (_snapshot is null)
                return [];

            var entries = new List<(int Index, string? Value)>();
            foreach (var child in _snapshot.GetSection(section).GetChildren())
            {
                if (int.TryParse(child.Key, CultureInfo.InvariantCulture, out var index))
                    entries.Add((index, child.Value));
            }

            entries.Sort((a, b) => a.Index.CompareTo(b.Index));
            return entries;
        }
    }

    private static void ConfigureRunnerServices(
        HostBuilderContext context,
        IServiceCollection services,
        string? llmLogVirtualPath,
        Action<HostBuilderContext, ILoggingBuilder>? configureLogging,
        Action<HostBuilderContext, IServiceCollection>? configureServices)
    {
        // What this host registers reads the settings, and every refusal it raises — a key that
        // is no setting, a profile it cannot build, a value the binder cannot convert — is the
        // operator's to fix: one type for all of them, which every entry point translates
        // (GAP-35). Before, each left the build as a bare InvalidOperationException, and the
        // runners that caught none crashed on it.
        try
        {
            RegisterRunnerServices(context, services, llmLogVirtualPath, configureLogging);
        }
        catch (InvalidOperationException ex) when (ex is not RunnerSettingsException)
        {
            throw Refused(ex);
        }

        // Runner-specific services: the caller's own code, whose exceptions are its own.
        configureServices?.Invoke(context, services);
    }

    private static void RegisterRunnerServices(
        HostBuilderContext context,
        IServiceCollection services,
        string? llmLogVirtualPath,
        Action<HostBuilderContext, ILoggingBuilder>? configureLogging)
    {
        ConfigureRunnerLogging(context, services, configureLogging);
        ConfigureLlmExchangeLogging(context, services, llmLogVirtualPath);

        // --- LLM provider from appsettings.json "Llm" section ---
        RegisterLlmProvider(context, services);

        // Core Orkeon services
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        // OpenTelemetry export: the `Telemetry` section of the settings, or the standard
        // OTEL_EXPORTER_OTLP_ENDPOINT environment a .NET Aspire AppHost (or any collector)
        // injects. Until 2026-09-11 the runners registered no exporter at all, so a run
        // launched from Aspire produced spans that went nowhere.
        services.AddOrkeonTelemetry(context.Configuration);

        // Runners load crews from user-authored YAML/TS: a tool referenced by a crew but
        // absent from the registry is almost always a typo or a missing registration, not an
        // intentional degrade. Fail loading with an explicit "unknown tool(s): …; available: …"
        // message rather than silently dropping the tool (the library default stays lenient).
        // A host can still opt back out via "Orkeon:CrewFactory:StrictTools": false.
        services.DeclareSettingsShape(CrewFactorySection, typeof(CrewFactorySettingsShape));
        var strictTools = context.Configuration.GetValue(
            $"{CrewFactorySection}:StrictTools", defaultValue: true);
        services.Configure<Orkeon.Infrastructure.Configuration.CrewFactoryOptions>(
            o => o.StrictTools = strictTools);

        // The .ork.ts sandbox limits and the esbuild toolchain, read when a script crew loads and
        // at its first transpilation — bound here and judged at the host's start (GAP-40).
        services.AddOptions<Orkeon.Scripting.Configuration.ScriptingLimitsOptions>()
            .Bind(context.Configuration.GetSection(Orkeon.Scripting.Configuration.ScriptingLimitsOptions.SectionName))
            .DeclareSettings(Orkeon.Scripting.Configuration.ScriptingLimitsOptions.SectionName);
        services.AddOptions<Orkeon.Scripting.Configuration.ScriptingToolchainOptions>()
            .Bind(context.Configuration.GetSection(Orkeon.Scripting.Configuration.ScriptingToolchainOptions.SectionName))
            .DeclareSettings(Orkeon.Scripting.Configuration.ScriptingToolchainOptions.SectionName);

        // Per-tool-call permission gate — config opt-in:
        // Orkeon:Security:PermissionGate:Enabled = true. No-op otherwise.
        services.AddOrkeonPermissionGate(context.Configuration);

        // Register all standard tools
        services.AddOrkeonFileSystemTools();
        services.AddOrkeonDataTools();
        services.AddOrkeonWebTools();
        services.AddOrkeonCodeTools();
        // Orkeon.Tools.Code binds Orkeon:Tools:Shell and validates it at start, but cannot declare
        // it: it does not see Orkeon.Application. Declared here, its keys are judged too (GAP-40).
        DeclareShellSettings(services);
        services.AddOrkeonAbstractionTools();
        // Session primitives (session_store/session_snip/token_budget/
        // memory_store/session_cost/session_stats) — previously REPL-only
        // (ConsoleApp), which made standalone crew runs silently lose the session
        // buffer, auto-compaction and memory. In-memory backing; idempotent.
        services.AddOrkeonSessionTools();

        // EventHub — the in-memory hub plus its seven agent tools (publish_event,
        // post_message, send_request, reply_to, receive_message, wait_for_event,
        // get_last_value). Four+ example crews reference these tools; without the hub
        // singleton the tools can't be constructed, so both must be wired together.
        // Registered under IBaseTool, which is what the default ToolRegistry enumerates.
        services.AddOrkeonInMemoryEventHub();
        services.AddOrkeonEventHubTools();

        // The ACL rides along, with the permissive default: a crew that declares no links:
        // block behaves exactly as before, and a crew that declares one is actually held to
        // it. Without this line the links: grammar parsed, registered — and guarded nothing,
        // because no composition root ever put the stage on the pipeline. A deployment that
        // wants the closed door swaps the policy in its own configureServices.
        services.AddOrkeonEventHubAcl();

        // E-mail (MAIL): the thirteen email_* tools, inert until an account is declared under
        // Orkeon:Tools:Email. OAuth tokens live under the internal /credentials root the
        // configuration step mounted for them, reached through the privileged view of the VFS
        // that no agent-facing tool resolves (ADR-008, ADR-012).
        services.AddOrkeonEmailTools(context.Configuration);
        if (HasInternalRoot(RunnerVirtualRoots.Credentials))
        {
            services.AddOrkeonEmailTokenStore(
                sp => sp.GetRequiredService<PrivilegedFileSystemAccess>().FileSystem,
                $"{RunnerVirtualRoots.Credentials}/{EmailCredentialsLocation.TokenSubdirectory}");
        }

        // RAG (GAP-02): the subsystem and its three agent tools, for every runner — YAML
        // crews, crew directories, declarative and procedural .ork.ts, and orkeon-host. A
        // crew's rag: collections are ingested when it loads, its agents' knowledge: reaches
        // their prompts, and rag_search/rag_ingest/rag_eval can be listed in tools:. Nothing
        // is resolved until a crew uses it: the store, its provider and the embeddings wait
        // for the first ingestion or retrieval. The ONNX reranker (balanced/quality) is each
        // binary's to add — orkeon, orkeon-host and orkeon-repl do (GAP-25) — so this package
        // stays free of ONNX; a C# host without it fails those two profiles at their first use.
        services.AddOrkeonRag(context.Configuration);
        services.AddOrkeonRagTools();

        // Virtual file system mounts (from appsettings + CLI --mount args, plus the
        // infrastructure mounts a runner declares for itself). Either list alone is enough
        // to make the VFS real: --list-tools has only the latter, and without the service
        // the filesystem-backed tools cannot even be constructed.
        if (HasMounts(ConfigurationKeys.FileSystemMounts) || HasMounts(ConfigurationKeys.FileSystemInternalMounts))
            services.AddOrkeonFileSystem(context.Configuration);
        else
            DeclareFileSystemSettings(context, services);

        bool HasMounts(string key)
        {
            var section = context.Configuration.GetSection(key);
            return section.Exists() && section.GetChildren().Any();
        }

        bool HasInternalRoot(string root) =>
            context.Configuration.GetSection(ConfigurationKeys.FileSystemInternalMounts).GetChildren()
                .Any(entry => entry.Value is { Length: > 0 } mount
                    && string.Equals(MountSelection.TryGetVirtualRoot(mount), root, StringComparison.Ordinal));

        // RaggableTree — available by default (crew-driven). Enables the semantic-graph
        // tools (codebase_map, symbol_detail, flow_trace, …) backed by a singleton
        // InMemoryRaggableStore. Analysed languages are auto-detected per index_codebase
        // call, never pinned a priori; a host opts out with "RaggableTree:Enabled": false.
        RegisterRaggableTree(context, services);

        // WebSearch tool — API key resolved at runtime via ISecretProvider
        services.AddOrkeonWebSearchTool();

        // Generic cache_search tool — queries the RAG cache populated by other
        // tools (web_scrape with cached=true, etc). Reuses IEmbeddingService and
        // IMemoryProvider from AddOrkeonInfrastructure() so agents can retrieve
        // chunks semantically instead of pinning full pages in their context.
        services.AddOrkeonCacheSearchTool();

        var braveKey = context.Configuration["BRAVE_API_KEY"]
            ?? Environment.GetEnvironmentVariable("BRAVE_API_KEY");
        if (!string.IsNullOrEmpty(braveKey))
            services.AddOrkeonBraveSearchTool(braveKey);

        // The MCP servers the settings declare (STUDIO-21): bound and provided here, connected
        // by McpStartup before the crew loads, since the runners never start the host.
        RegisterMcp(context, services);

        // The tool registry is AddOrkeonInfrastructure's default ToolRegistry, seeded from every
        // IBaseTool registered above (GAP-11): the runners need nothing of their own.
    }

    /// <summary>
    /// The <c>MCP</c> section of the settings, when it declares servers (STUDIO-21). Until then
    /// the section was bound by nobody: <c>orkeon run</c> built its host without configuration,
    /// so a server written in the settings changed nothing. An absent or empty
    /// <c>MCP:Servers</c>, or <c>MCP:Enabled = false</c>, registers nothing — exactly the
    /// surface every run had before — and the servers are connected by
    /// <see cref="McpStartup"/>, not by a hosted service, because the runners never start the
    /// host. A section that still carries the removed <c>MCP:EnableServer</c> fails the host
    /// first, servers declared or not (GAP-24). <c>AddOrkeonMcp</c> binds the section and has it
    /// judged at the host's start (GAP-40) — a server's <c>Transport</c> the binder cannot convert
    /// refuses the start, not the connection step that <c>--list-tools</c> and <c>orkeon-host</c>
    /// take before anything else. Its keys, and those of <c>MCP:Server</c> that <c>orkeon mcp
    /// serve</c> reads, are judged whether servers are declared or not: the section is the host's.
    /// </summary>
    private static void RegisterMcp(HostBuilderContext context, IServiceCollection services)
    {
        McpStartup.RefuseServerSwitch(context.Configuration);
        services.DeclareSettingsShape(ConfigurationKeys.McpSection, typeof(McpOptions));
        services.DeclareSettingsShape($"{ConfigurationKeys.McpSection}:Server", typeof(McpServerOptions));
        if (!McpStartup.IsConfigured(context.Configuration))
            return;

        services.AddOrkeonMcp(context.Configuration);
    }

    /// <summary>
    /// Declares <c>Orkeon:Tools:Shell</c>, which <c>AddOrkeonCodeTools</c> binds and validates at
    /// start (GAP-40): its values and its keys are judged with every section's. The REPL declares it
    /// the same way.
    /// </summary>
    internal static void DeclareShellSettings(IServiceCollection services) =>
        services.AddOptions<Orkeon.Tools.Code.ShellToolOptions>().DeclareSettings(Orkeon.Tools.Code.ShellToolOptions.SectionName);

    /// <summary>
    /// <c>Orkeon:FileSystem</c> and <c>Orkeon:Sandbox</c>, which <c>AddOrkeonFileSystem</c> binds and
    /// declares, judged when nothing is mounted too (GAP-40): a run always mounts — its crew, its
    /// output —, but <c>orkeon doctor</c> builds the host without a mount and must refuse what the run
    /// refuses. Bound here only when <c>AddOrkeonFileSystem</c> is not called: a list bound twice holds
    /// its entries twice.
    /// </summary>
    private static void DeclareFileSystemSettings(HostBuilderContext context, IServiceCollection services)
    {
        services.AddOptions<Orkeon.Infrastructure.Configuration.FileSystemOptions>()
            .Bind(context.Configuration.GetSection(FileSystemSection))
            .DeclareSettings(FileSystemSection);
        services.AddOptions<Orkeon.Infrastructure.Sandbox.SandboxFileSystemOptions>()
            .Bind(context.Configuration.GetSection(SandboxSection))
            .DeclareSettings(SandboxSection);
    }

    /// <summary>The section of the mounts.</summary>
    private const string FileSystemSection = "Orkeon:FileSystem";

    /// <summary>The section of the per-process sandbox root.</summary>
    private const string SandboxSection = "Orkeon:Sandbox";

    /// <summary>The section of the crew factory's switches.</summary>
    private const string CrewFactorySection = "Orkeon:CrewFactory";

    /// <summary>The keys of <c>Orkeon:CrewFactory</c> a runner reads (GAP-40). Never instantiated: its properties are the keys.</summary>
    private abstract class CrewFactorySettingsShape
    {
        public bool StrictTools { get; set; }
    }

    private static void ConfigureRunnerLogging(
        HostBuilderContext context,
        IServiceCollection services,
        Action<HostBuilderContext, ILoggingBuilder>? configureLogging)
    {
        // The levels and the console options of `Logging`, judged here, under the barrier, before
        // the host builds its logger from them (GAP-40): a level the logging configuration does not
        // know threw from the build, a sentence without the key, past every barrier — and systemd
        // restarted orkeon-host on it every ten seconds.
        SettingsValidation.CheckLogging(context.Configuration);

        // Logging (customizable, sensible default)
        if (configureLogging != null)
        {
            services.AddLogging(b => configureLogging(context, b));
            return;
        }

        // Quiet default — aligned across all runners (RUN-03):
        // single-line console + Warning level. Use --verbose 1 (or 2)
        // to opt into Information/Debug from Orkeon modules via
        // RunnerExecution.ConfigureVerboseLogging.
        services.AddLogging(b =>
        {
            b.AddSimpleConsole(opts =>
            {
                opts.SingleLine = true;
                opts.TimestampFormat = "HH:mm:ss.fff ";
            });
            b.SetMinimumLevel(LogLevel.Warning);
        });
    }

    private static void ConfigureLlmExchangeLogging(
        HostBuilderContext context,
        IServiceCollection services,
        string? llmLogVirtualPath)
    {
        // --- LLM exchange logging (--llm-log) ---
        if (string.IsNullOrEmpty(llmLogVirtualPath))
            return;

        // A virtual path, handed straight to LlmExchangeJsonLogger's logVirtualDir: the
        // logger writes through IFileSystemService. Resolving it to a full physical path
        // here is what used to force the identity mount (ADR-008).
        var logDir = llmLogVirtualPath;
        // The "LlmLogging" section, read with --llm-log only. Optional; absent keys keep their
        // defaults (FullEmbeddingLog=true, LogStreamingExchanges=true, MaxBodyLengthChars=0/no
        // truncation), and a value that is none refuses the start, naming its key (GAP-40): it
        // used to fall back on the default in silence. Its keys are judged too.
        services.DeclareSettingsShape(LlmLoggingSection, typeof(LlmLoggingSettingsShape));
        var llmLogSection = context.Configuration.GetSection(LlmLoggingSection);
        var llmOpts = new LlmLoggingOptions
        {
            FullEmbeddingLog = ReadBool(llmLogSection, "FullEmbeddingLog") ?? true,
            LogStreamingExchanges = ReadBool(llmLogSection, "LogStreamingExchanges") ?? true,
            MaxBodyLengthChars = ReadInt(llmLogSection, "MaxBodyLengthChars") ?? 0,
        };
        services.AddLlmExchangeLogging(logDir, llmOpts);
    }

    /// <summary>The section of the LLM exchange logging (<c>--llm-log</c>).</summary>
    private const string LlmLoggingSection = "LlmLogging";

    /// <summary>The keys of <c>LlmLogging</c> (GAP-40). Never instantiated: its properties are the keys.</summary>
    private abstract class LlmLoggingSettingsShape
    {
        public bool FullEmbeddingLog { get; set; }

        public bool LogStreamingExchanges { get; set; }

        public int MaxBodyLengthChars { get; set; }
    }

    /// <summary>A switch of a section read raw: <c>true</c> or <c>false</c>, any case; blank is unset; anything else is refused by its key.</summary>
    private static bool? ReadBool(IConfigurationSection section, string key)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return bool.TryParse(raw, out var value)
            ? value
            : throw new InvalidOperationException($"{section.Path}:{key} is '{raw}', which is not true or false.");
    }

    /// <summary>A whole number of a section read raw, written invariant; blank is unset; anything else is refused by its key.</summary>
    private static int? ReadInt(IConfigurationSection section, string key)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"{section.Path}:{key} is '{raw}', which is not a whole number.");
    }

    /// <summary>
    /// The absolute physical base path a mount string declares, or null when the string is
    /// not a well-formed mount or names a path the platform refuses to resolve — both are
    /// reported by the mount parser or the registry at host build time, with their own
    /// message. The grammar (drive letters, escaped separators) is the domain type's business,
    /// not this file's.
    /// </summary>
    private static string? TryExtractMountBasePath(string? mountString)
    {
        if (mountString is null || FileSystemMount.TryGetBasePath(mountString) is not { } basePath)
            return null;

        try
        {
            return Path.GetFullPath(basePath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The only keys of the <c>RaggableTree</c> section (GAP-15).</summary>
    private static readonly string[] s_raggableTreeKeys = ["Enabled", "Embedding"];

    internal static void RegisterRaggableTree(
        HostBuilderContext context,
        IServiceCollection services)
    {
        // Available by default; a host opts out with "RaggableTree:Enabled": false. The section
        // is OPTIONAL and carries two keys: Enabled and Embedding. What an index covers —
        // languages, exclusions, root alias, LLM enrichment — is set per index_codebase call by
        // the crew/agent, never pinned a priori by the host.
        var section = context.Configuration.GetSection("RaggableTree");
        var retired = section.GetChildren()
            .Select(c => c.Key)
            .FirstOrDefault(k => !s_raggableTreeKeys.Contains(k, StringComparer.OrdinalIgnoreCase));
        if (retired is not null)
            throw new InvalidOperationException(
                $"RaggableTree:{retired} is not a setting: the RaggableTree section carries only " +
                "Enabled and Embedding. What an index covers (exclude, root_alias, enrich_with_llm, " +
                "languages) is an argument of each index_codebase call. Remove the key.");

        // The keys under Embedding are judged with every declared section's (GAP-40), and its
        // values read strictly here, the section enabled or not: a provider written wrong ran the
        // local embeddings, and a number that was none the provider's default, in silence.
        services.DeclareSettingsShape("RaggableTree", typeof(RaggableTreeSettingsShape));
        var embeddingSection = section.GetSection("Embedding");
        // Default to on-device local embeddings (zero-config BGE-micro-v2, no network) so
        // codebase_search works out of the box; a host can override to OpenAI/Ollama via config.
        var provider = EmbeddingProvider(embeddingSection);
        var embedding = new EmbeddingOptions
        {
            Provider = provider,
            // Empty → each provider's own default model (LocalSmartComponents → bge-micro-v2).
            Model = embeddingSection["Model"] ?? "",
            ApiKey = embeddingSection["ApiKey"],
            BaseUrl = embeddingSection["BaseUrl"] is { } embedBaseUrl ? EmbeddingAddress(embedBaseUrl) : null,
            Dimensions = ReadInt(embeddingSection, "Dimensions"),
            MaxTextChars = ReadInt(embeddingSection, "MaxTextChars"),
        };

        if (section.Exists() && !section.GetValue("Enabled", defaultValue: true))
            return;

        var options = new RaggableTreeOptions
        {
            Enabled = true,
            Embedding = embedding,
        };

        // Pre-register the on-device provider explicitly when selected, so resolution never relies
        // on the reflection fallback (which can miss a not-yet-loaded assembly).
        if (provider == EmbeddingProviderKind.LocalSmartComponents)
            services.AddOrkeonLocalEmbeddings();

        services.AddRaggableTreeWithLogging(options);
        services.AddRaggableTreeTools();
    }

    /// <summary>
    /// <c>RaggableTree:Embedding:BaseUrl</c> as an address, or a refusal that names the key
    /// (GAP-35): <c>new Uri(...)</c> let a bare <see cref="UriFormatException"/> out of the build.
    /// </summary>
    private static Uri EmbeddingAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : throw new InvalidOperationException(
                $"RaggableTree:Embedding:BaseUrl is '{value}', which is not an address: write the embedding "
                + "endpoint's http:// or https:// URL, such as http://localhost:11434.");

    /// <summary>
    /// <c>RaggableTree:Embedding:Provider</c>: one of the provider names, any case, the local
    /// embeddings when blank (GAP-40). A name written wrong — or a number, which the enum parser took
    /// — used to run the local embeddings without a word.
    /// </summary>
    private static EmbeddingProviderKind EmbeddingProvider(IConfigurationSection embedding)
    {
        var raw = embedding["Provider"];
        if (string.IsNullOrWhiteSpace(raw))
            return EmbeddingProviderKind.LocalSmartComponents;

        var names = Enum.GetNames<EmbeddingProviderKind>();
        var name = names.FirstOrDefault(known => string.Equals(known, raw.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is not null
            ? Enum.Parse<EmbeddingProviderKind>(name)
            : throw new InvalidOperationException(
                $"{embedding.Path}:Provider is '{raw}', which is not an embedding provider: write one of {string.Join(", ", names)}.");
    }

    /// <summary>The keys of <c>RaggableTree</c> (GAP-15, GAP-40). Never instantiated: its properties are the keys.</summary>
    private abstract class RaggableTreeSettingsShape
    {
        public bool Enabled { get; set; }

        public RaggableTreeEmbeddingShape? Embedding { get; set; }
    }

    /// <summary>The keys of <c>RaggableTree:Embedding</c>.</summary>
    private abstract class RaggableTreeEmbeddingShape
    {
        public string? Provider { get; set; }

        public string? Model { get; set; }

        public string? ApiKey { get; set; }

        public string? BaseUrl { get; set; }

        public int? Dimensions { get; set; }

        public int? MaxTextChars { get; set; }
    }

    private static void RegisterLlmProvider(HostBuilderContext context, IServiceCollection services)
    {
        // Llm:Profiles:<name> — the named providers a crew may pick per agent or per task
        // (GAP-17). Read and validated now: a bad profile fails the host build with its key.
        services.AddOrkeonLlmProfiles(context.Configuration);

        // No default section (none at all, or one holding profiles alone) → the echo
        // provider, announced once per host build by WarnIfLlmNotConfigured (no logger exists
        // yet at this point).
        if (!LlmSettings.HasDefault(context.Configuration))
        {
            RegisterEchoProvider(services);
            return;
        }

        var llmConfig = LlmSettings.ReadDefault(context.Configuration);

        services.AddSingleton<IBasicLlmProvider>(sp =>
        {
            var factory = sp.GetRequiredService<ILlmProviderFactory>();
            return factory.Create(llmConfig);
        });

        services.AddSingleton<IChatClient>(sp =>
        {
            var basicProvider = sp.GetRequiredService<IBasicLlmProvider>();
            var textParser = sp.GetService<Application.Interfaces.LLM.IToolCallParser>();
            if (basicProvider is LlmProviderAdapter adapter)
                return new LlmProviderToChatClientAdapter(adapter.UnderlyingProvider, llmConfig, textParser);
            throw new InvalidOperationException(
                $"LLM provider for model '{llmConfig.Model}' does not expose ILlmProvider.");
        });
    }

    /// <summary>
    /// The fallback <see cref="LlmNotConfiguredMessage"/> promises: a host with no <c>Llm</c>
    /// section runs its crews on the echo provider (<see cref="UndefinedLlmProvider"/>, the one
    /// the scripting facade already answers <c>&lt;undefined-llm&gt;</c> with), which replays the
    /// prompt instead of answering it. Registered explicitly because the infrastructure default
    /// is an OpenAI provider without a key, and since LLM-11 a call that provider refuses is a
    /// failed task, not an empty answer — the bundled demos then exited 2 where the warning had
    /// announced a run.
    /// </summary>
    /// <remarks>
    /// Registered like any provider the factory does not build (<c>AddOrkeonLlmProvider</c>):
    /// metered, so an observed run still reports each call — at the zero tokens the echo
    /// declares (STUDIO-42).
    /// </remarks>
    private static void RegisterEchoProvider(IServiceCollection services) =>
        services.AddOrkeonLlmProvider(_ => new UndefinedLlmProvider());
}
