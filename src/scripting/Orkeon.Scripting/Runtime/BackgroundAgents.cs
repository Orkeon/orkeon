using Jint;
using Jint.Native;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The background children one script context started (<c>ctx.spawnBackground</c>) and the inbox
/// their completions land in. Owned by the script context, not by an <c>act</c> call: a ticket made
/// before an <c>act</c> counts for it, and two acts in sequence read the same inbox. Each completion
/// is a user-role message, delivered once (<see cref="Drain"/>).
/// </summary>
internal sealed class BackgroundAgents
{
    private readonly Engine _engine;
    private readonly SpawnAgentScriptTool _spawn;
    private readonly CancellationToken _ct;
    private readonly GatePermit? _permit;
    private readonly Lock _sync = new();
    private readonly Queue<string> _messages = new();
    private readonly Dictionary<int, (CancellationTokenSource Cts, object? Owner)> _running = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<object> _acts = [];
    private int _nextTicket;

    internal BackgroundAgents(Engine engine, SpawnAgentScriptTool spawn, GatePermit? permit, CancellationToken ct)
    {
        _permit = permit;
        _engine = engine;
        _spawn = spawn;
        _ct = ct;
    }

    /// <summary>Test seam over the gate permit this instance holds.</summary>
    internal GatePermit? Permit => _permit;

    /// <summary>Whether a child is still running or a completion waits to be delivered.</summary>
    internal bool HasPendingWork
    {
        get { lock (_sync) return _running.Count > 0 || _messages.Count > 0; }
    }

    /// <summary>Starts <paramref name="agent"/> on the engine thread under the crew gate; returns its ticket.</summary>
    internal JsValue Spawn(string agent, string input)
    {
        _ct.ThrowIfCancellationRequested();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
        JsValue promise;
        try { promise = _spawn.Start(agent, input, inheritPermit: false, cts.Token); }
        catch
        {
            cts.Dispose();
            throw;
        }

        int id;
        lock (_sync)
        {
            id = ++_nextTicket;
            _running[id] = (cts, _acts.Count > 0 ? _acts[^1] : null);
        }

        var onCompleted = new Action<JsValue>(output => Settle(id, $"agent {agent} completed: {(output.IsString() ? output.AsString() : output.ToString())}"));
        var onFailed = new Action<JsValue>(error => Settle(id, $"agent {agent} failed: {FailureMessage(error)}"));
        _engine.Invoke(promise.Get("then"), promise, [JsValue.FromObject(_engine, onCompleted), JsValue.FromObject(_engine, onFailed)]);

        var ticket = new Jint.Native.JsObject(_engine);
        ticket.Set("id", id);
        ticket.Set("agent", agent);
        return ticket;
    }

    /// <summary>The completions posted since the last call, oldest first.</summary>
    internal List<string> Drain()
    {
        lock (_sync)
        {
            var drained = new List<string>(_messages);
            _messages.Clear();
            return drained;
        }
    }

    /// <summary>Waits until a completion is posted or no child is left running.</summary>
    internal async Task WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (_sync)
            {
                if (_messages.Count > 0 || _running.Count == 0) return;
                changed = _changed.Task;
            }
            // The permit is handed back for the wait, so the children it waits for can take it, and
            // taken again before the next model turn.
            _permit?.Release();
            try { await changed.WaitAsync(cancellationToken).ConfigureAwait(false); }
            finally
            {
                if (_permit is not null) await _permit.AcquireAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Opens an act: children started until it ends, while it is the innermost one, belong to it.</summary>
    internal object BeginAct()
    {
        var act = new object();
        lock (_sync) _acts.Add(act);
        return act;
    }

    /// <summary>Closes <paramref name="act"/> and cancels the children it owns; children started outside any act stay.</summary>
    internal void EndAct(object act)
    {
        lock (_sync)
        {
            _acts.Remove(act);
            foreach (var running in _running.Values.Where(running => ReferenceEquals(running.Owner, act)))
                running.Cts.Cancel();
        }
    }

    private void Settle(int id, string message)
    {
        lock (_sync)
        {
            if (!_running.Remove(id, out var running)) return;
            running.Cts.Dispose();
            _messages.Enqueue(message);
            var settled = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            settled.TrySetResult();
        }
    }

    /// <summary>The displayed message of a rejection: the innermost CLR message, or a script error's own.</summary>
    internal static string FailureMessage(JsValue error)
    {
        if (JsHostError.Unwrap(error) is { } clr)
            return JsExceptionUnwrap.UnwrapToInnermost(clr).Message;
        if (error.IsObject() && error.AsObject().Get("message") is { } message && message.IsString())
            return message.AsString();
        return error.ToString();
    }
}
