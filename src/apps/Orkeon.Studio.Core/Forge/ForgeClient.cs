using Orkeon.Studio.Core.Events;
using System.Text.Json;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Forge;

/// <summary>What to launch: a new session from a need, or the resume of an existing one.</summary>
public sealed record ForgeStartRequest
{
    /// <summary>The problem in the user's words; null opens with the interview.</summary>
    public string? Need { get; init; }

    /// <summary>Slug of the session to resume; wins over <see cref="Need"/>.</summary>
    public string? ResumeSlug { get; init; }

    /// <summary>Workspace the session lives under (the CLI's working directory).</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Explicit settings path, same semantics as <c>orkeon run</c>.</summary>
    public string? SettingsPath { get; init; }

    /// <summary>Arbitrate without a human — Studio keeps the human, so false by default.</summary>
    public bool Auto { get; init; }

    /// <summary>
    /// <c>--dry</c>: generate and validate, then pause before the trial. The wizard's
    /// Composer step is exactly this boundary — the user reviews the team and decides to
    /// try it; a later resume (without dry) runs the trial.
    /// </summary>
    public bool Dry { get; init; }

    /// <summary>
    /// Environment variables added to the engine child — how Studio's assistant profile
    /// reaches the engine (<c>ORKEON_Llm__Model</c>/<c>__BaseUrl</c>): the engine reads the
    /// same settings file as every run, and these overrides sit on top, never inside it.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentOverrides { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The amended blueprint to apply at the dry pause (v3 W-10): the argv gains
    /// <c>--edit</c> and the JSON travels as the child's first stdin line — the engine
    /// validates it in full, re-renders deterministically, and with <see cref="Dry"/>
    /// pauses again at the same boundary. Only meaningful with <see cref="ResumeSlug"/>.
    /// </summary>
    public string? EditedBlueprintJson { get; init; }

    /// <summary>
    /// <c>--adopt</c>: take the team as generated, without running a trial. Answers the same
    /// dry pause as <see cref="EditedBlueprintJson"/>, and is offline and instantaneous —
    /// the engine moves the session to Ready and exits. Only meaningful with
    /// <see cref="ResumeSlug"/>.
    /// </summary>
    public bool Adopt { get; init; }
}

/// <summary>
/// The <c>orkeon forge</c> argv, composed against the CLI's §5.1 grammar. Mirrors the
/// grammar the same way <c>RunArgumentsBuilder</c> mirrors <c>orkeon run</c> — the drift
/// pin is <c>ForgeClientTests</c>' golden argv.
/// </summary>
public static class ForgeArgumentsBuilder
{
    /// <summary>The CLI verb.</summary>
    public const string ForgeVerb = "forge";

    /// <summary>
    /// Builds the argv of <c>forge promote</c>: destination required, schedule passed
    /// through verbatim — the engine owns the grammar and refuses loudly, the client never
    /// pre-validates what it does not own.
    /// </summary>
    public static IReadOnlyList<string> BuildPromote(string slug, string destination, string? schedule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var arguments = new List<string> { ForgeVerb, "promote", slug, "--to", destination, "--events", "jsonl" };
        if (!string.IsNullOrWhiteSpace(schedule))
        {
            arguments.Add("--schedule");
            arguments.Add(schedule);
        }

        return arguments;
    }

    /// <summary>Builds the argv of <paramref name="request"/>, <c>--events jsonl</c> always on.</summary>
    public static IReadOnlyList<string> Build(ForgeStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var arguments = new List<string> { ForgeVerb };

        if (!string.IsNullOrWhiteSpace(request.ResumeSlug))
        {
            arguments.Add("resume");
            arguments.Add(request.ResumeSlug);
        }
        else if (!string.IsNullOrWhiteSpace(request.Need))
        {
            arguments.Add(request.Need);
        }

        arguments.Add("--events");
        arguments.Add("jsonl");

        if (!string.IsNullOrWhiteSpace(request.SettingsPath))
        {
            arguments.Add("--settings");
            arguments.Add(request.SettingsPath);
        }

        if (request.Auto)
            arguments.Add("--auto");

        if (request.Dry)
            arguments.Add("--dry");

        if (!string.IsNullOrWhiteSpace(request.EditedBlueprintJson))
            arguments.Add("--edit");

        if (request.Adopt)
            arguments.Add("--adopt");

        return arguments;
    }
}

/// <summary>
/// The Studio side of the forge protocol (SPEC-ORKEON-FORGE §5-§6): launches
/// <c>orkeon forge --events jsonl</c> as a child process, parses its stdout into
/// <see cref="OrkeonEvent"/>s, and answers over stdin — <c>user.message</c> and
/// <c>decision.made</c>, one JSON line each. The Studio process never touches an LLM;
/// it only ever sees these lines.
/// </summary>
public sealed class ForgeClient
{
    private readonly IProcessLauncher _launcher;
    private readonly OrkeonBinaryLocator _locator;
    private volatile IProcessInputWriter? _input;
    private CancellationTokenSource? _cancellation;

