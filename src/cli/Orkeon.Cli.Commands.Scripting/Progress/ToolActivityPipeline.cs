using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Domain.FileSystem;

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
        observer.Started(new ToolCallStarted(id, tool, thread) { Subject = subject });
        var compared = tool is "edit_file" or "file_write" && subject.Length > 0;
        var before = compared ? await ReadForDiffAsync(subject, cancellationToken).ConfigureAwait(false) : null;
        var succeeded = false;
        ToolCallDiff? diff = null;
        try
        {
            var result = await _inner.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
            succeeded = result.Success;
            if (succeeded && before is not null)
                diff = await DiffAfterAsync(subject, before, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            observer.Finished(new ToolCallFinished(id, thread, succeeded) { Diff = diff });
        }
    }

    private async System.Threading.Tasks.Task<ToolCallDiff?> DiffAfterAsync(string path, string before, CancellationToken cancellationToken)
    {
        var after = await ReadForDiffAsync(path, cancellationToken).ConfigureAwait(false);
        if (after is null || after == before) return null;
        return new ToolCallDiff(path, LineDiff.Compute(before, after));
    }

    // The text of a file, empty when there is none yet; null when it cannot be shown: too large, binary, unreadable.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A display must not fail the tool call it describes.")]
    private async System.Threading.Tasks.Task<string?> ReadForDiffAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var virtualPath = path.StartsWith('/') ? path : "/workspace/" + path;
            var bytes = await _files.TryReadAllBytesAsync(virtualPath, cancellationToken).ConfigureAwait(false);
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
