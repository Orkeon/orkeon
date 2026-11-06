using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Constants.Llm;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Abstractions.Security;

namespace Orkeon.Cli.Commands.Scripting.Progress;

/// <summary>
/// Tells the console about each call of the host's invocation point, the one every model-driven call goes through.
/// The arguments stay here and on the screen: the run's stream and the logs keep to the tool's name. For a tool that
/// changes a file it compares the file before and after the call, for the screen alone: what the model receives
/// does not change, and a file another writer touched meanwhile can show a change that is not the call's.
/// </summary>
public sealed class ToolActivityPipeline : IToolInvocationPipeline
{
    private readonly IToolInvocationPipeline _inner;
    private readonly ToolActivityFeed _feed;
    private readonly IFileSystemService _files;
    private long _lastId;

    // A larger file shows the call's line without its change.
    private const int MaxComparedBytes = 1024 * 1024;

    // On a call's span: a step shows this call, so the turn's sentence does not count it again (ToolActivityAggregator).
    private const string ReportedTag = "orkeon.tool.reported";

    /// <summary>Wraps <paramref name="inner"/>; <paramref name="files"/> reads what a call changes.</summary>
    public ToolActivityPipeline(IToolInvocationPipeline inner, ToolActivityFeed feed, IFileSystemService files)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    /// <summary>Puts the feed in front of the invocation point registered so far.</summary>
    public static IServiceCollection Decorate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ToolActivityFeed>();
        var inner = services.LastOrDefault(d => d.ServiceType == typeof(IToolInvocationPipeline));
        if (inner?.ImplementationFactory is not { } factory) return services;
        services.Remove(inner);
        services.AddSingleton<IToolInvocationPipeline>(sp => new ToolActivityPipeline(
            (IToolInvocationPipeline)factory(sp), sp.GetRequiredService<ToolActivityFeed>(), sp.GetRequiredService<IFileSystemService>()));
        return services;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<ToolInvocationResult> InvokeAsync(
        ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return _feed.Observer is { } observer
            ? ObservedAsync(observer, invocation, cancellationToken)
            : _inner.InvokeAsync(invocation, cancellationToken);
    }

    private async System.Threading.Tasks.Task<ToolInvocationResult> ObservedAsync(
        IToolActivityObserver observer, ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _lastId);
        var thread = ConsoleThread.Current;
        var tool = invocation.Tool.Name;
        var subject = ToolCallSubject.Of(tool, invocation.Arguments);
        if (Activity.Current is { } span && Equals(span.GetTagItem(GenAiAttributes.ToolName), tool))
            span.SetTag(ReportedTag, true);
        Tell(() => observer.Started(new ToolCallStarted(id, tool, thread) { Subject = subject }));
        var path = tool is "edit_file" or "file_write" && ToolCallSubject.PathOf(invocation.Arguments) is { Length: > 0 } written
            ? WorkingDirectoryResolver.Absolutize(written, _files)
            : null;
        var before = path is null ? null : await ReadForDiffAsync(path, cancellationToken).ConfigureAwait(false);
        var succeeded = false;
        ToolCallDiff? diff = null;
        try
        {
            var result = await _inner.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
            succeeded = result.Success;
            if (succeeded && before is not null)
            {
                var after = await ReadForDiffAsync(path!, cancellationToken).ConfigureAwait(false);
                // Line endings alone change no row: nothing to show.
                if (after is not null && after != before && LineDiff.Compute(before, after) is { Count: > 0 } rows)
                    diff = new ToolCallDiff(subject, rows);
            }
            return result;
        }
        finally
        {
            Tell(() => observer.Finished(new ToolCallFinished(id, thread, succeeded) { Diff = diff }));
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A display must not fail the tool call it describes.")]
    private static void Tell(Action observer)
    {
        try
        {
            observer();
        }
        catch (Exception)
        {
            // The call goes on without its line.
        }
    }

    // The text of a file, empty when there is none yet; null when it cannot be shown: too large, binary, a link,
    // unreadable. Measured before it is opened: an entry of no length is not read at all, which keeps a FIFO or a
    // device, opened in a call nothing can cancel, out of it.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A display must not fail the tool call it describes.")]
    private async System.Threading.Tasks.Task<string?> ReadForDiffAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await _files.TryGetEntryAsync(path, cancellationToken).ConfigureAwait(false);
            if (entry is { Kind: not VirtualEntryKind.File } or { SizeBytes: > MaxComparedBytes }) return null;
            if (entry is { SizeBytes: 0 }) return string.Empty;
            var bytes = await _files.TryReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (bytes is null) return string.Empty;
            if (bytes.Length > MaxComparedBytes || Array.IndexOf(bytes, (byte)0) >= 0) return null;
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
