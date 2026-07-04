using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Configuration;

/// <summary>Main configuration for Orkeon system.</summary>
public sealed record OrkeonConfig
{
    /// <summary>Gets the version of this configuration.</summary>
    public string Version { get; init; } = "1.0.0";
    /// <summary>Gets the execution configuration.</summary>
    public ExecutionConfig Execution { get; init; } = new();
    /// <summary>Gets the memory configuration.</summary>
    public MemoryConfig Memory { get; init; } = new();
    /// <summary>Gets the telemetry configuration.</summary>
    public TelemetryConfig Telemetry { get; init; } = new();
    /// <summary>Gets the feature flags.</summary>
    public OrkeonFeatureFlags FeatureFlags { get; init; } = new();
    /// <summary>Gets custom settings.</summary>
    public Dictionary<string, object> CustomSettings { get; init; } = [];
    /// <summary>Gets the timestamp when this configuration was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>Gets the timestamp when this configuration was last updated.</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Gets the default configuration.</summary>
    public static OrkeonConfig Default => new();

    /// <summary>Gets a development-optimized configuration.</summary>
    public static OrkeonConfig Development => new()
    {
        Execution = new ExecutionConfig { EnableDebugMode = true },
        Memory = new MemoryConfig { Type = MemoryStorageType.InMemory },
        Telemetry = new TelemetryConfig { LogLevel = TelemetryLogLevel.Debug },
        FeatureFlags = OrkeonFeatureFlags.Development
    };

    /// <summary>Gets a production-optimized configuration.</summary>
    public static OrkeonConfig Production => new()
    {
        Execution = new ExecutionConfig { EnableDebugMode = false },
        Memory = new MemoryConfig { Type = MemoryStorageType.Redis },
        Telemetry = new TelemetryConfig { LogLevel = TelemetryLogLevel.Warning },
        FeatureFlags = OrkeonFeatureFlags.Production
    };
}