    /// <summary>Creates a client over an explicit launcher and locator (the test seam).</summary>
    public ForgeClient(IProcessLauncher launcher, OrkeonBinaryLocator locator)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
    }

    /// <summary>Creates a client over the real machine and real processes.</summary>
    public static ForgeClient ForCurrentMachine() =>
        new(SystemProcessLauncher.Instance, OrkeonBinaryLocator.ForCurrentMachine());

    /// <summary>Whether a forge child is currently alive.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Runs one engine invocation to completion. Protocol lines reach
    /// <paramref name="onEvent"/>; everything else — stderr, unparsable stdout — reaches
    /// <paramref name="onRaw"/>, never dropped (the terminal's own rule: what the stream
    /// said stays visible). Callbacks arrive serialized, on a background thread.
    /// </summary>
    public async Task<ProcessRunResult> RunAsync(
        ForgeStartRequest request,
        Action<OrkeonEvent> onEvent,
        Action<ProcessOutputLine>? onRaw = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onEvent);
        if (IsRunning)
            throw new InvalidOperationException("A forge session is already running.");

        var location = _locator.Locate();
        if (!location.Found)
            return ProcessRunResult.NotStarted(location.Error ?? $"`{OrkeonBinaryLocator.ExecutableBaseName}` was not found.");

        // The dry-pause edit rides the launch itself: the engine's `--edit` reads the
        // amended blueprint as its first inbound line, so it is queued on stdin before
        // any event comes back — no race with the reader.
        string? blueprintLine = null;
        if (!string.IsNullOrWhiteSpace(request.EditedBlueprintJson))
        {
            blueprintLine = TryBuildBlueprintLine(request.EditedBlueprintJson);
            if (blueprintLine is null)
                return ProcessRunResult.NotStarted("The amended blueprint is not a JSON object.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = linked;
        IsRunning = true;
        try
        {
            var launch = new ProcessLaunchRequest
            {
                FileName = location.Path!,
                Arguments = ForgeArgumentsBuilder.Build(request),
                WorkingDirectory = request.WorkingDirectory,
                Environment = request.EnvironmentOverrides,
                OnInputReady = writer =>
                {
                    _input = writer;
                    if (blueprintLine is not null)
                        writer.TryWriteLine(blueprintLine);
                },
            };

            return await _launcher.RunAsync(
                launch,
                line =>
                {
                    if (line.Channel == ProcessOutputChannel.StandardOutput
                        && OrkeonEventParser.TryParse(line.Text, out var orkeonEvent))
                    {
                        onEvent(orkeonEvent!);
                    }
                    else
                    {
                        onRaw?.Invoke(line);
                    }
                },
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            IsRunning = false;
            _input = null;
            _cancellation = null;
        }
    }

    /// <summary>
    /// Promotes a Ready session to <paramref name="destination"/> — the "Adopter" card's
    /// gesture. Same child process, same protocol; the <c>promoted</c> event carries the
    /// folder, the launcher and the displayed-never-executed install command.
    /// </summary>
    public async Task<ProcessRunResult> PromoteAsync(
        string slug,
        string destination,
        string? schedule,
        string? workingDirectory,
        Action<OrkeonEvent> onEvent,
        Action<ProcessOutputLine>? onRaw = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onEvent);
        if (IsRunning)
            throw new InvalidOperationException("A forge session is already running.");

        var location = _locator.Locate();
        if (!location.Found)
            return ProcessRunResult.NotStarted(location.Error ?? $"`{OrkeonBinaryLocator.ExecutableBaseName}` was not found.");

        IsRunning = true;
        try
        {
            var launch = new ProcessLaunchRequest
            {
                FileName = location.Path!,
                Arguments = ForgeArgumentsBuilder.BuildPromote(slug, destination, schedule),
                WorkingDirectory = workingDirectory,
            };

            return await _launcher.RunAsync(
                launch,
                line =>
                {
                    if (line.Channel == ProcessOutputChannel.StandardOutput
                        && OrkeonEventParser.TryParse(line.Text, out var orkeonEvent))
                    {
                        onEvent(orkeonEvent!);
                    }
                    else
                    {
                        onRaw?.Invoke(line);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Sends the user's next conversation turn; false when no child is listening.</summary>
    public bool SendMessage(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return WriteLine(new { kind = ForgeEventKinds.UserMessage, text });
    }

    /// <summary>Sends an arbitration (<c>accept</c>/<c>refine</c>/<c>edit</c>/<c>abort</c>); false when no child is listening.</summary>
    public bool SendDecision(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return WriteLine(new { kind = ForgeEventKinds.DecisionMade, value });
    }

    /// <summary>
    /// Sends the amended blueprint that must follow <c>SendDecision("edit")</c>. The JSON
    /// travels as an object, not a string — the engine re-validates it in full, so this
    /// only refuses what could never be a document at all. False when no child listens.
    /// </summary>
    public bool SendBlueprint(string blueprintJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintJson);
        return TryBuildBlueprintLine(blueprintJson) is { } line
            && _input is { } writer && writer.TryWriteLine(line);
    }

    /// <summary>The <c>blueprint.edited</c> stdin line, or null when the JSON could never be a document.</summary>
    private static string? TryBuildBlueprintLine(string blueprintJson)
    {
        try
        {
            var blueprint = JsonSerializer.Deserialize<JsonElement>(blueprintJson);
            return blueprint.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Serialize(new { kind = ForgeEventKinds.BlueprintEdited, blueprint })
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Asks the running child to stop; false when nothing runs or it was already asked.</summary>
    public bool RequestCancellation()
    {
        var cancellation = _cancellation;
        if (cancellation is null)
            return false;

        try
        {
            if (cancellation.IsCancellationRequested)
                return false;

            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The run finished between the read and the cancel; nothing left to stop.
            return false;
        }
    }

    private bool WriteLine(object payload) =>
        _input is { } writer && writer.TryWriteLine(JsonSerializer.Serialize(payload));
}
