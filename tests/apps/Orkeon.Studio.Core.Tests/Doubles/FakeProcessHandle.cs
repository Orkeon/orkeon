using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IProcessHandle"/> recording the order of the termination steps, so
/// the "signal first, kill second" contract can be asserted without spawning anything.
/// </summary>
public sealed class FakeProcessHandle : IProcessHandle
{
    /// <summary>Call names in order: <c>graceful-stop</c>, <c>wait</c>, <c>kill</c>.</summary>
    public List<string> Calls { get; } = new();

    /// <summary>Timeouts passed to <see cref="WaitForExitAsync"/>, in call order.</summary>
    public List<TimeSpan> WaitTimeouts { get; } = new();

    /// <inheritdoc />
    public int ProcessId { get; set; } = 4242;

    /// <inheritdoc />
    public bool HasExited { get; set; }

    /// <summary>Whether this platform is modelled as offering a graceful stop.</summary>
    public bool GracefulStopSucceeds { get; set; } = true;

    /// <summary>Message returned when <see cref="GracefulStopSucceeds"/> is false.</summary>
    public string GracefulStopFailure { get; set; } = "no graceful stop on this platform";

    /// <summary>When true the modelled process honours the signal and exits.</summary>
    public bool ExitsOnGracefulStop { get; set; }

    /// <summary>Number of graceful stop requests received.</summary>
    public int GracefulStopRequests { get; private set; }

    /// <summary>True once <see cref="Kill"/> was called.</summary>
    public bool WasKilled { get; private set; }

    /// <inheritdoc />
    public bool TryRequestGracefulStop(out string? failureReason)
    {
        Calls.Add("graceful-stop");
        GracefulStopRequests++;

        if (!GracefulStopSucceeds)
        {
            failureReason = GracefulStopFailure;
            return false;
        }

        if (ExitsOnGracefulStop)
            HasExited = true;

        failureReason = null;
        return true;
    }

    /// <inheritdoc />
    public void Kill()
    {
        Calls.Add("kill");
        WasKilled = true;
        HasExited = true;
    }

    /// <inheritdoc />
    public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Calls.Add("wait");
        WaitTimeouts.Add(timeout);
        return Task.FromResult(HasExited);
    }
}
