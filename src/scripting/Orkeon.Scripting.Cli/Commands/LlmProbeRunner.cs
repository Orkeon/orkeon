using System.Diagnostics;
using System.Globalization;
using System.Text;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// One mode of the LLM provider test protocol (matrix §5).
/// </summary>
/// <remarks>
/// The identifiers are the matrix's own, so a probe result can be transcribed into it without
/// a translation step — the whole point of the exercise is to know what was <em>really</em>
/// exercised, and an approximate mapping would defeat that.
/// </remarks>
internal enum LlmProbeMode
{
    /// <summary>M1 — a single prompt returns non-empty content.</summary>
    M1,

    /// <summary>M2 — a multi-turn conversation with a system message is honoured.</summary>
    M2,

    /// <summary>M3 — single-prompt token streaming yields more than one chunk.</summary>
    M3,

    /// <summary>M4 — chat streaming yields typed events and a terminal Completed.</summary>
    M4,

    /// <summary>M7 — a thinking configuration is accepted and, where applicable, traced.</summary>
    M7,

    /// <summary>M8 — a response-format constraint yields parseable JSON.</summary>
    M8,

    /// <summary>M12 — an invalid model produces a typed error, not an exception.</summary>
    M12,

    /// <summary>M13 — a cancelled call stops promptly without throwing to the caller.</summary>
    M13,
}

/// <summary>Outcome of one probe mode.</summary>
/// <param name="Mode">The protocol mode exercised.</param>
/// <param name="Passed">Whether the provider behaved as the mode requires.</param>
/// <param name="Detail">What was observed — recorded whether it passed or not.</param>
/// <param name="ElapsedMs">Wall-clock duration, useful when comparing providers.</param>
internal sealed record LlmProbeResult(LlmProbeMode Mode, bool Passed, string Detail, long ElapsedMs);

/// <summary>
/// Runs the protocol modes against a live provider.
/// </summary>
/// <remarks>
/// <para>
/// Roughly 400 unit tests cover the LLM providers and every one of them speaks to a mocked
/// HTTP handler. A mock proves the framework sends what we believe it sends; it cannot prove
/// the vendor accepts it. This runner produces that second proof.
/// </para>
/// <para>
/// It never sees an API key as a value it could archive: the key is read from the environment
/// by the caller and lives only in the <see cref="LlmConfig"/> passed in. Nothing this class
/// returns contains credentials.
/// </para>
/// </remarks>
internal sealed class LlmProbeRunner
{
    private const string JsonPrompt = "Reply with a json object containing a single key \"ok\" set to true.";

    private readonly ILlmProvider _provider;

