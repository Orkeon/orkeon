using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools;
using Orkeon.Scripting;
using Orkeon.Scripting.Configuration;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>Extracts the embedded assistant pack into the session (SPEC-ORKEON-FORGE §7.5).</summary>
internal static class ForgePack
{
    /// <summary>The assistant crew's file name, inside the pack directory.</summary>
    public const string AssistantFileName = "forge-assistant.ork.js";

    /// <summary>The pack directory inside a session.</summary>
    public const string PackDirectoryName = "pack";

    /// <summary>
    /// Materializes the pack under <c>&lt;session&gt;/pack/</c> and returns the assistant's
    /// physical path. <paramref name="overrideDirectory"/> (<c>--pack</c>) substitutes any
    /// file it holds for the embedded one — the extension point for a domain or a rewrite.
    /// </summary>
    public static string Ensure(string sessionDirectory, string? overrideDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);

        var packDirectory = Path.Combine(sessionDirectory, PackDirectoryName);
        Directory.CreateDirectory(packDirectory);
        var target = Path.Combine(packDirectory, AssistantFileName);

        var overridePath = overrideDirectory is null ? null : Path.Combine(overrideDirectory, AssistantFileName);
        if (overridePath is not null && File.Exists(overridePath))
        {
            File.Copy(overridePath, target, overwrite: true);
            return target;
        }

        var assembly = typeof(ForgePack).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(AssistantFileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The embedded pack '{AssistantFileName}' is missing from this build.");

        using var resource = assembly.GetManifestResourceStream(resourceName)!;
        using var file = File.Create(target);
        resource.CopyTo(file);
        return target;
    }
}

/// <summary>
/// The production <see cref="IForgeAssistant"/> (SPEC-ORKEON-FORGE §7.1): each turn runs
/// the pack crew once through <see cref="ScriptHost"/>, in-process. The script gets the
/// whole turn as <c>inputs</c> (phase, message, history, brief, errors, catalogue) and is
/// stateless; the transcript persisted here is what makes a resumed interview remember.
/// Submissions come back through the <see cref="ForgeSubmissionBox"/> the injected tools
/// write into — never through the script's return value, which is only conversation.
/// </summary>
internal sealed class ForgeCrewAssistant : IForgeAssistant
{
    /// <summary>Read-only tools offered to the assistant, spec §7.1 — grounding, never writing.</summary>
    private static readonly string[] ReadToolAllowlist = ["directory_read", "directory_search", "file_read"];

    private static readonly JsonSerializerOptions InputsOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ForgeSession _session;
    private readonly ForgeSubmissionBox _box;
    private readonly ForgeUsageTally _tally;
    private readonly ScriptHost _scriptHost;
    private readonly IReadOnlyList<string> _readTools;
    private readonly IReadOnlyList<ForgeToolInfo> _crewTools;
    private readonly string _packPhysicalPath;
    private readonly string _packVirtualPath;

    /// <summary>
    /// Builds the assistant over the engine host's services. The pack must already be
    /// materialized (<see cref="ForgePack.Ensure"/>) and the session directory mounted at
    /// <paramref name="sessionVirtualRoot"/>.
    /// </summary>
    public ForgeCrewAssistant(
        IServiceProvider services,
        ForgeSession session,
        string packPhysicalPath,
        string sessionVirtualRoot = "/forge")
    {
        ArgumentNullException.ThrowIfNull(services);
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _packPhysicalPath = packPhysicalPath ?? throw new ArgumentNullException(nameof(packPhysicalPath));
        _packVirtualPath = $"{sessionVirtualRoot}/{ForgePack.PackDirectoryName}/{ForgePack.AssistantFileName}";

        _box = services.GetRequiredService<ForgeSubmissionBox>();
        _tally = services.GetRequiredService<ForgeUsageTally>();

        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var tools = services.GetServices<IBaseTool>().ToList();
        _readTools = [.. ReadToolAllowlist.Where(name => tools.Any(t => t.Name == name))];
        // What the designed team may use (SPEC §7.3): the sandbox catalogue with
        // descriptions — a closed list the blueprint prompt shows and validation enforces.
        _crewTools = ForgeSandbox.SelectCrewTools(tools);

        var configuration = services.GetRequiredService<IConfiguration>();
        var engineFactory = new JsEngineFactory(
            limits: ResolveLimits(configuration),
            loggerFactory: loggerFactory,
            configuration: configuration,
            builtInTools: tools,
            llmProvider: services.GetService<ILlmProvider>(),
            permissionGate: services.GetService<IPermissionGate>(),
            deltaSink: services.GetService<ILlmDeltaSink>(),
            usageSink: services.GetService<ILlmUsageSink>());

        // The pack ships pre-built JS: pass-through, no esbuild required for the interview
        // (SPEC §7.1 — esbuild only matters to a `--format script` render).
        _scriptHost = new ScriptHost(
            services.GetRequiredService<IFileSystemService>(),
            PassThroughTranspiler.Instance,
            engineFactory,
            loggerFactory.CreateLogger<ScriptHost>());
    }

