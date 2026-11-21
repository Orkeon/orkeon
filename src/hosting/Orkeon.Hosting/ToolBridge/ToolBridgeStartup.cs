using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The caller's link to the tool bridge (#445), one per host: <see cref="ConnectAsync"/> asks the bridge what it
/// serves, checks it against <c>Orkeon:Tools:Bridge</c> and puts a <see cref="ToolBridgeProxy"/> in the registry in
/// place of each tool bridged. It refuses the start, with <see cref="RunnerSettingsException"/>, when the bridge
/// cannot be reached, when it refuses the token, when a tool <c>Allow</c> names is not served, or when a contract
/// hash differs; with the lone <c>*</c>, it bridges the tools it selected that the bridge serves.
/// </summary>
internal sealed partial class ToolBridgeLink : IDisposable
{
    private readonly ToolBridgeClientOptions _options;
    private readonly Func<string, string?> _readEnvironment;
    private readonly IFileSystemService _fileSystem;
    private readonly TimeProvider? _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ToolBridgeClient? _client;

    /// <summary>Builds the link; nothing is asked of the bridge before <see cref="ConnectAsync"/>.</summary>
    /// <param name="options">The section <c>Orkeon:Tools:Bridge</c>.</param>
    /// <param name="fileSystem">The caller's file system, through which the files of a bridged call cross.</param>
    /// <param name="readEnvironment">Reads an environment variable; the process environment when null.</param>
    /// <param name="timeProvider">The clock of the heartbeat; the system's when null.</param>
    public ToolBridgeLink(
        ToolBridgeClientOptions options, IFileSystemService fileSystem, Func<string, string?>? readEnvironment = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _options = options;
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
    }

    /// <summary>The client of the bridge, once connected with something bridged; null otherwise.</summary>
    internal ToolBridgeClient? Client => _client;

    /// <summary>The names of the tools bridged; empty before <see cref="ConnectAsync"/> or when nothing is.</summary>
    public ImmutableList<string> BridgedTools { get; private set; } = [];

    /// <summary>Whether <see cref="ConnectAsync"/> completed.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Connects once: a second call returns at once. Does nothing while the section bridges nothing.
    /// </summary>
    /// <param name="registry">The caller's registry, whose tools are replaced.</param>
    /// <param name="decorator">The host's tool decorator (<c>--events</c>), applied to each proxy; none when null.</param>
    /// <param name="configuration">The host's settings, read for the settings of a tool that now runs elsewhere.</param>
    /// <param name="logger">The link's logger.</param>
    /// <param name="operatorLogger">The operator channel, for the warnings.</param>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns>A task that completes once the tools are bridged.</returns>
    public async System.Threading.Tasks.Task ConnectAsync(
        IToolRegistry registry,
        IToolDecorator? decorator,
        IConfiguration? configuration,
        ILogger logger,
        ILogger operatorLogger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(operatorLogger);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
                return;
            if (_options.IsEnabled)
                await BridgeAsync(registry, decorator, configuration, logger, operatorLogger, cancellationToken).ConfigureAwait(false);
            IsConnected = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client?.Dispose();
        _gate.Dispose();
    }