    /// <summary>Initializes a new runner over an already-configured provider.</summary>
    /// <param name="provider">The provider under test.</param>
    public LlmProbeRunner(ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <summary>Modes this harness can exercise without extra assets or a paid long-context call.</summary>
    public static IReadOnlyList<LlmProbeMode> SupportedModes { get; } =
    [
        LlmProbeMode.M1, LlmProbeMode.M2, LlmProbeMode.M3, LlmProbeMode.M4,
        LlmProbeMode.M7, LlmProbeMode.M8, LlmProbeMode.M12, LlmProbeMode.M13,
    ];

    /// <summary>Runs the requested modes in order and returns one result per mode.</summary>
    /// <param name="config">The effective configuration (model, key, base URL).</param>
    /// <param name="modes">The modes to exercise.</param>
    /// <param name="cancellationToken">Cancels the whole campaign.</param>
    /// <returns>One result per requested mode, in the order requested.</returns>
    public async Task<IReadOnlyList<LlmProbeResult>> RunAsync(
        LlmConfig config, IEnumerable<LlmProbeMode> modes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(modes);

        var results = new List<LlmProbeResult>();
        foreach (var mode in modes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunOneAsync(mode, config, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<LlmProbeResult> RunOneAsync(
        LlmProbeMode mode, LlmConfig config, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (passed, detail) = mode switch
            {
                LlmProbeMode.M1 => await ProbeSinglePromptAsync(config, cancellationToken).ConfigureAwait(false),
                LlmProbeMode.M2 => await ProbeMultiTurnAsync(config, cancellationToken).ConfigureAwait(false),
                LlmProbeMode.M3 => await ProbeTextStreamingAsync(config, cancellationToken).ConfigureAwait(false),
                LlmProbeMode.M4 => await ProbeChatStreamingAsync(config, cancellationToken).ConfigureAwait(false),
                LlmProbeMode.M7 => await ProbeThinkingAsync(config, cancellationToken).ConfigureAwait(false),
                LlmProbeMode.M8 => await ProbeResponseFormatAsync(config, cancellationToken).ConfigureAwait(false),
                LlmProbeMode.M12 => await ProbeErrorHandlingAsync(config, cancellationToken).ConfigureAwait(false),
                LlmProbeMode.M13 => await ProbeCancellationAsync(config, cancellationToken).ConfigureAwait(false),
                _ => (false, $"mode {mode} is not implemented by this harness"),
            };

            return new LlmProbeResult(mode, passed, detail, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A probe reports every failure as a failed mode; one bad mode must not abort the campaign.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // An exception is itself a finding: the framework's contract is to return typed
            // error responses, not to throw.
            return new LlmProbeResult(
                mode, false, $"threw {ex.GetType().Name}: {ex.Message}", stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<(bool Passed, string Detail)> ProbeSinglePromptAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        var response = await _provider.GenerateAsync("Say hello in one short sentence.", config, cancellationToken)
            .ConfigureAwait(false);

        return Describe(response, r => !string.IsNullOrWhiteSpace(r.Content));
    }

    private async Task<(bool Passed, string Detail)> ProbeMultiTurnAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        // The system message asks for a marker so the answer proves the turn was honoured
        // rather than merely produced.
        var withSystem = config with { SystemMessage = "Always end your reply with the token ORKEON_OK." };
        LlmMessage[] messages =
        [
            LlmMessage.User("What is 2 + 2?"),
            LlmMessage.Assistant("4"),
            LlmMessage.User("And multiplied by 3?"),
        ];

        var response = await _provider.ChatAsync(messages, withSystem, cancellationToken).ConfigureAwait(false);

        var honoured = response.Content.Contains("ORKEON_OK", StringComparison.Ordinal);
        return Describe(
            response,
            _ => honoured,
            honoured ? "system message honoured" : "system message absent from the reply");
    }

    private async Task<(bool Passed, string Detail)> ProbeTextStreamingAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (_provider is not IStreamingLlmProvider streaming)
            return (false, "provider does not implement IStreamingLlmProvider");

        var chunks = 0;
        var text = new StringBuilder();
        await foreach (var token in streaming
            .GenerateStreamingAsync("Count from one to five.", config, cancellationToken)
            .ConfigureAwait(false))
        {
            chunks++;
            text.Append(token);
        }

        return (chunks > 1, $"{chunks} chunk(s), {text.Length} char(s)");
    }

    private async Task<(bool Passed, string Detail)> ProbeChatStreamingAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (_provider is not IStreamingLlmProvider streaming)
            return (false, "provider does not implement IStreamingLlmProvider");

        var deltas = 0;
        LlmResponse? final = null;
        await foreach (var ev in streaming.ChatStreamingAsync(
            [LlmMessage.User("Count from one to five.")], config, cancellationToken).ConfigureAwait(false))
        {
            if (ev.Kind == LlmStreamEventKind.ContentDelta)
                deltas++;
            if (ev.Kind == LlmStreamEventKind.Completed)
                final = ev.FinalResponse;
        }

        var passed = final is not null && !string.IsNullOrWhiteSpace(final.Content);
        return (passed, $"{deltas} content delta(s), completed={final is not null}, tokens={final?.TokensUsed}");
    }

    private async Task<(bool Passed, string Detail)> ProbeThinkingAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (_provider.Capabilities.Thinking == ThinkingSupport.None)
            return (true, "not applicable: the provider declares no thinking capability");

        var withThinking = config with { Thinking = new LlmThinkingConfig { Effort = "low" } };
        var response = await _provider.GenerateAsync(
            "Briefly, why is the sky blue?", withThinking, cancellationToken).ConfigureAwait(false);

        var traced = response.Metadata.ContainsKey("reasoning_content");
        return Describe(
            response,
            r => !string.IsNullOrWhiteSpace(r.Content),
            traced ? "reasoning trace returned" : "accepted, no reasoning trace returned");
    }

    private async Task<(bool Passed, string Detail)> ProbeResponseFormatAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (_provider.Capabilities.ResponseFormat == ResponseFormatSupport.None)
            return (true, "not applicable: the provider declares no response-format capability");

        // Some APIs (Anthropic) have no schema-less JSON mode at all, so asking for a bare
        // json_object there constrains nothing and the mode would fail for the wrong reason.
        var format = _provider.Capabilities.ResponseFormat >= ResponseFormatSupport.JsonSchema
            ? LlmResponseFormat.JsonSchema(
                "probe_ok",
                """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""")
            : LlmResponseFormat.JsonObject();

        var constrained = config with { ResponseFormat = format };
        var response = await _provider.GenerateAsync(JsonPrompt, constrained, cancellationToken).ConfigureAwait(false);

        if (HasError(response))
            return (false, ErrorOf(response));

        var isJson = IsParseableJson(response.Content);
        return (isJson, isJson ? "valid JSON returned" : $"not JSON: {Truncate(response.Content)}");
    }

    private async Task<(bool Passed, string Detail)> ProbeErrorHandlingAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        // A model that cannot exist: the contract is a typed error response, never a throw.
        var broken = config with { Model = "orkeon-nonexistent-model-probe" };
        var response = await _provider.GenerateAsync("hello", broken, cancellationToken).ConfigureAwait(false);

        var reported = HasError(response);
        return (reported, reported ? $"typed error: {Truncate(ErrorOf(response))}" : "no error reported for an invalid model");
    }

