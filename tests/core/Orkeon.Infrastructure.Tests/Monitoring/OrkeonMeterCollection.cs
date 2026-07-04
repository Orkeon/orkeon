namespace Orkeon.Infrastructure.Tests.Monitoring;

/// <summary>
/// Groups all test classes that observe the process-global "Orkeon" <see cref="System.Diagnostics.Metrics.Meter"/>
/// via <c>MetricsAggregationService</c>'s <c>MeterListener</c>. These tests MUST NOT run in parallel with
/// each other: one class recording measurements would be observed by another class's listener (the meter is
/// global by name), breaking assertions that expect a pristine counter value.
/// </summary>
[CollectionDefinition("OrkeonMeter", DisableParallelization = true)]
public sealed class OrkeonMeterCollection;
