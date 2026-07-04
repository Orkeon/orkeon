namespace Orkeon.Application.Constants.Configuration;

/// <summary>
/// Default values for application path configuration.
/// Centralizes magic strings used in application options and persistence settings.
/// </summary>
public static class PathDefaults
{
    /// <summary>
    /// Default path to crew configuration files.
    /// </summary>
    public const string DefaultCrewsPath = "crews";

    /// <summary>
    /// Default path to the SQLite database used for memory persistence.
    /// </summary>
    public const string DefaultMemoryDatabasePath = "orkeon_memory.db";
}