    private async System.Threading.Tasks.Task BridgeAsync(
        IToolRegistry registry, IToolDecorator? decorator, IConfiguration? configuration, ILogger logger, ILogger operatorLogger,
        CancellationToken cancellationToken)
    {
        const string Section = ToolBridgeClientOptions.SectionName;
        var problems = _options.Problems().ToList();
        if (problems.Count > 0)
            throw Refusal(string.Join(" ", problems));
        var token = ToolBridgeProtocol.ReadToken(_readEnvironment(_options.TokenEnvVar), _options.TokenEnvVar, out var tokenProblem)
            ?? throw Refusal(tokenProblem!);

        // A tool the registry holds bridged already is left as it is: a proxy is never wrapped in another one.
        var all = await registry.GetAllToolsAsync(cancellationToken).ConfigureAwait(false);
        var local = all.Where(t => !IsBridged(t)).ToList();
        if (all.Where(IsBridged).Select(t => t.Name).ToList() is { Count: > 0 } already)
            RunnerOperatorLog.Warn(operatorLogger,
                $"{Section}: {string.Join(", ", already.Select(n => $"'{n}'"))} runs on a tool bridge already and is left as it is.");
        var selection = ToolBridgeSelection.Resolve(Section, local, [.. _options.Allow], [.. _options.Deny]);
        if (selection.Problems.Count > 0)
            throw Refusal(string.Join(" ", selection.Problems));
        var selected = Describe(selection.Tools.Select(t => t.Name));
        LogSelected(logger, selected);
        foreach (var warning in selection.Warnings)
            RunnerOperatorLog.Warn(operatorLogger, warning);

        if (_options.IsDockerHostOverHttp)
            RunnerOperatorLog.Warn(operatorLogger,
                $"{Section}:Address names {ToolBridgeAddress.DockerHost} over http: the token and every call travel in clear over the Docker host's virtual network.");
        var client = new ToolBridgeClient(new Uri(_options.Address!), token, _options.ConnectTimeout, _timeProvider,
            _options.EffectiveHostHeader(), _options.MaxResponseBytes);
        BridgeToolListResponse served;
        try
        {
            served = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ToolBridgeException ex)
        {
            client.Dispose();
            throw Refusal(ex.Message);
        }

        if (ListProblem(served, client.Address) is { } listProblem)
        {
            client.Dispose();
            throw Refusal(listProblem);
        }

        if (served.HeartbeatWindowMs is { } window)
            client.HeartbeatWindow = TimeSpan.FromMilliseconds(window);
        var hashes = served.Tools.ToDictionary(t => t.Name, t => t.ContractHash, StringComparer.OrdinalIgnoreCase);
        var named = new HashSet<string>(_options.Allow.Where(a => a != ToolBridgeSelection.All), StringComparer.OrdinalIgnoreCase);
        var refusals = new List<string>();
        var bridged = new List<IBaseTool>();
        foreach (var tool in selection.Tools)
        {
            if (!hashes.TryGetValue(tool.Name, out var hash))
            {
                if (named.Contains(tool.Name))
                    refusals.Add($"{Section}:Allow names '{tool.Name}', which the tool bridge at {client.Address} does not serve.");
                continue;
            }

            if (!string.Equals(hash, ToolBridgeContract.HashOf(tool), StringComparison.Ordinal))
                refusals.Add($"the contract of '{tool.Name}' differs between this Orkeon and the tool bridge at {client.Address}: both must run the same version of the tool.");
            else
                bridged.Add(tool);
        }

        if (refusals.Count > 0)
        {
            client.Dispose();
            throw Refusal(string.Join(" ", refusals));
        }

        await InstallAsync(registry, decorator, client, bridged, operatorLogger, cancellationToken).ConfigureAwait(false);

        _client = client;
        BridgedTools = [.. bridged.Select(t => t.Name)];
        var address = client.Address.ToString();
        var names = Describe(BridgedTools);
        LogBridged(logger, address, names);
        foreach (var name in BridgedTools)
        {
            if (LocalSettingsOf(configuration, name) is { } section)
                RunnerOperatorLog.Warn(operatorLogger,
                    $"{section} is set here, but '{name}' runs on the tool bridge with the bridge's own settings: this section is not used.");
        }
    }

