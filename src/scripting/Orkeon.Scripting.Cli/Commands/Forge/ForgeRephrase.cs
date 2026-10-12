using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Configuration;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Hosting;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Scripting.Cli.Commands.Run;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// <c>orkeon forge rephrase &lt;request&gt;</c> (STUDIO-57): one call to the assistant's LLM that
/// rewrites the request typed at step 1 as a clear brief — what comes in, what comes out and in
/// which shape, every rule the user stated — in the user's own language, adding nothing. No
/// session is opened, nothing is written: the verb answers and exits. With <c>--events jsonl</c>
/// the answer is one <c>need.rephrased</c> line (<c>text</c>, <c>original</c>); in the terminal
/// it is the text itself. A missing LLM is <c>FORGE-LLM-UNAVAILABLE</c>, an answer the model
/// did not give <c>FORGE-REPHRASE-EMPTY</c>; both exit 1.
/// </summary>
internal static class ForgeRephrase
{
    /// <summary>The event the rewritten request travels on.</summary>
    public const string EventKind = "need.rephrased";

    /// <summary>The code of a model that answered nothing usable.</summary>
    public const string EmptyCode = "FORGE-REPHRASE-EMPTY";

    /// <summary>The code of a host with no LLM to ask.</summary>
    public const string LlmUnavailableCode = "FORGE-LLM-UNAVAILABLE";

    private const int ExitError = 1;

    /// <summary>Runs the verb: builds the host on the settings, asks the default LLM once, answers.</summary>
    public static async Task<int> RunAsync(string workspace, ForgeCommandOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentNullException.ThrowIfNull(options);

        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, workspace);
        var events = options.Events ? new ForgeEventWriter(Console.Out) : null;
        using var host = RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan(),
            configureLogging: (_, logging) =>
            {
                // stdout carries the answer; everything the host says goes to stderr.
                logging.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.TimestampFormat = "HH:mm:ss.fff ";
                });
                logging.Services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(
                    o => o.LogToStandardErrorThreshold = LogLevel.Trace);
                logging.SetMinimumLevel(LogLevel.Warning);
            },
            configureServices: (_, services) =>
            {
                if (events is not null)
                    services.AddSingleton<ILlmCallObserver>(new RunEventsLlmCallObserver(events, TimeProvider.System));
            });

        if (!host.Services.GetRequiredService<IConfiguration>().GetSection(ConfigurationKeys.LlmSection).Exists()
            || host.Services.GetService<ILlmProvider>() is not { } provider)
        {
            const string Message = "no LLM is configured — run `orkeon init`, or pass --settings.";
            if (events is not null)
                events.Error(LlmUnavailableCode, Message, recoverable: false);
            else
                await Console.Error.WriteLineAsync($"orkeon forge: {Message}").ConfigureAwait(false);
            return ExitError;
        }

        var rewritten = await RewriteAsync(provider, options.Need!, events, CancellationToken.None).ConfigureAwait(false);
        if (rewritten is null)
        {
            const string Message = "the model gave no rewritten request.";
            if (events is not null)
                events.Error(EmptyCode, Message, recoverable: false);
            else
                await Console.Error.WriteLineAsync($"orkeon forge: {Message}").ConfigureAwait(false);
            return ExitError;
        }

        if (events is not null)
            events.Emit(EventKind, new { text = rewritten, original = options.Need });
        else
            await Console.Out.WriteLineAsync(rewritten).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Asks <paramref name="provider"/> once to rewrite <paramref name="request"/>; the answer
    /// trimmed of the quotes and fences a model wraps it in, or null when nothing usable came
    /// back.
    /// </summary>
    public static async Task<string?> RewriteAsync(ILlmProvider provider, string request, CancellationToken cancellationToken) =>
        Clean((await AskAsync(provider, request, cancellationToken).ConfigureAwait(false)).Content);

    /// <summary>
    /// <see cref="RewriteAsync(ILlmProvider, string, CancellationToken)"/> that, given <paramref name="events"/>,
    /// writes the call's <c>llm.call.*</c> lines and, after it, a <c>cost.updated</c> line.
    /// </summary>
    public static async Task<string?> RewriteAsync(
        ILlmProvider provider, string request, ForgeEventWriter? events, CancellationToken cancellationToken)
    {
        if (events is null)
            return await RewriteAsync(provider, request, cancellationToken).ConfigureAwait(false);

        var observed = LlmProviderFactory.Observe(provider, new RunEventsLlmCallObserver(events, TimeProvider.System));
        var response = await AskAsync(observed, request, cancellationToken).ConfigureAwait(false);
        ForgeCost.Emit(
            events,
            new ForgeBudget(),
            new ForgeUsageSnapshot(response.PromptTokens ?? 0, response.CompletionTokens ?? 0, 0));
        return Clean(response.Content);
    }

    private static async Task<LlmResponse> AskAsync(ILlmProvider provider, string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(request);

        LlmMessage[] prompt =
        [
            new LlmMessage { Role = "system", Content = SystemPrompt },
            new LlmMessage { Role = "user", Content = request.Trim() },
        ];
        return await provider.ChatAsync(prompt, config: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What the model is asked — the one job, its limits, and the shape of the answer.</summary>
    public const string SystemPrompt =
        "You rewrite a request that a person typed for a team of AI agents, so that the request is clear and "
        + "complete for the people who will build that team. Rules: keep every fact the person gave — folders, "
        + "names, formats, numbers, addresses, constraints — and invent nothing; say what comes in, what comes "
        + "out and in which shape, and every rule the person stated; keep the person's language and their "
        + "words where they are clear; write 3 to 8 sentences of plain text — no Markdown, no title, no list, "
        + "no preamble, no comment on the request. Answer with the rewritten request only.";

    /// <summary>The answer without the wrapping a model adds: fences, surrounding quotes, blank edges.</summary>
    public static string? Clean(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return null;

        var text = answer.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = text.IndexOf('\n', StringComparison.Ordinal);
            text = firstBreak < 0 ? "" : text[(firstBreak + 1)..];
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
                text = text[..fence];
            text = text.Trim();
        }

        if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '«' && text[^1] == '»')))
            text = text[1..^1].Trim();

        return text.Length == 0 ? null : text;
    }
}
