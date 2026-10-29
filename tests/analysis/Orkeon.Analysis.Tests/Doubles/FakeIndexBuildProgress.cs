using System.Collections.Concurrent;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Tests.Doubles;

/// <summary>
/// Build progress sink that records every report; safe when files are parsed in parallel.
/// </summary>
public sealed class FakeIndexBuildProgress : IProgress<IndexBuildProgress>
{
    public ConcurrentQueue<IndexBuildProgress> Reports = new();

    public void Report(IndexBuildProgress value) => Reports.Enqueue(value);
}