    private async Task<(bool Passed, string Detail)> ProbeCancellationAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Short enough that the request is still in flight, long enough that it has started.
        linked.CancelAfter(TimeSpan.FromMilliseconds(50));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _provider.GenerateAsync(
                "Write a long essay about the history of cartography.", config, linked.Token).ConfigureAwait(false);

            // Returning a typed error is an acceptable outcome; returning full content is not.
            return HasError(response)
                ? (true, $"cancelled into a typed error after {stopwatch.ElapsedMilliseconds} ms")
                : (false, $"completed despite cancellation after {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (true, $"cancelled after {stopwatch.ElapsedMilliseconds} ms");
        }
    }

    private static (bool Passed, string Detail) Describe(
        LlmResponse response, Func<LlmResponse, bool> predicate, string? note = null)
    {
        if (HasError(response))
            return (false, ErrorOf(response));

        var passed = predicate(response);
        var detail = note ?? $"{response.Content.Length} char(s)";
        return (passed, $"{detail}, tokens={response.TokensUsed}");
    }

    private static bool HasError(LlmResponse response) => response.Metadata.ContainsKey("error");

    private static string ErrorOf(LlmResponse response) =>
        response.Metadata.TryGetValue("error", out var error) ? error?.ToString() ?? "" : "";

    private static bool IsParseableJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(content);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static string Truncate(string value, int max = 160) =>
        value.Length <= max ? value : value[..max] + "…";

    /// <summary>
    /// Renders the campaign as the Markdown row block the matrix's journal (§7) expects.
    /// </summary>
    /// <param name="provider">Provider name as it appears in the matrix.</param>
    /// <param name="model">The exact model identifier exercised.</param>
    /// <param name="version">The Orkeon version under test.</param>
    /// <param name="timestampUtc">Campaign timestamp, supplied by the caller.</param>
    /// <param name="results">The per-mode results.</param>
    /// <returns>A Markdown fragment ready to paste into the matrix.</returns>
    public static string ToMarkdown(
        string provider, string model, string version, DateTimeOffset timestampUtc,
        IReadOnlyList<LlmProbeResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var sb = new StringBuilder();
        sb.Append("# Campagne ").Append(provider).Append(" — ").Append(model).AppendLine();
        sb.AppendLine();
        sb.Append("- **Horodatage (UTC)** : ")
          .AppendLine(timestampUtc.ToString("u", CultureInfo.InvariantCulture));
        sb.Append("- **Version Orkéon** : ").AppendLine(version);
        sb.Append("- **Qualité de preuve** : sortie archivée").AppendLine();
        sb.AppendLine();
        sb.AppendLine("| Mode | Résultat | Détail | Durée |");
        sb.AppendLine("|---|---|---|---|");

        foreach (var result in results)
        {
            sb.Append("| ").Append(result.Mode)
              .Append(" | ").Append(result.Passed ? "✅" : "❌")
              .Append(" | ").Append(result.Detail.Replace('|', '/'))
              .Append(" | ").Append(result.ElapsedMs.ToString(CultureInfo.InvariantCulture)).AppendLine(" ms |");
        }

        return sb.ToString();
    }
}
