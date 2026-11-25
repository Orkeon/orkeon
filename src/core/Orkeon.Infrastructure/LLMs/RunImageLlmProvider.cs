using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Validation;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.LLMs.Capabilities;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.LLMs.Replay;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>Where the images of a run's input are read, and within which limits.</summary>
internal sealed record RunImageSource(IFileSystemService FileSystem, MultiModalOptions Options);

/// <summary>
/// Shows the images of a run's input to the model: when an agent's call is in progress (<see cref="RunImageTurn"/>), reads
/// them again and adds them to the first user message, provided the model this provider sends the call to reads images. One
/// that it does not, a file that is not an image, or one over what its vendor documents (<see cref="RunImageLimits"/>) is named
/// in the message and recorded for the person. A call the vendor refuses all the same is made once more without its images;
/// answered then, they are shown no more to that model in the run.
/// Each profile of a fallback chain has its own instance, so the model judged is the one that answers.
/// </summary>
internal sealed class RunImageLlmProvider(
    ILlmProvider inner, IFileSystemService files, MultiModalOptions options, IModelCapabilityRegistry capabilities)
    : ILlmProvider, IStreamingLlmProvider, ILlmProviderDecorator
{
    /// <summary>By run, what a model refused of it. A run's images are its own list.</summary>
    private readonly ConditionalWeakTable<IReadOnlyList<CrewInputImage>, ConcurrentDictionary<(string Model, string Path), bool>> _refused = new();

    public ILlmProvider Inner => inner;

    public string Name => inner.Name;

    public LlmConfig? BaseConfig => inner.BaseConfig;

    public LlmProviderCapabilities Capabilities => inner.Capabilities;

    /// <inheritdoc/>
    public LlmReasoningCapabilities? DescribeReasoning(string? model) => inner.DescribeReasoning(model);

    /// <inheritdoc/>
    public LlmEffortReport? ReportEffort(LlmConfig? config) => inner.ReportEffort(config);

    public bool SupportsStreaming => (inner as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    public bool StreamsNatively => (inner as IStreamingLlmProvider)?.StreamsNatively ?? false;

    public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        inner.GenerateAsync(prompt, config, cancellationToken);

    public async System.Threading.Tasks.Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        if (RunImageTurn.Current is not { } turn)
            return await inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);

        // Taken: a provider that answers through another model call must not show the images twice.
        using var taken = turn.Take();
        var prepared = await PrepareAsync(turn, messages, config, cancellationToken).ConfigureAwait(false);
        var outcome = await AttemptAsync(prepared, config, cancellationToken).ConfigureAwait(false);
        if (prepared.Shown.Count > 0 && outcome.MayBeItsImages)
        {
            var without = prepared.WithoutImages();
            var second = await AttemptAsync(without, config, cancellationToken).ConfigureAwait(false);
            if (second.Answered)
            {
                Remember(prepared.Shown, config);
                prepared = without;
                outcome = second;
            }
            else if (second.FailsOver)
            {
                outcome = second;
            }
        }

        var response = outcome.Get();
        if (response.Error is null)
            prepared.Note(turn);
        return response;
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (inner is IStreamingLlmProvider streaming)
        {
            await foreach (var text in streaming.GenerateStreamingAsync(prompt, config, cancellationToken).ConfigureAwait(false))
                yield return text;
            yield break;
        }

        var response = await inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
        if (response.Content.Length > 0)
            yield return response.Content;
    }

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (inner is not IStreamingLlmProvider streaming)
        {
            var response = await ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
            if (response.Content.Length > 0)
                yield return LlmStreamEvent.Content(response.Content);
            yield return LlmStreamEvent.Complete(response);
            yield break;
        }

        var turn = RunImageTurn.Current;
        Prepared? prepared = null;
        if (turn is not null)
        {
            using (turn.Take())
                prepared = await PrepareAsync(turn, messages, config, cancellationToken).ConfigureAwait(false);
        }

        var enumerator = streaming.ChatStreamingAsync(prepared?.Messages ?? messages, config, cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            // Whether the call may still be made again: it carries images and nothing of it was handed over.
            var first = prepared is { Shown.Count: > 0 };
            Outcome? refusal = null;
            IReadOnlyList<string> notShown = [];
            while (true)
            {
                var refused = false;
                var ended = false;
                // An async iterator resumes in its consumer's context: the turn is taken again around each step.
                using (turn?.Take())
                {
                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                            break;
                        var final = enumerator.Current.FinalResponse;
                        refused = first && final is not null && LlmFailureClassifier.MayBeItsImages(final);
                        if (refused)
                            refusal = new Outcome(final, null);
                        else if (refusal is not null && final?.Error is not null)
                            ended = !new Outcome(final, null).FailsOver;
                    }
                    catch (HttpRequestException ex) when (first && LlmFailureClassifier.MayBeItsImages(ex))
                    {
                        refused = true;
                        refusal = new Outcome(null, ex);
                    }
                    catch (HttpRequestException ex) when (refusal is not null && !new Outcome(null, ex).FailsOver)
                    {
                        ended = true;
                    }
                }

                first = false;
                if (refused)
                {
                    using (turn?.Take())
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                    notShown = prepared!.Shown;
                    prepared = prepared.WithoutImages();
                    enumerator = streaming.ChatStreamingAsync(prepared.Messages, config, cancellationToken).GetAsyncEnumerator(cancellationToken);
                    continue;
                }

                // The call without images failed before it said anything: the first error is the one to know.
                if (ended)
                {
                    yield return LlmStreamEvent.Complete(refusal!.Get());
                    yield break;
                }

                refusal = null;
                if (notShown.Count > 0 && enumerator.Current.FinalResponse is { Error: null })
                    Remember(notShown, config);

                if (turn is not null && prepared is not null && enumerator.Current.FinalResponse is not { Error: not null })
                {
                    prepared.Note(turn);
                    prepared = null;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>What one attempt of a call ended with.</summary>
    private sealed record Outcome(LlmResponse? Response, HttpRequestException? Failure)
    {
        public bool Answered => Response is { Error: null };

        public bool MayBeItsImages => Failure is not null
            ? LlmFailureClassifier.MayBeItsImages(Failure)
            : LlmFailureClassifier.MayBeItsImages(Response!);

        /// <summary>Whether another profile of a chain could answer: such a failure is never hidden behind a refusal.</summary>
        public bool FailsOver => LlmFailureClassifier.FailsOver(
            Failure is not null ? LlmFailureClassifier.Classify(Failure) : LlmFailureClassifier.Classify(Response!));

        public LlmResponse Get()
        {
            if (Failure is not null)
                ExceptionDispatchInfo.Throw(Failure);
            return Response!;
        }
    }

    private async System.Threading.Tasks.Task<Outcome> AttemptAsync(Prepared prepared, LlmConfig? config, CancellationToken cancellationToken)
    {
        try
        {
            return new Outcome(await inner.ChatAsync(prepared.Messages, config, cancellationToken).ConfigureAwait(false), null);
        }
        catch (HttpRequestException ex)
        {
            return new Outcome(null, ex);
        }
    }

    private void Remember(IReadOnlyList<string> paths, LlmConfig? config)
    {
        var refused = _refused.GetOrCreateValue(RunImages.Current);
        foreach (var path in paths)
            refused[(ModelOf(config) ?? string.Empty, path)] = true;
    }

    private bool IsRefused(string path, LlmConfig? config) =>
        _refused.TryGetValue(RunImages.Current, out var refused) && refused.ContainsKey((ModelOf(config) ?? string.Empty, path));

    /// <summary>The messages of one call, what they show and what they do not.</summary>
    private sealed record Prepared(
        LlmMessage[] Messages, IReadOnlyList<(string Path, string Reason)> Unseen, IReadOnlyList<string> Shown, Func<Prepared>? Refused = null)
    {
        /// <summary>The same call with every image named and none shown, for the vendor that refused them.</summary>
        public Prepared WithoutImages() => Refused!();

        public void Note(RunImageTurn turn)
        {
            foreach (var (path, reason) in Unseen)
                turn.NoteUnseen(path, reason);
        }
    }

    private async System.Threading.Tasks.Task<Prepared> PrepareAsync(
        RunImageTurn turn, LlmMessage[] messages, LlmConfig? config, CancellationToken cancellationToken)
    {
        var first = Array.FindIndex(messages, m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
        if (first < 0)
            return new Prepared(messages, [.. turn.Images.Select(i => (i.Path, "its model was asked nothing it could join"))], []);

        var limits = RunImageLimits.Of(inner.Name);
        var most = Math.Min(options.MaxImages, limits.MaxImages ?? int.MaxValue);
        var encoded = 0L;
        var shown = new List<(string Path, ImageContentPart Part)>();
        var unseen = new List<(string Path, string Reason)>();
        if (BlindReason(config) is { } blind)
        {
            unseen.AddRange(turn.Images.Select(i => (i.Path, blind)));
        }
        else
        {
            foreach (var image in turn.Images)
            {
                if (IsRefused(image.Path, config))
                {
                    unseen.Add((image.Path, RefusedReason));
                    continue;
                }

                if (shown.Count >= most)
                {
                    unseen.Add((image.Path, string.Create(CultureInfo.InvariantCulture, $"a call carries at most {most} images")));
                    continue;
                }

                var (part, reason) = await ReadAsync(image, limits, encoded, cancellationToken).ConfigureAwait(false);
                if (part is null)
                {
                    unseen.Add((image.Path, reason));
                    continue;
                }

                shown.Add((image.Path, part));
                encoded += RunImageLimits.Encoded(part.Data!.Count);
            }
        }

        return Compose(messages, first, shown, unseen);
    }

    private const string RefusedReason = "its model's provider refused the call that carried it";

    private static Prepared Compose(
        LlmMessage[] messages, int first, List<(string Path, ImageContentPart Part)> shown, List<(string Path, string Reason)> unseen)
    {
        var target = messages[first];
        var note = string.Concat(unseen.Select(u => $"\n\n[Image not shown to you: \"{u.Path}\"]"));
        var content = target.MultiModalContent ?? MultiModalContent.FromText(target.Content);
        if (note.Length > 0)
            content = content.AddText(note.Trim());
        foreach (var (_, part) in shown)
            content = content.AddImage(part);

        // Text alone stays a plain message: a model without vision is sent nothing it could refuse.
        var joined = target with { Content = target.Content + note };
        if (target.MultiModalContent is not null || shown.Count > 0)
            joined = joined with { MultiModalContent = content };
        var copy = messages.ToArray();
        copy[first] = joined;
        return new Prepared(copy, unseen, [.. shown.Select(s => s.Path)], () => Compose(
            messages, first, [], [.. unseen, .. shown.Select(s => (s.Path, RefusedReason))]));
    }

    /// <summary>Why the model of this call is shown no image; null when it reads them.</summary>
    private string? BlindReason(LlmConfig? config)
    {
        if (!options.Enabled)
            return "images are turned off on this host";
        var model = ModelOf(config);
#pragma warning disable ORKEXP005 // The per-model table is experimental.
        var reads = inner.Capabilities.Vision && capabilities.TryGet(model, inner.Name) is { Vision: true };
#pragma warning restore ORKEXP005
        return reads ? null : $"its model{(string.IsNullOrWhiteSpace(model) ? string.Empty : $" '{model}'")} is not known to read images";
    }

    /// <summary>The model a call of <paramref name="config"/> goes to.</summary>
    private string? ModelOf(LlmConfig? config)
    {
        var vendor = inner;
        while (vendor is ILlmProviderDecorator decorator)
            vendor = decorator.Inner;
        return vendor is HttpLlmProviderBase http
            ? http.EffectiveModel(config)
            : string.IsNullOrWhiteSpace(config?.Model) ? inner.BaseConfig?.Model : config.Model;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A file that cannot be read is one image not shown, told to the person, never the task's failure; the caller's cancellation still propagates.")]
    private async System.Threading.Tasks.Task<(ImageContentPart? Part, string Reason)> ReadAsync(
        CrewInputImage image, RunImageLimits limits, long encoded, CancellationToken cancellationToken)
    {
        try
        {
            if (await files.TryGetEntryAsync(image.Path, cancellationToken).ConfigureAwait(false) is not { Kind: VirtualEntryKind.File } entry)
                return (null, "the file is no longer there");
            var most = Math.Min(options.MaxImageSizeBytes, limits.MaxBytes ?? long.MaxValue);
            var tooLarge = string.Create(CultureInfo.InvariantCulture, $"it is over {most} bytes");
            if (entry.SizeBytes > most)
                return (null, tooLarge);
            if (await files.TryReadAllBytesAsync(image.Path, cancellationToken).ConfigureAwait(false) is not { } bytes)
                return (null, "the file is no longer there");
            if (bytes.LongLength > most)
                return (null, tooLarge);
            if (!limits.Fits(encoded, bytes.LongLength))
                return (null, "its model's provider has no room for it in one request");
            if (MediaTypeOf(bytes) is not { } mediaType)
                return (null, "it is not a PNG, JPEG, WebP or GIF image");
            if (!options.SupportedImageFormats.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
                return (null, $"{mediaType} images are not taken on this host");
            return limits.RefusalOf(bytes, mediaType) is { } refusal
                ? (null, refusal)
                : (ImageContentPart.FromBytes(bytes, mediaType), string.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return (null, "the file could not be read");
        }
    }

    /// <summary>The media type the first bytes say, whatever the file's name and announced type; null for any other.</summary>
    internal static string? MediaTypeOf(ReadOnlySpan<byte> bytes)
    {
        if (bytes is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..])
            return "image/png";
        if (bytes is [0xFF, 0xD8, 0xFF, ..])
            return "image/jpeg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
            return "image/gif";
        return bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) ? "image/webp" : null;
    }
}
