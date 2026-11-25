using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The images of the crew run in progress (<see cref="CrewInput.Images"/>), by reference: the orchestrator opens a scope per
/// run, and each model call of an agent opens a <see cref="RunImageTurn"/> on it. The bytes are never here: the provider
/// layer reads them again for each call, so a conversation, its compaction and its checkpoints hold none.
/// </summary>
/// <remarks>
/// Backed by <see cref="AsyncLocal{T}"/>, like <see cref="CrewPlanScope"/>: a crew run from inside a task opens its own scope
/// and sees its own images only.
/// </remarks>
public static class RunImages
{
    private static readonly AsyncLocal<Run?> s_ambient = new();

    /// <summary>Gets the images of the run in progress; empty outside a run.</summary>
    public static IReadOnlyList<CrewInputImage> Current => s_ambient.Value?.Images ?? [];

    /// <summary>Gets what was not shown so far, and to whom; empty outside a run.</summary>
    public static IReadOnlyList<UnseenImage> Unseen => s_ambient.Value?.Snapshot() ?? [];

    /// <summary>Opens the scope of a run; disposing the handle restores the enclosing one.</summary>
    /// <param name="images">The run's images, by reference.</param>
    /// <returns>The handle that restores the enclosing scope.</returns>
    public static IDisposable Begin(IReadOnlyList<CrewInputImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        var enclosing = s_ambient.Value;
        s_ambient.Value = new Run(images);
        return new Scope(enclosing);
    }

    /// <summary>Records that <paramref name="reader"/> was shown none of the run's images.</summary>
    /// <param name="reader">The agent or the task, in words for a person.</param>
    /// <param name="reason">Why, in words for a person.</param>
    public static void NoteAllUnseen(string reader, string reason)
    {
        if (s_ambient.Value is { } run)
            run.Note(run.Images.Select(i => new UnseenImage { Path = i.Path, Reader = reader, Reason = reason }));
    }

    /// <summary>
    /// Opens the turn of one model call of an agent: the images its file-system scope lets it read. Null when there is none;
    /// those the scope excludes are recorded as unseen.
    /// </summary>
    /// <param name="reader">The agent, in words for a person.</param>
    /// <returns>The turn, to dispose once the call is answered; null when the call carries no image.</returns>
    public static RunImageTurn? OpenTurn(string reader)
    {
        if (s_ambient.Value is not { Images.Count: > 0 } run)
            return null;

        var scope = AgentRunScope.Current?.FileSystem;
        var readable = run.Images.Where(i => scope is null || scope.Allows(i.Path, FileAccessRights.Read)).ToList();
        if (readable.Count < run.Images.Count)
        {
            run.Note(run.Images.Except(readable).Select(i => new UnseenImage
            {
                Path = i.Path,
                Reader = reader,
                Reason = "its filesystem scope does not cover the file",
            }));
        }

        return readable.Count == 0 ? null : new RunImageTurn(readable, reader, run.Note);
    }

    private sealed class Scope(Run? enclosing) : IDisposable
    {
        public void Dispose() => s_ambient.Value = enclosing;
    }

    private sealed class Run(IReadOnlyList<CrewInputImage> images)
    {
        private readonly Lock _sync = new();
        private readonly List<UnseenImage> _unseen = [];

        public IReadOnlyList<CrewInputImage> Images { get; } = images;

        public void Note(IEnumerable<UnseenImage> unseen)
        {
            lock (_sync)
            {
                foreach (var image in unseen)
                {
                    if (!_unseen.Contains(image))
                        _unseen.Add(image);
                }
            }
        }

        public IReadOnlyList<UnseenImage> Snapshot()
        {
            lock (_sync)
                return [.. _unseen];
        }
    }
}

/// <summary>
/// The images one model call of an agent may carry. Ambient for the call alone, never for the tools around it; the provider
/// that answers takes it, and says what it did not show.
/// </summary>
public sealed class RunImageTurn : IDisposable
{
    private static readonly AsyncLocal<RunImageTurn?> s_current = new();

    private readonly Action<IEnumerable<UnseenImage>> _note;
    private readonly string _reader;
    private readonly RunImageTurn? _enclosing;
    private int _taken;

    internal RunImageTurn(IReadOnlyList<CrewInputImage> images, string reader, Action<IEnumerable<UnseenImage>> note)
    {
        Images = images;
        _reader = reader;
        _note = note;
        _enclosing = s_current.Value;
        s_current.Value = this;
    }

    /// <summary>Gets the turn of the call in progress; null when it carries no image.</summary>
    public static RunImageTurn? Current => s_current.Value;

    /// <summary>Gets the images, by reference.</summary>
    public IReadOnlyList<CrewInputImage> Images { get; }

    /// <summary>Takes the turn for a provider: a model call made under it carries no image until the handle is disposed.</summary>
    /// <returns>The handle that restores the turn.</returns>
    public IDisposable Take()
    {
        Interlocked.Exchange(ref _taken, 1);
        var enclosing = s_current.Value;
        s_current.Value = null;
        return new Restored(enclosing);
    }

    /// <summary>Records an image a model that answered was not shown.</summary>
    /// <param name="path">The image's virtual path.</param>
    /// <param name="reason">Why, in words for a person.</param>
    public void NoteUnseen(string path, string reason) =>
        _note([new UnseenImage { Path = path, Reader = _reader, Reason = reason }]);

    /// <summary>Records that the call was answered: the images are unseen when no provider took the turn.</summary>
    public void Answered()
    {
        if (Volatile.Read(ref _taken) == 0)
            _note(Images.Select(i => new UnseenImage { Path = i.Path, Reader = _reader, Reason = "its model provider does not carry images" }));
    }

    /// <summary>Ends the call.</summary>
    public void Dispose() => s_current.Value = _enclosing;

    private sealed class Restored(RunImageTurn? turn) : IDisposable
    {
        public void Dispose() => s_current.Value = turn;
    }
}
