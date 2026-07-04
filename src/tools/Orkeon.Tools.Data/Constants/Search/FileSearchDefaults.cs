namespace Orkeon.Tools.Data.Constants.Search;

/// <summary>
/// Default values for file-based semantic search operations.
/// </summary>
internal static class FileSearchDefaults
{
    /// <summary>Maximum allowed TopK value for search results.</summary>
    public const int MaxTopK = 100;

    /// <summary>Minimum allowed chunk size in characters.</summary>
    public const int MinChunkSize = 100;

    /// <summary>Maximum allowed chunk size in characters.</summary>
    public const int MaxChunkSize = 5000;
}
