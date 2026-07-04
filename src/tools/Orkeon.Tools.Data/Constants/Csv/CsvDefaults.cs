namespace Orkeon.Tools.Data.Constants.Csv;

/// <summary>
/// Default values for CSV reading operations.
/// </summary>
internal static class CsvDefaults
{
    /// <summary>Default column delimiter for CSV files.</summary>
    public const string DefaultDelimiter = ",";

    /// <summary>Prefix used for auto-generated column names when no header is present.</summary>
    public const string UnnamedColumnPrefix = "column_";
}
