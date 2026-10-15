namespace Orkeon.Application.Tests.Telemetry;

/// <summary>
/// Groups the test classes that observe the process-global "Orkeon" meter, so that one class
/// recording measurements is not observed by another class's listener at the same time.
/// </summary>
[CollectionDefinition("OrkeonMeter", DisableParallelization = true)]
public sealed class OrkeonMeterCollection;