    /// <summary>
    /// Puts a proxy in the registry in place of each tool of <paramref name="bridged"/>, all or none: when a replacement
    /// fails (the registry changed under a tool, a cancellation), the proxies already installed give their place back to
    /// the originals, the client is disposed and the start is refused, so the next attempt finds the registry as it was.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Whatever stopped the replacement, the registry is rolled back before the failure is reported.")]
    private async System.Threading.Tasks.Task InstallAsync(
        IToolRegistry registry, IToolDecorator? decorator, ToolBridgeClient client, List<IBaseTool> bridged, ILogger operatorLogger,
        CancellationToken cancellationToken)
    {
        var installed = new List<(IBaseTool Original, IBaseTool Replacement)>();
        string? failure = null;
        Exception? thrown = null;
        foreach (var tool in bridged)
        {
            var proxy = new ToolBridgeProxy(tool, client, _fileSystem, _options.MaxFileBytes, operatorLogger);
            var replacement = decorator?.Decorate(proxy) ?? proxy;
            try
            {
                if (await registry.ReplaceToolAsync(tool, replacement, cancellationToken).ConfigureAwait(false))
                {
                    installed.Add((tool, replacement));
                    continue;
                }

                failure = $"'{tool.Name}' changed in the registry while it was being bridged.";
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            break;
        }

        if (failure is null && thrown is null)
            return;
        var kept = new List<string>();
        foreach (var (original, replacement) in Enumerable.Reverse(installed))
        {
            if (!await registry.ReplaceToolAsync(replacement, original, CancellationToken.None).ConfigureAwait(false))
                kept.Add(original.Name);
        }

        client.Dispose();
        if (thrown is OperationCanceledException && kept.Count == 0)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(thrown);
        var reason = failure ?? thrown!.Message;
        if (kept.Count > 0)
            reason += $" The registry changed under {string.Join(", ", kept.Select(n => $"'{n}'"))} too: their local instance could not be put back.";
        throw Refusal(reason);
    }

    /// <summary>Whether <paramref name="tool"/> is, or wraps at any depth, a proxy of the bridge: a tool bridged already.</summary>
    /// <param name="tool">A tool of the registry.</param>
    /// <returns>True for a bridged tool.</returns>
    internal static bool IsBridged(IBaseTool tool)
    {
        var current = tool;
        for (var depth = 0; depth < 16 && current is not null; depth++)
        {
            if (current is ToolBridgeProxy)
                return true;
            current = (current as IToolWrapper)?.Inner;
        }

        return false;
    }

    /// <summary>
    /// The section of this Orkeon's settings that configures <paramref name="tool"/>, by the convention of the built-in
    /// tools (<c>Orkeon:Tools:Firefox</c> for <c>firefox</c>, <c>Orkeon:Tools:Shell</c> for <c>shell_command</c>): the
    /// tool's name in Pascal case, or its first word.
    /// </summary>
    /// <param name="configuration">The settings.</param>
    /// <param name="tool">The tool's name.</param>
    /// <returns>The section's path when it holds a value; otherwise null.</returns>
    internal static string? LocalSettingsOf(IConfiguration? configuration, string tool)
    {
        if (configuration is null)
            return null;
        var words = tool.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;
        static string Pascal(string word) => char.ToUpperInvariant(word[0]) + word[1..];
        foreach (var candidate in new[] { string.Concat(words.Select(Pascal)), Pascal(words[0]) }.Distinct(StringComparer.Ordinal))
        {
            var path = $"Orkeon:Tools:{candidate}";
            if (!string.Equals(path, ToolBridgeClientOptions.SectionName, StringComparison.OrdinalIgnoreCase)
                && configuration.GetSection(path).GetChildren().Any())
                return path;
        }

        return null;
    }

    /// <summary>
    /// What the bridge's list of tools cannot be taken for: an entry without a name or a hash, a name listed twice, a
    /// heartbeat window outside <see cref="ToolBridgeServerOptions.MinHeartbeatWindow"/> and
    /// <see cref="ToolBridgeServerOptions.MaxHeartbeatWindow"/>; null when it is sound.
    /// </summary>
    /// <param name="served">The bridge's answer.</param>
    /// <param name="address">The bridge's address, for the message.</param>
    /// <returns>One sentence, or null.</returns>
    internal static string? ListProblem(BridgeToolListResponse served, Uri address)
    {
        ArgumentNullException.ThrowIfNull(served);
        if (served.Tools is null || served.Tools.Any(t => t is null || string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.ContractHash)))
            return $"the tool bridge at {address} sent a list of tools with an entry that names no tool or no contract hash.";
        var twice = served.Tools.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (twice.Count > 0)
            return $"the tool bridge at {address} lists {string.Join(", ", twice.Select(n => $"'{n}'"))} more than once.";
        var min = ToolBridgeServerOptions.MinHeartbeatWindow;
        var max = ToolBridgeServerOptions.MaxHeartbeatWindow;
        if (served.HeartbeatWindowMs is { } window && (window < (long)min.TotalMilliseconds || window > (long)max.TotalMilliseconds))
            return $"the tool bridge at {address} gives a heartbeat window of {window} ms, outside {min} to {max}.";
        return null;
    }

