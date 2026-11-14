using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>One profile of a chain, entered and assembled: its provider and the configuration its profile was built with.</summary>
/// <param name="Name">The profile's name.</param>
/// <param name="Provider">The provider the registry assembled for it, metered, traced and in its call mode.</param>
/// <param name="BaseConfig">The configuration of the profile; null takes the provider's own.</param>
internal sealed record FallbackLink(string Name, ILlmProvider Provider, LlmConfig? BaseConfig = null);

/// <summary>
/// The outermost link of a profile that names a <c>Fallback</c> (FWK-05): it tries the profile itself,
/// then each profile of its list, and moves on only for a failure a retry cannot cure
/// (<see cref="LlmFailureClassifier.FailsOver"/>): a transport error, a 5xx, a 429 once the retries are
/// spent, a timeout. A 4xx, a blocked content and a failure nothing is known about are answered as they are.
/// </summary>
/// <remarks>
/// Every link is a profile already entered by <c>LlmProviderEntrance</c>, so the meter and the trace name the
/// provider that answered, and each link keeps its own call mode. A stream moves on only before its first
/// delta: once a token reached the caller the call is committed to its link. The chain's time budget is
/// checked between two links; it does not cut a call in flight (the links' own timeouts do).
/// </remarks>
internal sealed partial class FallbackLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    private readonly IReadOnlyList<FallbackLink> _links;
    private readonly ILlmFallbackObserver? _observer;
    private readonly ILogger _logger;
    private readonly TimeSpan? _budget;
    private readonly TimeProvider _time;

    public FallbackLlmProvider(
        string profile,
        IReadOnlyList<FallbackLink> links,
        ILlmFallbackObserver? observer,
        ILogger logger,
        TimeSpan? budget,
        TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentNullException.ThrowIfNull(links);
        if (links.Count == 0)
            throw new ArgumentException("A fallback chain holds at least one profile.", nameof(links));
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        Profile = profile;
        _links = links;
        _observer = observer;
        _logger = logger;
        _budget = budget;
        _time = time;
    }

    /// <summary>The profile a crew named: the first of the chain.</summary>
    public string Profile { get; }

    /// <summary>The profiles of the chain, in the order they are tried.</summary>
    internal IReadOnlyList<FallbackLink> Links => _links;

    /// <summary>The first profile's provider name.</summary>
    public string Name => _links[0].Provider.Name;

    /// <summary>The first profile's configuration: what the chat client of the chain starts every call from.</summary>
    public LlmConfig? BaseConfig => _links[0].BaseConfig ?? _links[0].Provider.BaseConfig;

    /// <summary>What the first profile declares; <see cref="LlmProfileRegistry"/> warns about a later profile that does less.</summary>
    public LlmProviderCapabilities Capabilities => _links[0].Provider.Capabilities;

    /// <summary>What the first profile's provider and <paramref name="model"/> take: a call starts there, and a later profile that does less is warned about.</summary>
    /// <param name="model">The model to judge; null or blank for the first profile's own.</param>
    public LlmReasoningCapabilities? DescribeReasoning(string? model) => _links[0].Provider.DescribeReasoning(model);

    /// <summary>The effort the first profile sends for <paramref name="config"/>: a call that fails over may land on a profile that judges it otherwise.</summary>
    /// <param name="config">The call's configuration.</param>
    public LlmEffortReport? ReportEffort(LlmConfig? config) => _links[0].Provider.ReportEffort(config);

    /// <summary>Always true: a link that cannot stream answers buffered.</summary>
    public bool SupportsStreaming => true;

    /// <summary>Whether the first profile streams on the wire.</summary>
    public bool StreamsNatively => (_links[0].Provider as IStreamingLlmProvider)?.StreamsNatively ?? false;

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        RunAsync((link, c) => link.Provider.GenerateAsync(prompt, c, cancellationToken), config, cancellationToken);

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        RunAsync((link, c) => link.Provider.ChatAsync(messages, c, cancellationToken), config, cancellationToken);

    private async Task<LlmResponse> RunAsync(
        Func<FallbackLink, LlmConfig?, Task<LlmResponse>> call, LlmConfig? config, CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        var failures = new List<string>();
        Exception? lastException = null;
        var budgetSpent = false;

        for (var i = 0; i < _links.Count; i++)
        {
            var link = _links[i];
            var kind = LlmFailureKind.None;
            try
            {
                var response = await call(link, ConfigFor(i, config)).ConfigureAwait(false);
                kind = LlmFailureClassifier.Classify(response);
                if (!LlmFailureClassifier.FailsOver(kind))
                    return response;
                // A provider reports the caller's own cancellation in-band too: never a reason to switch.
                cancellationToken.ThrowIfCancellationRequested();
                failures.Add(Describe(link, response.Error));
                lastException = null;
            }
            catch (Exception ex) when (FailsOver(ex, cancellationToken, out kind))
            {
                failures.Add(Describe(link, ex.Message));
                lastException = ex;
            }

            if (SwitchOrStop(i, started, kind))
            {
                budgetSpent = true;
                break;
            }
        }

        var text = ExhaustedMessage(failures, budgetSpent);
        if (lastException is not null)
            throw new HttpRequestException(text, lastException);
        return Exhausted(text);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var started = _time.GetTimestamp();
        var failures = new List<string>();
        Exception? lastException = null;
        var budgetSpent = false;

        for (var i = 0; i < _links.Count; i++)
        {
            var link = _links[i];
            var chunks = OpenText(link, prompt, ConfigFor(i, config), cancellationToken).GetAsyncEnumerator(cancellationToken);
            await using var chunksScope = chunks.ConfigureAwait(false);
            var opening = await PullFirstAsync(chunks, null, cancellationToken).ConfigureAwait(false);
            if (!opening.Failed)
            {
                // Committed: the first chunk reached the caller, or the stream was empty; a failure
                // from here on is the caller's.
                if (opening.Has)
                {
                    yield return opening.First!;
                    while (await chunks.MoveNextAsync().ConfigureAwait(false))
                        yield return chunks.Current;
                }

                yield break;
            }

            failures.Add(Describe(link, opening.Failure!));
            lastException = opening.Exception;
            if (SwitchOrStop(i, started, opening.Kind))
            {
                budgetSpent = true;
                break;
            }
        }

        throw new HttpRequestException(ExhaustedMessage(failures, budgetSpent), lastException);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var started = _time.GetTimestamp();
        var failures = new List<string>();
        Exception? lastException = null;
        var budgetSpent = false;

        for (var i = 0; i < _links.Count; i++)
        {
            var link = _links[i];
            var events = OpenChat(link, messages, ConfigFor(i, config), cancellationToken).GetAsyncEnumerator(cancellationToken);
            await using var eventsScope = events.ConfigureAwait(false);
            var opening = await PullFirstAsync(events, InBandFailure, cancellationToken).ConfigureAwait(false);
            if (!opening.Failed)
            {
                // Committed: the first event reached the caller, or the stream was empty.
                if (opening.Has)
                {
                    yield return opening.First!;
                    while (await events.MoveNextAsync().ConfigureAwait(false))
                        yield return events.Current;
                }

                yield break;
            }

            failures.Add(Describe(link, opening.Failure!));
            lastException = opening.Exception;
            if (SwitchOrStop(i, started, opening.Kind))
            {
                budgetSpent = true;
                break;
            }
        }

        var text = ExhaustedMessage(failures, budgetSpent);
        if (lastException is not null)
            throw new HttpRequestException(text, lastException);
        yield return LlmStreamEvent.Complete(Exhausted(text));
    }

    /// <summary>What pulling the first element of a stream produced: committed, empty or failed over.</summary>
    private sealed class StreamOpening<T>
    {
        public bool Has { get; set; }

        public T? First { get; set; }

        public bool Failed { get; set; }

        public LlmFailureKind Kind { get; set; } = LlmFailureKind.None;

        public string? Failure { get; set; }

        public Exception? Exception { get; set; }
    }

    private static (LlmFailureKind Kind, string? Error)? InBandFailure(LlmStreamEvent first, CancellationToken cancellationToken) =>
        FirstEventFailsOver(first, cancellationToken, out var kind, out var error) ? (kind, error) : null;

    /// <summary>
    /// Pulls the first element of a stream, the one point where a link can still be left: a failure
    /// that fails over (thrown, or in-band when <paramref name="inBandFailure"/> says so) is reported, not thrown.
    /// </summary>
    private static async System.Threading.Tasks.Task<StreamOpening<T>> PullFirstAsync<T>(
        IAsyncEnumerator<T> stream, Func<T, CancellationToken, (LlmFailureKind Kind, string? Error)?>? inBandFailure, CancellationToken cancellationToken)
    {
        var opening = new StreamOpening<T>();
        try
        {
            opening.Has = await stream.MoveNextAsync().ConfigureAwait(false);
            if (opening.Has)
            {
                opening.First = stream.Current;
                if (inBandFailure?.Invoke(opening.First, cancellationToken) is { } failure)
                {
                    opening.Kind = failure.Kind;
                    opening.Failure = failure.Error;
                    opening.Failed = true;
                }
            }
        }
        catch (Exception ex) when (FailsOver(ex, cancellationToken, out var kind))
        {
            opening.Kind = kind;
            opening.Failure = ex.Message;
            opening.Exception = ex;
            opening.Failed = true;
        }

        return opening;
    }

    private static bool FailsOver(Exception ex, CancellationToken cancellationToken, out LlmFailureKind kind)
    {
        kind = LlmFailureKind.None;
        if (cancellationToken.IsCancellationRequested)
            return false;
        kind = LlmFailureClassifier.Classify(ex);
        return LlmFailureClassifier.FailsOver(kind);
    }

    /// <summary>Whether the first event of a stream is a completed response that fails over (the failure arrives in-band).</summary>
    private static bool FirstEventFailsOver(
        LlmStreamEvent first, CancellationToken cancellationToken, out LlmFailureKind kind, out string? error)
    {
        kind = LlmFailureKind.None;
        error = null;
        if (first.Kind != LlmStreamEventKind.Completed || first.FinalResponse is not { } final)
            return false;
        kind = LlmFailureClassifier.Classify(final);
        if (!LlmFailureClassifier.FailsOver(kind))
            return false;
        cancellationToken.ThrowIfCancellationRequested();
        error = final.Error;
        return true;
    }

    /// <summary>
    /// Announces the switch to the link after <paramref name="index"/>; true when the budget is spent and the
    /// chain must stop instead. A last link switches to nothing.
    /// </summary>
    private bool SwitchOrStop(int index, long started, LlmFailureKind kind)
    {
        if (index + 1 >= _links.Count)
            return false;
        if (BudgetSpent(started))
            return true;
        Switched(_links[index], _links[index + 1], kind);
        return false;
    }

    private static IAsyncEnumerable<string> OpenText(FallbackLink link, string prompt, LlmConfig? config, CancellationToken ct) =>
        link.Provider is IStreamingLlmProvider { SupportsStreaming: true } streaming
            ? streaming.GenerateStreamingAsync(prompt, config, ct)
            : BufferedText(link.Provider, prompt, config, ct);

    private static async IAsyncEnumerable<string> BufferedText(
        ILlmProvider provider, string prompt, LlmConfig? config, [EnumeratorCancellation] CancellationToken ct)
    {
        var response = await provider.GenerateAsync(prompt, config, ct).ConfigureAwait(false);
        if (response.Error is not null)
            throw LlmFailureClassifier.ToException(response);
        if (response.Content.Length > 0)
            yield return response.Content;
    }

    private static IAsyncEnumerable<LlmStreamEvent> OpenChat(FallbackLink link, LlmMessage[] messages, LlmConfig? config, CancellationToken ct) =>
        link.Provider is IStreamingLlmProvider { SupportsStreaming: true } streaming
            ? streaming.ChatStreamingAsync(messages, config, ct)
            : BufferedChat(link.Provider, messages, config, ct);

    private static async IAsyncEnumerable<LlmStreamEvent> BufferedChat(
        ILlmProvider provider, LlmMessage[] messages, LlmConfig? config, [EnumeratorCancellation] CancellationToken ct)
    {
        var response = await provider.ChatAsync(messages, config, ct).ConfigureAwait(false);
        if (response.Error is null && response.Content.Length > 0)
            yield return LlmStreamEvent.Content(response.Content);
        yield return LlmStreamEvent.Complete(response);
    }

    /// <summary>
    /// The configuration a link is called with. The chat client of the chain starts every call from the first
    /// profile's configuration and lays the caller's options over it, so a call carries the first profile's
    /// settings (model, key, endpoint, limits, sampling, thinking, custom parameters...) as well as the caller's.
    /// A field that still equals the first profile's is taken as the profile's own and replaced by the value of the
    /// profile the call lands on; a field that differs was set by the caller and stays for every profile. A caller
    /// that sets a field to exactly the first profile's value cannot be told from the profile: it gets the later
    /// profile's. Every settable field is covered (reflection over <see cref="LlmConfig"/>), so a field added
    /// later follows the rule without a change here.
    /// </summary>
    private LlmConfig? ConfigFor(int index, LlmConfig? config)
    {
        var primary = BaseConfig;
        if (index == 0 || config is null || primary is null)
            return config;

        var link = _links[index];
        var own = link.BaseConfig ?? link.Provider.BaseConfig ?? LlmConfig.OnProfile();
        var result = config with { };
        foreach (var property in s_settable.Where(p => SameValue(p.GetValue(config), p.GetValue(primary))))
            property.SetValue(result, property.GetValue(own));

        return result;
    }

    private static readonly System.Reflection.PropertyInfo[] s_settable =
    [
        .. typeof(LlmConfig).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanRead && p.SetMethod is { IsPublic: true }),
    ];

    private static bool SameValue(object? a, object? b)
    {
        if (ReferenceEquals(a, b) || Equals(a, b))
            return true;
        if (a is IReadOnlyDictionary<string, object> da && b is IReadOnlyDictionary<string, object> db)
            return da.Count == db.Count && da.All(pair => db.TryGetValue(pair.Key, out var other) && Equals(pair.Value, other));
        return a is System.Collections.IEnumerable ea and not string && b is System.Collections.IEnumerable eb and not string
            && ea.Cast<object?>().SequenceEqual(eb.Cast<object?>());
    }

    private bool BudgetSpent(long started) =>
        _budget is { } budget && _time.GetElapsedTime(started) >= budget;

    private static string Describe(FallbackLink link, string? error) =>
        $"{link.Name}: {LogSanitizer.SanitizeString(error ?? "failed")}";

    private string ExhaustedMessage(List<string> failures, bool budgetSpent) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Every profile tried for '{Profile}' failed{(budgetSpent ? $" (the fallback time budget of {_budget!.Value.TotalSeconds:0.#} s was spent before the next one)" : string.Empty)}: {string.Join("; ", failures)}");

    private static LlmResponse Exhausted(string text) => new()
    {
        Metadata = new Dictionary<string, object>
        {
            [LlmResponseMetadataKeys.Error] = text,
            [LlmResponseMetadataKeys.ErrorType] = LlmFailureClassifier.ExhaustedErrorType,
        },
    };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Observer fault barrier: a faulty host observer must degrade to an unobserved switch, never to a failed LLM call.")]
    private void Switched(FallbackLink from, FallbackLink to, LlmFailureKind kind)
    {
        var reason = LlmFailureClassifier.Reason(kind);
        LogSwitched(_logger, Profile, from.Name, to.Name, reason);
        try
        {
            _observer?.FallbackTaken(Profile, from.Name, to.Name, reason);
        }
        catch (Exception ex)
        {
            LogObserverFailed(_logger, ex, Profile);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM profile '{Profile}' moved a call from '{From}' to '{To}': {Reason}.")]
    private static partial void LogSwitched(ILogger logger, string profile, string from, string to, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The fallback observer failed on profile '{Profile}'; the call goes on.")]
    private static partial void LogObserverFailed(ILogger logger, Exception exception, string profile);
}
