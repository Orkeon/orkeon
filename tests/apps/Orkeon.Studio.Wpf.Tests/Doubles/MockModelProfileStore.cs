using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// An <see cref="IModelProfileStore"/> over another one — the real file store, typically — that
/// counts the writes and keeps the last one, so a test can await what a gesture persisted and tell
/// a startup that wrote nothing. <see cref="FailNext"/> refuses the next write without reaching the
/// inner store, as a read-only file or a set the serializer refuses would (STUDIO-56).
/// </summary>
public sealed class MockModelProfileStore(IModelProfileStore inner) : IModelProfileStore
{
    /// <summary>How many sets were written.</summary>
    public int Saves { get; private set; }

    /// <summary>The last write, complete once the set is on its way to the inner store's file.</summary>
    public Task LastSave { get; private set; } = Task.CompletedTask;

    /// <summary>When set, the next write fails with it — then the writes go through again.</summary>
    public Exception? FailNext { get; set; }

    /// <inheritdoc />
    public Task<ModelProfileLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    /// <inheritdoc />
    public Task SaveAsync(ModelProfileSet profiles, CancellationToken cancellationToken = default)
    {
        Saves++;
        if (FailNext is { } failure)
        {
            FailNext = null;
            return LastSave = Task.FromException(failure);
        }

        return LastSave = inner.SaveAsync(profiles, cancellationToken);
    }
}