    private static RunnerSettingsException Refusal(string reason) => new($"the tool bridge refuses the start: {reason}");

    private static string Describe(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Tool bridge: Orkeon:Tools:Bridge selects {Tools}")]
    private static partial void LogSelected(ILogger logger, string tools);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Tool bridge at {Address}: bridged {Tools}")]
    private static partial void LogBridged(ILogger logger, string address, string tools);
}

/// <summary>The mark of the host of <c>orkeon bridge serve</c> (#445): a serving host never connects to a bridge.</summary>
internal sealed class ToolBridgeServing;

/// <summary>
/// The shared start-up step of every caller of the tool bridge (#445): <c>orkeon run</c>, <c>validate</c>,
/// <c>--list-tools</c>, <c>orkeon mcp serve</c>, the REPL and the other CLI paths call <see cref="ConnectAsync(IServiceProvider, CancellationToken)"/>
/// right after the MCP servers connect, <c>orkeon-host</c> through its hosted service. A host without the link
/// registered (no <c>Orkeon:Tools:Bridge</c>) is left as it is.
/// </summary>
internal static class ToolBridgeStartup
{
    /// <summary>Bridges the tools <c>Orkeon:Tools:Bridge</c> selects, once per host.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns>A task that completes once the tools are bridged.</returns>
    public static System.Threading.Tasks.Task ConnectAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        // The host of orkeon bridge serve runs its tools for a caller: it never dials a bridge itself.
        if (services.GetService<ToolBridgeLink>() is not { } link || services.GetService<ToolBridgeServing>() is not null)
            return System.Threading.Tasks.Task.CompletedTask;
        var loggerFactory = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        return link.ConnectAsync(
            services.GetRequiredService<IToolRegistry>(),
            services.GetService<IToolDecorator>(),
            services.GetService<IConfiguration>(),
            loggerFactory.CreateLogger("Orkeon.Hosting.ToolBridge"),
            RunnerOperatorLog.Create(loggerFactory),
            cancellationToken);
    }

    /// <summary>
    /// What the host of <c>orkeon bridge serve</c> adds: the folder of the calls' scratches
    /// (<see cref="ToolBridgeScratch.Parent"/>) among the directories its path validator allows, as the sandbox's
    /// session folder is, and the mark that keeps <see cref="ConnectAsync(IServiceProvider, CancellationToken)"/> from
    /// dialing a bridge of its own.
    /// </summary>
    /// <param name="services">The host's services.</param>
    public static void RegisterServing(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ToolBridgeServing>();
        services.AddOptions<Orkeon.Infrastructure.Configuration.PathSecurityOptions>().Configure(options =>
        {
            if (!options.AdditionalAllowedDirectories.Contains(ToolBridgeScratch.Parent))
                options.AdditionalAllowedDirectories.Add(ToolBridgeScratch.Parent);
        });
    }

    /// <summary>
    /// Declares both sections of the bridge, binds them and judges them at start, and registers the caller's link when
    /// <c>Orkeon:Tools:Bridge</c> names an address.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's settings.</param>
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<ToolBridgeClientOptions>()
            .Bind(configuration.GetSection(ToolBridgeClientOptions.SectionName))
            .DeclareSettings(ToolBridgeClientOptions.SectionName)
            .ValidateSettings((options, _) => options.Problems());
        services.AddOptions<ToolBridgeServerOptions>()
            .Bind(configuration.GetSection(ToolBridgeServerOptions.SectionName))
            .DeclareSettings(ToolBridgeServerOptions.SectionName)
            .ValidateSettings((options, _) => options.Problems());
        if (string.IsNullOrWhiteSpace(configuration[$"{ToolBridgeClientOptions.SectionName}:Address"]))
            return;
        services.AddSingleton(sp => new ToolBridgeLink(
            sp.GetRequiredService<IOptions<ToolBridgeClientOptions>>().Value, sp.GetRequiredService<IFileSystemService>()));
    }
}
