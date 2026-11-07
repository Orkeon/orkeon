namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// What a task streams while its output phase is guarded (FWK-19): the deltas of its turns are held
/// instead of written to the run's stream and the host's sink, then released once the guardian allows the
/// output, or dropped when it blocks — a blocked output is never shown, streamed or kept in the run's
/// history. The hold is ambient to the task's async flow, like <see cref="CrewStreamScope"/>.
/// </summary>
internal sealed class OutputHold
{
    private static readonly AsyncLocal<OutputHold?> Ambient = new();

    private readonly Lock _gate = new();
    private List<Action>? _held = [];

    /// <summary>The hold of the task in progress, or null when its output is not guarded.</summary>
    public static OutputHold? Current => Ambient.Value;

    /// <summary>Opens the hold of the task that is about to run; disposing the handle restores the enclosing one.</summary>
    /// <param name="hold">The hold, which the caller releases or drops.</param>
    /// <returns>The handle that restores the enclosing hold.</returns>
    public static IDisposable Begin(out OutputHold hold)
    {
        var enclosing = Ambient.Value;
        hold = new OutputHold();
        Ambient.Value = hold;
        return new Scope(enclosing);
    }

    /// <summary>Keeps one emission until the verdict; runs it at once when the hold is already released.</summary>
    /// <param name="emission">What writes the delta where it goes.</param>
    public void Hold(Action emission)
    {
        ArgumentNullException.ThrowIfNull(emission);
        lock (_gate)
        {
            if (_held is not null)
            {
                _held.Add(emission);
                return;
            }
        }

        emission();
    }

    /// <summary>The output was allowed: writes every held emission, in order; later ones pass straight through.</summary>
    public void Release()
    {
        List<Action>? held;
        lock (_gate)
        {
            held = _held;
            _held = null;
        }

        if (held is null)
            return;
        foreach (var emission in held)
            emission();
    }

    /// <summary>The output was refused, or never judged: forgets every held emission; later ones are forgotten too.</summary>
    public void Drop()
    {
        lock (_gate)
        {
            if (_held is not null)
                _held = [];
        }
    }

    private sealed class Scope(OutputHold? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
