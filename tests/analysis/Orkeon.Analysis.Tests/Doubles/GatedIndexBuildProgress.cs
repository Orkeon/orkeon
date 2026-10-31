using System.Collections.Concurrent;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Tests.Doubles;

/// <summary>
/// Build progress sink that holds the first Parse report (Current 1) until another worker has reached its own
/// Parse report, so a later worker reports first. The hold is released by that second report, or by
/// a hang guard when the builder serialises its emissions.
/// </summary>
public sealed class GatedIndexBuildProgress : IProgress<IndexBuildProgress>, IDisposable
{
    public void Dispose() => _secondWorkerArrived.Dispose();

    // Only releases the gate when no later report arrives; the assertion is on order, not on duration.
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(1);
    private readonly ManualResetEventSlim _secondWorkerArrived = new(false);

    public ConcurrentQueue<IndexBuildProgress> Reports = new();

    public void Report(IndexBuildProgress value)
    {
        if (value.Phase == IndexBuildPhase.Parse)
        {

            if (value.Current == 1)
            {
                _secondWorkerArrived.Wait(HangGuard);
            }
            else
            {
                Reports.Enqueue(value);
                _secondWorkerArrived.Set();
                return;
            }
        }

        Reports.Enqueue(value);
    }
}
