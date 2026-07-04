namespace Orkeon.Tools.FileSystem.Constants.File;

/// <summary>
/// Default values for file read/write operations.
/// </summary>
internal static class FileDefaults
{
    /// <summary>Default text encoding for file operations.</summary>
    public const string DefaultEncoding = "UTF-8";

    /// <summary>Timestamp format used when generating backup file names.</summary>
    public const string BackupTimestampFormat = "yyyyMMdd_HHmmss";

    /// <summary>Prefix inserted between the file name and the timestamp in backup file names.</summary>
    public const string BackupSuffixPrefix = ".backup_";

    /// <summary>Date/time format used when displaying file metadata dates.</summary>
    public const string DisplayDateTimeFormat = "yyyy-MM-dd HH:mm:ss 'UTC'";
}
