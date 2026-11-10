using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Runs;
using Orkeon.Compliance.Vfs;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting.Runs;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Hosting;

/// <summary>What <see cref="McpCrewToolHost.OpenAsync"/> builds the crews' hosts with.</summary>
internal sealed record McpCrewHostOptions
{
    /// <summary>Gets the settings file, as <c>--settings</c> gave it; resolved from each crew's folder when null.</summary>
    public string? SettingsPath { get; init; }

    /// <summary>Gets the directory the relative crew paths and the hosts are anchored at.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Gets whether a crew outside the working directory is served: the guard of <c>orkeon run --allow-external-mounts</c>.</summary>
    public bool AllowExternalMounts { get; init; }

    /// <summary>Gets the console: its error output is where a refusal is written, its output is never touched.</summary>
    public required IRunnerConsole Console { get; init; }

    /// <summary>Gets a test seam: services added to every crew's host, before the progress hook wraps the crew hook.</summary>
    public Action<HostBuilderContext, IServiceCollection>? ConfigureServices { get; init; }
}

/// <summary>
/// The crews <c>orkeon mcp serve --crew</c> names, served as MCP tools (FWK-24): <c>run_crew_&lt;name&gt;</c> runs one
/// crew and answers its final output, and every task of every crew is a prompt. A crew is opened once, at the start,
/// through <see cref="CrewRunEntryPoint"/> (the load <c>orkeon run</c> and <c>orkeon eval</c> use), so the host of a crew
/// carries the budget, the guardian and the permission gate of <c>orkeon run</c> and a call runs in a scope of its own.
/// Only the crews the operator named are served: a client never gives a path.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: resolves the crew paths the operator typed on the command line, before any virtual file system exists.")]
internal sealed partial class McpCrewToolHost : IMcpServerExtension, IDisposable
{
    private const string ToolPrefix = "run_crew_";

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_.\-]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugPattern();

    /// <summary>The call in flight on this control flow: the crew host's hook reads it to report the tasks that finish.</summary>
    private static readonly AsyncLocal<RunProgress?> s_current = new();

    private readonly List<ServedCrew> _crews;

    private McpCrewToolHost(List<ServedCrew> crews)
    {
        _crews = crews;
        Tools = [.. crews.Select(ToolOf)];
        Prompts = [.. crews.SelectMany(PromptsOf)];
    }

    /// <inheritdoc />
    public IReadOnlyList<McpToolDefinition> Tools { get; }

    /// <inheritdoc />
    public IReadOnlyList<McpPrompt> Prompts { get; }

    /// <summary>Gets the host of each crew, in the order the crews were named.</summary>
    internal IReadOnlyList<IHost> Hosts => [.. _crews.Select(c => c.Entry.Host)];

    private sealed record TaskText(string Description, string ExpectedOutput, IReadOnlyList<string> Placeholders);

    private sealed record ServedCrew(
        string Slug, string Name, string Goal, IReadOnlyList<TaskText> Tasks, IReadOnlyList<string> Placeholders, CrewRunEntryPoint Entry)
    {
        /// <summary>One call of the crew at a time: the crew's host is shared by its calls, and they share what the host mounts (/output, the checkpoints).</summary>
        public CrewGate Gate { get; } = new();

        public string ToolName => ToolPrefix + Slug;
    }

    private sealed record CrewDescription(string? Name, string Goal, IReadOnlyList<TaskText> Tasks);

    /// <summary>Lets one call of a crew run at a time and counts those waiting their turn.</summary>
    private sealed class CrewGate : IDisposable
    {
        private readonly SemaphoreSlim _free = new(1, 1);
        private readonly CancellationTokenSource _closed = new();
        private int _waiting;

        public int Waiting => Volatile.Read(ref _waiting);

        /// <summary>
        /// Takes the crew, or says why not: <see cref="Admission.Busy"/> when <see cref="MaxWaitingCalls"/> calls
        /// already wait for it, <see cref="Admission.Closed"/> once <see cref="Close"/> was called for a call that would
        /// have to wait, including one that was waiting then. A call read before the close that finds the crew free
        /// still runs: its client may still read the answer.
        /// </summary>
        public async Task<Admission> EnterAsync(CancellationToken ct)
        {
            if (_closed.IsCancellationRequested)
                return _free.Wait(0, CancellationToken.None) ? Admission.Entered : Admission.Closed;
            if (Interlocked.Increment(ref _waiting) > MaxWaitingCalls)
            {
                Interlocked.Decrement(ref _waiting);
                return Admission.Busy;
            }

            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closed.Token);
                await _free.WaitAsync(linked.Token).ConfigureAwait(false);
                return Admission.Entered;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Admission.Closed;
            }
            finally
            {
                Interlocked.Decrement(ref _waiting);
            }
        }

        public void Leave() => _free.Release();

        /// <summary>Admits no more waiting calls: those waiting are let go without running the crew.</summary>
        public void Close()
        {
            try
            {
                _closed.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Disposed with its host: nothing waits any more.
            }
        }

        public void Dispose()
        {
            _free.Dispose();
            _closed.Dispose();
        }
    }

    private enum Admission
    {
        Entered,
        Busy,
        Closed,
    }

    /// <summary>
    /// How many calls of one crew may wait while it runs: a call beyond is answered an error result at once, so a
    /// client cannot queue paid runs without end (a call of a crew holds no slot of the server).
    /// </summary>
    internal const int MaxWaitingCalls = 4;

    /// <summary>
    /// The history record of one call, when the host's history is on: the run's identity opened before <c>run.started</c> (the
    /// kickoff adopts it, so the history, the checkpoints and the output name the run by one id), a publisher over the
    /// history's sink and the observer that turns the tasks' hooks into events: what <c>orkeon-host</c> records a run with.
    /// </summary>
    private sealed class CallRecording : IDisposable
    {
        private readonly IDisposable _identity;
        private readonly RunEventPublisher _publisher;
        private readonly long _started = Stopwatch.GetTimestamp();

        private CallRecording(IDisposable identity, RunEventPublisher publisher, ExecutionId id)
        {
            _identity = identity;
            _publisher = publisher;
            Id = id;
            Observer = new RunEventObserver(publisher, inner: null, stream: false);
        }

        public ExecutionId Id { get; }

        public ICrewExecutionHook Observer { get; }

        public bool Succeeded { get; set; }

        public bool Cancelled { get; set; }

        /// <summary>The recording of a call on <paramref name="host"/>, or null when its history is off.</summary>
        public static CallRecording? Begin(IHost host, string crewName)
        {
            if (host.Services.GetService<ConfiguredRunHistory>() is not { } history)
                return null;

            var runScope = host.Services.GetRequiredService<IRunScope>();
            var id = ExecutionId.New();
            var identity = runScope.Enter(new RunScope(id, crewName));
            var publisher = new RunEventPublisher([history.Sink], TimeProvider.System, runScope, id.ToString());
            publisher.Emit(RunEventKinds.RunStarted, new { target = crewName, crewKey = crewName, stream = false, via = "mcp" });
            return new CallRecording(identity, publisher, id);
        }

        public void Dispose()
        {
            _publisher.Emit(RunEventKinds.RunFinished, new
            {
                success = Succeeded,
                exitCode = ExitCode(),
                durationMs = (long)Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
            });
            _identity.Dispose();
        }

        private int ExitCode()
        {
            if (Succeeded)
                return 0;
            return Cancelled ? 130 : 1;
        }
    }

    private sealed class McpCrewOptions : RunnerOptionsBase;

    /// <summary>What a call reports: the tasks that finished, out of the crew's tasks.</summary>
    private sealed class RunProgress(IMcpProgress progress, int total, ICrewExecutionHook? recorder)
    {
        public ICrewExecutionHook? Recorder => recorder;

        private int _done;

        public void TaskFinished(string role, bool success)
        {
            var done = Interlocked.Increment(ref _done);
            if (done <= total)
                progress.Report(done, total, $"The {role} agent {(success ? "finished" : "failed")} a task ({done}/{total}).");
        }
    }

    /// <summary>Wraps the crew's hook: every callback goes to it, and a finished task is reported to the call in flight.</summary>
    private sealed class ProgressHook(ICrewExecutionHook? inner) : ICrewExecutionHook
    {
        public async System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct)
        {
            if (s_current.Value?.Recorder is { } recorder)
                await recorder.OnTaskStartedAsync(snapshot, ct).ConfigureAwait(false);
            if (inner is not null)
                await inner.OnTaskStartedAsync(snapshot, ct).ConfigureAwait(false);
        }

        public async System.Threading.Tasks.Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
        {
            if (s_current.Value?.Recorder is { } recorder)
                await recorder.OnTaskCompletedAsync(snapshot, ct).ConfigureAwait(false);
            s_current.Value?.TaskFinished(snapshot.AgentRole, snapshot.Success);
            if (inner is not null)
                await inner.OnTaskCompletedAsync(snapshot, ct).ConfigureAwait(false);
        }

        public async System.Threading.Tasks.Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct)
        {
            if (s_current.Value?.Recorder is { } recorder)
                await recorder.OnCrewCompletedAsync(snapshot, ct).ConfigureAwait(false);
            if (inner is not null)
                await inner.OnCrewCompletedAsync(snapshot, ct).ConfigureAwait(false);
        }

        public async System.Threading.Tasks.Task OnCrewFailedAsync(CrewExecutionSnapshot isPartial, Exception? ex, CancellationToken ct)
        {
            if (s_current.Value?.Recorder is { } recorder)
                await recorder.OnCrewFailedAsync(isPartial, ex, ct).ConfigureAwait(false);
            if (inner is not null)
                await inner.OnCrewFailedAsync(isPartial, ex, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens every crew of <paramref name="crewPaths"/>. Null when one cannot be served, the reason on the console's
    /// error output: a guard that refused the crew's host, a crew that does not load, or two crews that give one tool name.
    /// </summary>
    /// <param name="crewPaths">The crews the operator named.</param>
    /// <param name="options">How their hosts are built.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The host of the crews' tools, or null.</returns>
    internal static async Task<McpCrewToolHost?> OpenAsync(IReadOnlyList<string> crewPaths, McpCrewHostOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(crewPaths);
        ArgumentNullException.ThrowIfNull(options);
        var served = new List<ServedCrew>();
        var error = options.Console.ErrorOutput;
        var cwd = Path.GetFullPath(options.WorkingDirectory ?? Directory.GetCurrentDirectory());
        try
        {
            foreach (var path in crewPaths)
            {
                var full = Path.GetFullPath(path, cwd);
                var crew = await OpenOneAsync(full, options, cwd, ct).ConfigureAwait(false);
                if (crew is null)
                {
                    DisposeAll(served);
                    return null;
                }

                served.Add(crew);
                if (served.Count(c => c.ToolName == crew.ToolName) > 1)
                {
                    await error.WriteLineAsync(
                        $"orkeon mcp serve: two crews give the tool name '{crew.ToolName}' ('{crew.Name}' in {full}): give one of them another name.").ConfigureAwait(false);
                    DisposeAll(served);
                    return null;
                }
            }

            return new McpCrewToolHost(served);
        }
        catch
        {
            DisposeAll(served);
            throw;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The entry point is handed to the ServedCrew that owns it, or disposed on every refusal and failure path.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308", Justification = "The slug is a tool name: lower case is its spelling, not a normalization for comparison.")]
    private static async Task<ServedCrew?> OpenOneAsync(string fullPath, McpCrewHostOptions options, string cwd, CancellationToken ct)
    {
        var error = options.Console.ErrorOutput;
        var crewOptions = new McpCrewOptions
        {
            ConfigPath = fullPath,
            SettingsPath = options.SettingsPath,
            AllowExternalMounts = options.AllowExternalMounts,
            RunnerConsole = options.Console,
            WorkingDirectory = cwd,
        };

        // Every log goes to stderr: stdout is the protocol channel, and the host's default logging writes to it.
        if (!CrewRunEntryPoint.TryOpen(
                crewOptions,
                "Orkeon.McpServe",
                (context, services) =>
                {
                    options.ConfigureServices?.Invoke(context, services);
                    ObserveTasks(services);
                },
                out var entry,
                out _,
                (_, logging) => RunnerExecution.ConfigureStderrOnlyLogging(logging)))
        {
            return null;
        }

        try
        {
            var (described, loadError) = await entry!.InspectAsync(DescribeAsync, ct).ConfigureAwait(false);
            if (described is null)
            {
                await error.WriteLineAsync($"orkeon mcp serve: the crew {fullPath} does not load: {loadError?.Message}").ConfigureAwait(false);
                entry.Dispose();
                return null;
            }

            var (name, goal, tasks) = described;
            // The loader requires a name: it is the tool's, lower case, every run of other characters one underscore.
            var slug = NonSlugPattern().Replace((name ?? string.Empty).ToLowerInvariant(), "_").Trim('_');
            if (slug.Length == 0)
                slug = "crew";

            var placeholders = Distinct(tasks.SelectMany(t => t.Placeholders));
            return new ServedCrew(slug, name ?? slug, goal, tasks, placeholders, entry);
        }
        catch
        {
            entry!.Dispose();
            throw;
        }
    }

    private static async Task<CrewDescription> DescribeAsync(
        Domain.Crew.Crew crew, IServiceProvider services, CancellationToken ct)
    {
        var found = await services.GetRequiredService<ITaskRepository>().GetByIdsAsync(crew.Tasks, ct).ConfigureAwait(false);
        var tasks = new List<TaskText>();
        foreach (var id in crew.Tasks)
        {
            if (found.FirstOrDefault(t => t.Id.Equals(id)) is not { } task)
                continue;
            var description = task.Description.Value;
            var expected = task.ExpectedOutput.Value ?? string.Empty;
            tasks.Add(new TaskText(description, expected, Distinct(PlaceholdersIn(description).Concat(PlaceholdersIn(expected)))));
        }

        return new CrewDescription(crew.Name, crew.Goal.Value, tasks);
    }

    /// <summary>Puts the progress hook on the crew's hook: what <c>AutoSummaryWriter</c> registered stays inside it.</summary>
    private static void ObserveTasks(IServiceCollection services)
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(ICrewExecutionHook));
        if (existing is not null)
            services.Remove(existing);

        services.AddScoped<ICrewExecutionHook>(sp => new ProgressHook(existing is null ? null : Materialize(sp, existing)));
    }

    private static ICrewExecutionHook? Materialize(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.IsKeyedService)
            return null;
        if (descriptor.ImplementationInstance is ICrewExecutionHook instance)
            return instance;
        if (descriptor.ImplementationFactory is { } factory)
            return (ICrewExecutionHook?)factory(sp);
        return descriptor.ImplementationType is { } type
            ? (ICrewExecutionHook)ActivatorUtilities.CreateInstance(sp, type)
            : null;
    }

    private static IEnumerable<string> PlaceholdersIn(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value);

    /// <summary>The names without repeats; interpolation ignores case, so <c>{Topic}</c> and <c>{topic}</c> are one, as first spelled.</summary>
    private static List<string> Distinct(IEnumerable<string> names) =>
        [.. names.DistinctBy(n => n, StringComparer.OrdinalIgnoreCase)];

    private static McpToolDefinition ToolOf(ServedCrew crew) => new()
    {
        Name = crew.ToolName,
        Description = $"Runs the crew '{crew.Name}' ({crew.Tasks.Count} task(s)) and returns its final output. Goal: {crew.Goal}",
        InputSchema = SchemaOf(crew.Placeholders, "Value for {{{0}}} in the text of the tasks."),
    };

    private static JsonElement SchemaOf(IReadOnlyList<string> names, string descriptionFormat) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = names.ToDictionary(
                n => n,
                n => (object)new Dictionary<string, string>
                {
                    ["type"] = "string",
                    ["description"] = string.Format(CultureInfo.InvariantCulture, descriptionFormat, n),
                },
                StringComparer.Ordinal),
            ["required"] = names,
            ["additionalProperties"] = false,
        });

    private static IEnumerable<McpPrompt> PromptsOf(ServedCrew crew) =>
        crew.Tasks.Select((task, i) => new McpPrompt
        {
            Name = $"{crew.Slug}_task_{i + 1}",
            Description = $"Task {i + 1} of the crew '{crew.Name}', its placeholders filled in.",
            Arguments = [.. task.Placeholders.Select(n => new McpPromptArgument
            {
                Name = n,
                Description = $"Value for {{{n}}} in the text of the task.",
                Required = true,
            })],
        });

    /// <summary>Gets how many calls of the tool <paramref name="toolName"/> wait for the crew to be free.</summary>
    /// <param name="toolName">The tool's name.</param>
    internal int WaitingCalls(string toolName) => _crews.FirstOrDefault(c => c.ToolName == toolName)?.Gate.Waiting ?? 0;

    /// <inheritdoc />
    public bool OwnsTool(string name) => _crews.Any(c => c.ToolName == name);

    /// <inheritdoc />
    public void StopAdmitting()
    {
        foreach (var crew in _crews)
            crew.Gate.Close();
    }

    /// <inheritdoc />
    public async Task<McpToolCallResult> CallToolAsync(string name, JsonElement? arguments, IMcpProgress progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var crew = _crews.FirstOrDefault(c => c.ToolName == name)
            ?? throw new McpRequestException($"Tool not found: {name}");
        var variables = ReadVariables(crew.Placeholders, arguments, $"the crew '{crew.Name}'");

        switch (await crew.Gate.EnterAsync(ct).ConfigureAwait(false))
        {
            case Admission.Busy:
                return Text($"The crew '{crew.Name}' is busy: {MaxWaitingCalls} calls already wait for it. Try again later.", isError: true);
            case Admission.Closed:
                return Text($"The server is closing: the crew '{crew.Name}' was not run.", isError: true);
        }

        try
        {
            return await RunCrewAsync(crew, variables, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            crew.Gate.Leave();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A crew that fails to run is the tool's result, an error the client reads, never a fault of the server.")]
    private static async Task<McpToolCallResult> RunCrewAsync(
        ServedCrew crew, Dictionary<string, string> variables, IMcpProgress progress, CancellationToken ct)
    {
        using var recording = CallRecording.Begin(crew.Entry.Host, crew.Name);
        s_current.Value = new RunProgress(progress, crew.Tasks.Count, recording?.Observer);
        try
        {
            var run = await crew.Entry.RunAsync(
                (_, _) => System.Threading.Tasks.Task.FromResult<Orkeon.Application.Interfaces.Services.CrewInput?>(
                    Orkeon.Application.Interfaces.Services.CrewInput.WithStringVariables(null, variables)),
                resume: null,
                ct).ConfigureAwait(false);

            // A crew whose agent was cancelled answers a failed output, not an exception: the client that cancelled gets no answer.
            ct.ThrowIfCancellationRequested();
            if (recording is not null)
                recording.Succeeded = run.Output is { Succeeded: true };

            // A failure's own text (an exception message, a task error) may carry a path or a secret of the run: it goes
            // to the log, and the client reads that it failed and where the details are.
            switch (run.Outcome)
            {
                case CrewRunOutcome.Completed when run.Output is { Succeeded: true } output:
                    return Text(output.FinalOutput, isError: false);
                case CrewRunOutcome.Completed:
                    LogCrewOutputFailed(LoggerOf(crew), crew.Name, run.Output?.Error ?? run.Output?.FinalOutput ?? "no error text");
                    return Text(CrewFailedText, isError: true);
                case CrewRunOutcome.LoadFailed:
                    LogCrewLoadFailed(LoggerOf(crew), run.LoadError, crew.Name);
                    return Text("The crew no longer loads. The details are in the log of the server.", isError: true);
                default:
                    return Text(run.Refusal ?? "The crew did not run.", isError: true);
            }
        }
        catch (OperationCanceledException)
        {
            if (recording is not null)
                recording.Cancelled = true;
            throw;
        }
        catch (Exception ex)
        {
            // The details go to the log of the crew's host: a message may carry a path or a secret of the run.
            LogCrewFailed(LoggerOf(crew), ex, crew.Name);
            return Text(CrewFailedText, isError: true);
        }
        finally
        {
            s_current.Value = null;
        }
    }

    /// <inheritdoc />
    public Task<McpPromptGetResult> GetPromptAsync(string name, JsonElement? arguments, CancellationToken ct)
    {
        foreach (var crew in _crews)
        {
            for (var i = 0; i < crew.Tasks.Count; i++)
            {
                if ($"{crew.Slug}_task_{i + 1}" != name)
                    continue;

                var task = crew.Tasks[i];
                var variables = ReadVariables(task.Placeholders, arguments, $"the prompt '{name}'");
                var text = new StringBuilder(TaskTextInterpolation.Interpolate(task.Description, variables));
                if (task.ExpectedOutput.Length > 0)
                    text.Append("\n\nExpected output: ").Append(TaskTextInterpolation.Interpolate(task.ExpectedOutput, variables));

                return Task.FromResult(new McpPromptGetResult
                {
                    Description = $"Task {i + 1} of the crew '{crew.Name}'.",
                    Messages = [new McpPromptMessage { Role = "user", Content = new McpContent { Text = text.ToString() } }],
                });
            }
        }

        throw new McpRequestException($"Prompt not found: {name}");
    }

    /// <summary>
    /// The placeholders' values from the client's arguments: a name that is none is refused, so is a value that is neither a
    /// string, a number nor a boolean, and so is a placeholder left without a value (it would reach the model as <c>{name}</c>).
    /// </summary>
    private static Dictionary<string, string> ReadVariables(IReadOnlyList<string> placeholders, JsonElement? arguments, string subject)
    {
        var given = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (arguments is { ValueKind: JsonValueKind.Object } given0)
        {
            var unknown = new List<string>();
            foreach (var property in given0.EnumerateObject())
            {
                var known = placeholders.FirstOrDefault(p => string.Equals(p, property.Name, StringComparison.OrdinalIgnoreCase));
                if (known is null)
                {
                    unknown.Add(property.Name);
                    continue;
                }

                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        given[known] = property.Value.GetString()!;
                        break;
                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        given[known] = property.Value.GetRawText();
                        break;
                    default:
                        throw new McpRequestException($"The argument '{property.Name}' of {subject} must be a string, a number or a boolean.");
                }
            }

            if (unknown.Count > 0)
                throw new McpRequestException($"{subject} takes no argument named {string.Join(", ", unknown.Select(u => $"'{u}'"))}: it takes {Describe(placeholders)}.");
        }

        var missing = placeholders.Where(p => !given.ContainsKey(p)).ToList();
        if (missing.Count > 0)
            throw new McpRequestException($"Missing {(missing.Count == 1 ? "argument" : "arguments")} for {subject}: {string.Join(", ", missing.Select(m => $"'{m}'"))}.");

        return given;
    }

    private static string Describe(IReadOnlyList<string> placeholders) =>
        placeholders.Count == 0 ? "none" : string.Join(", ", placeholders.Select(p => $"'{p}'"));

    private static McpToolCallResult Text(string text, bool isError) => new()
    {
        Content = [new McpContent { Type = "text", Text = text }],
        IsError = isError,
    };

    private static void DisposeAll(IEnumerable<ServedCrew> crews)
    {
        foreach (var crew in crews)
        {
            crew.Entry.Dispose();
            crew.Gate.Dispose();
        }
    }

    private const string CrewFailedText = "The crew failed. The details are in the log of the server.";

    private static ILogger LoggerOf(ServedCrew crew) =>
        (ILogger?)crew.Entry.Host.Services.GetService<ILoggerFactory>()?.CreateLogger<McpCrewToolHost>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    [LoggerMessage(Level = LogLevel.Error, Message = "The crew '{Crew}' failed during an MCP call")]
    private static partial void LogCrewFailed(ILogger logger, Exception ex, string crew);

    [LoggerMessage(Level = LogLevel.Error, Message = "The crew '{Crew}' failed during an MCP call: {Error}")]
    private static partial void LogCrewOutputFailed(ILogger logger, string crew, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The crew '{Crew}' no longer loads during an MCP call")]
    private static partial void LogCrewLoadFailed(ILogger logger, Exception? ex, string crew);

    /// <summary>Disposes the host of every crew.</summary>
    public void Dispose() => DisposeAll(_crews);
}
