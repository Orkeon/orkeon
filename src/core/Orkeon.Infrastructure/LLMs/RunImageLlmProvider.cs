using System.Globalization;
using System.Runtime.CompilerServices;
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
using Orkeon.Infrastructure.LLMs.Replay;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>Where the images of a run's input are read, and within which limits.</summary>
internal sealed record RunImageSource(IFileSystemService FileSystem, MultiModalOptions Options);

/// <summary>
/// Shows the images of a run's input to the model: when an agent's call is in progress (<see cref="RunImageTurn"/>), reads
/// them again and adds them to the first user message, provided the model this provider sends the call to reads images. One
/// that it does not, or a file that is not an image, is named in the message and recorded for the person. Each profile of a
/// fallback chain has its own instance, so the model judged is the one that answers.
/// </summary>
internal sealed class RunImageLlmProvider(
    ILlmProvider inner, IFileSystemService files, MultiModalOptions options, IModelCapabilityRegistry capabilities)
    : ILlmProvider, IStreamingLlmProvider, ILlmProviderDecorator
{
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
        var response = await inner.ChatAsync(prepared.Messages, config, cancellationToken).ConfigureAwait(false);
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
        await using var disposal = enumerator.ConfigureAwait(false);
        while (true)
        {
            // An async iterator resumes in its consumer's context: the turn is taken again around each step.
            using (turn?.Take())
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    break;
            }

            if (turn is not null && prepared is not null && enumerator.Current.FinalResponse is not { Error: not null })
            {
                prepared.Note(turn);
                prepared = null;
            }

            yield return enumerator.Current;
        }
    }

    private sealed record Prepared(LlmMessage[] Messages, IReadOnlyList<(string Path, string Reason)> Unseen)
    {
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
            return new Prepared(messages, [.. turn.Images.Select(i => (i.Path, "its model was asked nothing it could join"))]);

        var shown = new List<ImageContentPart>();
        var unseen = new List<(string Path, string Reason)>();
        if (BlindReason(config) is { } blind)
        {
            unseen.AddRange(turn.Images.Select(i => (i.Path, blind)));
        }
        else
        {
            foreach (var image in turn.Images)
            {
                if (shown.Count >= options.MaxImages)
                {
                    unseen.Add((image.Path, string.Create(CultureInfo.InvariantCulture, $"a call carries at most {options.MaxImages} images")));
                    continue;
                }

                var (part, reason) = await ReadAsync(image, cancellationToken).ConfigureAwait(false);
                if (part is null)
                    unseen.Add((image.Path, reason));
                else
                    shown.Add(part);
            }
        }

        var target = messages[first];
        var note = string.Concat(unseen.Select(u => $"\n\n[Image not shown to you: \"{u.Path}\"]"));
        var content = target.MultiModalContent ?? MultiModalContent.FromText(target.Content);
        if (note.Length > 0)
            content = content.AddText(note.Trim());
        foreach (var part in shown)
            content = content.AddImage(part);

        // Text alone stays a plain message: a model without vision is sent nothing it could refuse.
        var joined = target with { Content = target.Content + note };
        if (target.MultiModalContent is not null || shown.Count > 0)
            joined = joined with { MultiModalContent = content };
        var copy = messages.ToArray();
        copy[first] = joined;
        return new Prepared(copy, unseen);
    }

    /// <summary>Why the model of this call is shown no image; null when it reads them.</summary>
    private string? BlindReason(LlmConfig? config)
    {
        if (!options.Enabled)
            return "images are turned off on this host";
        var vendor = inner;
        while (vendor is ILlmProviderDecorator decorator)
            vendor = decorator.Inner;
        var model = vendor is HttpLlmProviderBase http
            ? http.EffectiveModel(config)
            : string.IsNullOrWhiteSpace(config?.Model) ? inner.BaseConfig?.Model : config.Model;
#pragma warning disable ORKEXP005 // The per-model table is experimental.
        var reads = inner.Capabilities.Vision && capabilities.TryGet(model, inner.Name) is { Vision: true };
#pragma warning restore ORKEXP005
        return reads ? null : $"its model{(string.IsNullOrWhiteSpace(model) ? string.Empty : $" '{model}'")} is not known to read images";
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A file that cannot be read is one image not shown, told to the person, never the task's failure; the caller's cancellation still propagates.")]
    private async System.Threading.Tasks.Task<(ImageContentPart? Part, string Reason)> ReadAsync(CrewInputImage image, CancellationToken cancellationToken)
    {
        try
        {
            if (await files.TryGetEntryAsync(image.Path, cancellationToken).ConfigureAwait(false) is not { Kind: VirtualEntryKind.File } entry)
                return (null, "the file is no longer there");
            var tooLarge = string.Create(CultureInfo.InvariantCulture, $"it is over {options.MaxImageSizeBytes} bytes");
            if (entry.SizeBytes > options.MaxImageSizeBytes)
                return (null, tooLarge);
            if (await files.TryReadAllBytesAsync(image.Path, cancellationToken).ConfigureAwait(false) is not { } bytes)
                return (null, "the file is no longer there");
            if (bytes.LongLength > options.MaxImageSizeBytes)
                return (null, tooLarge);
            if (MediaTypeOf(bytes) is not { } mediaType)
                return (null, "it is not a PNG, JPEG, WebP or GIF image");
            return options.SupportedImageFormats.Contains(mediaType, StringComparer.OrdinalIgnoreCase)
                ? (ImageContentPart.FromBytes(bytes, mediaType), string.Empty)
                : (null, $"{mediaType} images are not taken on this host");
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
