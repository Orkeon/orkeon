namespace Orkeon.Tools.Data.Constants.Database;

/// <summary>
/// Default values for relational database query operations.
/// </summary>
internal static class DatabaseDefaults
{
    /// <summary>Default maximum number of rows returned by a SELECT query.</summary>
    public const int DefaultMaxRows = 1000;

    /// <summary>Default query timeout in seconds.</summary>
    public const int DefaultTimeoutSeconds = 30;
}
