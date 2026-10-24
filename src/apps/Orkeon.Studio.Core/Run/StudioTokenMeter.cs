using Orkeon.Studio.Core.Events;

namespace Orkeon.Studio.Core.Run;

/// <summary>Which kind of execution an <see cref="ExecutionMeter"/> counts.</summary>
public enum ExecutionKind
{
    Run,
    Forge,
    Rephrase,
}

/// <summary>Tokens up and down; <see cref="Estimated"/> marks a figure that holds an estimate (« ~ »).</summary>
public sealed record TokenFigure(long Up, long Down, bool Estimated);

/// <summary>
/// Counts every model call of the session, live: per execution, the provider's latest exact
/// cumulative plus the estimates of the calls not yet counted — an estimate is replaced by the
/// provider's count, never added to it.
/// </summary>
public sealed class StudioTokenMeter
{
    private readonly object _gate = new();
    private readonly List<ExecutionMeter> _executions = [];

    /// <summary>Every execution since the meter was created, summed.</summary>
    public TokenFigure Session
    {
        get
        {
            lock (_gate)
            {
                var figures = _executions.Select(execution => execution.Figure).ToList();
                return new TokenFigure(
                    figures.Sum(figure => figure.Up),
                    figures.Sum(figure => figure.Down),
                    figures.Any(figure => figure.Estimated));
            }
        }
    }

    /// <summary>The latest execution begun among the kinds; null when none was.</summary>
    public ExecutionMeter? Latest(params ExecutionKind[] kinds)
    {
        lock (_gate)
            return _executions.LastOrDefault(execution => kinds.Contains(execution.Kind));
    }

    /// <summary>Raised on each applied line, on the thread that applied it.</summary>
    public event EventHandler? Changed;

    /// <summary>Opens the count of a new execution.</summary>
    public ExecutionMeter Begin(ExecutionKind kind)
    {
        var execution = new ExecutionMeter(kind, this);
        lock (_gate)
            _executions.Add(execution);
        return execution;
    }

    internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>The count of one execution.</summary>
public sealed class ExecutionMeter
{
    private const string Buffer = "Buffer";
    private const string Stream = "Stream";

    private readonly object _gate = new();
    private readonly StudioTokenMeter _owner;
    private readonly Dictionary<string, Call> _inFlight = [];
    private long _exactUp;
    private long _exactDown;
    private bool _costEstimated;
    private long _keptUp;
    private long _keptDown;
    private bool _kept;
    private long _order;
    private string? _lastFinishedMode;
    private bool _lastFinishedFellBack;

    internal ExecutionMeter(ExecutionKind kind, StudioTokenMeter owner)
    {
        Kind = kind;
        _owner = owner;
    }

    public ExecutionKind Kind { get; }

    /// <summary>Whether <see cref="End"/> was called: the execution is over.</summary>
    public bool Ended { get; private set; }

    /// <summary>The exact count plus the estimates of the calls without a provider count.</summary>
    public TokenFigure Figure
    {
        get
        {
            lock (_gate)
            {
                var up = _exactUp + _keptUp + _inFlight.Values.Sum(call => call.Up);
                var down = _exactDown + _keptDown + _inFlight.Values.Sum(call => call.Down);
                return new TokenFigure(up, down, _costEstimated || _kept || _inFlight.Count > 0);
            }
        }
    }

    /// <summary>
    /// The mode (<c>Stream</c> or <c>Buffer</c>) of the latest started call in flight, else of the
    /// last finished call; null before any call.
    /// </summary>
    public string? CurrentMode
    {
        get
        {
            lock (_gate)
                return _inFlight.Values.MaxBy(call => call.Order)?.Mode ?? _lastFinishedMode;
        }
    }

    /// <summary>Whether the current mode is a fallback: the last finished call fell back to Buffer.</summary>
    public bool CurrentFellBack
    {
        get
        {
            lock (_gate)
                return _inFlight.Count == 0 && _lastFinishedFellBack;
        }
    }

    /// <summary>Folds one line in; a line that carries no count is ignored.</summary>
    public void Apply(OrkeonEvent line)
    {
        ArgumentNullException.ThrowIfNull(line);

        bool applied;
        lock (_gate)
        {
            applied = line.Kind switch
            {
                Orkeon.Constants.Protocol.RunEventKinds.LlmCallStarted => Started(line),
                Orkeon.Constants.Protocol.RunEventKinds.LlmCallProgress => Progressed(line),
                Orkeon.Constants.Protocol.RunEventKinds.LlmCallFinished => Finished(line),
                Orkeon.Constants.Protocol.RunEventKinds.CostUpdated => Costed(line),
                _ => false,
            };
        }

        if (applied)
            _owner.RaiseChanged();
    }

    /// <summary>The execution is over: a call that never reported its end no longer counts.</summary>
    public void End()
    {
        lock (_gate)
        {
            _inFlight.Clear();
            Ended = true;
        }

        _owner.RaiseChanged();
    }

    private bool Started(OrkeonEvent line)
    {
        if (line.GetString("callId") is not { } id)
            return false;

        _inFlight[id] = new Call(NormalizeMode(line.GetString("mode")), ++_order)
        {
            Up = line.GetInt64("estimatedPromptTokens") ?? 0,
        };
        return true;
    }

    private bool Progressed(OrkeonEvent line)
    {
        if (line.GetString("callId") is not { } id || !_inFlight.TryGetValue(id, out var call))
            return false;

        call.Down = line.GetInt64("estimatedCompletionTokens") ?? call.Down;
        return true;
    }

    private bool Finished(OrkeonEvent line)
    {
        if (line.GetString("callId") is not { } id)
            return false;

        _inFlight.Remove(id, out var call);
        _lastFinishedMode = NormalizeMode(line.GetString("mode")) ?? call?.Mode;
        _lastFinishedFellBack = line.GetBool("fellBack") ?? false;

        var counted = line.GetInt64("promptTokens") is not null || line.GetInt64("completionTokens") is not null;
        if (!counted && call is not null)
        {
            _keptUp += call.Up;
            _keptDown += call.Down;
            _kept = true;
        }

        return true;
    }

    private bool Costed(OrkeonEvent line)
    {
        _exactUp = line.GetInt64("promptTokens") ?? _exactUp;
        _exactDown = line.GetInt64("completionTokens") ?? _exactDown;
        _costEstimated = line.GetInt64("estimatedTokens") is > 0;
        return true;
    }

    private static string? NormalizeMode(string? mode)
    {
        if (string.Equals(mode, Stream, StringComparison.OrdinalIgnoreCase))
            return Stream;
        if (string.Equals(mode, Buffer, StringComparison.OrdinalIgnoreCase))
            return Buffer;
        return null;
    }

    private sealed class Call
    {
        public Call(string? mode, long order)
        {
            Mode = mode;
            Order = order;
        }

        public string? Mode { get; }

        public long Order { get; }

        public long Up { get; init; }

        public long Down { get; set; }
    }
}
