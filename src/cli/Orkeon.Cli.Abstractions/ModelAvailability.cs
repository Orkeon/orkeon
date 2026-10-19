namespace Orkeon.Cli.Abstractions;

/// <summary>Availability of a model as the host last observed it.</summary>
public enum ModelAvailability
{
    /// <summary>The last call succeeded, or none was made.</summary>
    Available,

    /// <summary>A retry is pending.</summary>
    Reconnecting,

    /// <summary>The last call failed.</summary>
    Unavailable,
}