    /// <summary>
    /// The interview's default wall-clock ceiling per assistant turn, matching the
    /// <c>wallTime: 600</c> the pack declares for itself.
    /// </summary>
    private static readonly TimeSpan DefaultTurnTimeout = TimeSpan.FromSeconds(600);

    /// <summary>
    /// The engine limits for one assistant turn.
    /// <para>
    /// This used to be omitted entirely, which silently discarded
    /// <c>Orkeon:Scripting:Limits</c> and pinned every turn to the 30 s untrusted-script
    /// default. Jint's TimeoutInterval is WALL CLOCK, not JS CPU time: the stopwatch keeps
    /// running while the host awaits the LLM. So a single slow model reply, or one wasted
    /// round trip on a refused tool call, killed the session mid-interview with a bare
    /// "The operation has timed out." — and the pack's own 600 s budget was unreachable.
    /// </para>
    /// <para>
    /// A configured value always wins. Absence is read from the key itself rather than by
    /// comparing against <see cref="ScriptingLimitsOptions.ExecutionTimeout"/>'s default,
    /// which would be indistinguishable from someone deliberately asking for 30 s.
    /// </para>
    /// </summary>
    internal static ScriptingLimitsOptions ResolveLimits(IConfiguration configuration)
    {
        var limits = configuration.GetSection(ScriptingLimitsOptions.SectionName)
            .Get<ScriptingLimitsOptions>() ?? new ScriptingLimitsOptions();

        var configured = configuration[$"{ScriptingLimitsOptions.SectionName}:ExecutionTimeout"];
        return string.IsNullOrWhiteSpace(configured)
            ? limits with { ExecutionTimeout = DefaultTurnTimeout }
            : limits;
    }

    /// <inheritdoc />
    public async Task<ForgeAssistantReply> NextAsync(
        ForgeAssistantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.UserMessage is { Length: > 0 } userMessage)
            _session.AppendTranscript("user", userMessage);

        _box.Reset();
        var before = _tally.Snapshot;

        var result = await _scriptHost.RunFromFileAsync(
            _packPhysicalPath, _packVirtualPath, cancellationToken, BuildInputsJson(request))
            .ConfigureAwait(false);

        var usage = _tally.Snapshot.Since(before);
        var (briefJson, blueprintJson) = _box.Take();

        if (briefJson is not null)
            return new ForgeAssistantReply { BriefJson = briefJson, Usage = usage };
        if (blueprintJson is not null)
            return new ForgeAssistantReply { BlueprintJson = blueprintJson, Usage = usage };

        var message = ReadFinalOutput(result);
        if (message is { Length: > 0 })
            _session.AppendTranscript("assistant", message);

        return new ForgeAssistantReply { Message = message, Usage = usage };
    }

    private string BuildInputsJson(ForgeAssistantRequest request)
    {
        var history = _session.LoadTranscript()
            .Select(turn => new { role = turn.Role, text = turn.Text })
            .ToArray();

        var isBrief = request.Phase == ForgeAssistantPhase.Brief;
        return JsonSerializer.Serialize(new
        {
            phase = isBrief ? "brief" : "blueprint",
            language = LanguageOf(request),
            message = request.UserMessage,
            history,
            brief = request.Brief,
            previousBlueprint = request.PreviousBlueprint,
            errors = request.Errors,
            readTools = _readTools,
            crewTools = isBrief
                ? null
                : _crewTools.Select(tool => new { name = tool.Name, description = tool.Description }),
            submitTool = isBrief ? "brief_submit" : "blueprint_submit",
        }, InputsOptions);
    }

    private static string LanguageOf(ForgeAssistantRequest request) =>
        request.Brief?.Language ?? "fr";

    /// <summary>
    /// Reads <c>finalOutput</c> from <see cref="ScriptHost.RunFromFileAsync"/>'s handoff
    /// result (an anonymous object — reflection is the honest access, not a contract).
    /// </summary>
    private static string? ReadFinalOutput(object? result)
    {
        var text = result?.GetType()
            .GetProperty("finalOutput", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(result) as string;

        return string.IsNullOrWhiteSpace(text) || text == "(no aggregated output)" ? null : text;
    }
}
