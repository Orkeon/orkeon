namespace Orkeon.Tools.Drive.Configuration;

/// <summary>The cloud drive an account opens.</summary>
internal enum DriveProvider
{
    /// <summary>Google Drive ("My Drive"), through the Drive API v3.</summary>
    GoogleDrive,

    /// <summary>OneDrive, personal or for work, through Microsoft Graph.</summary>
    OneDrive,
}

/// <summary>What an agent may do with an account. Declared per account, never inferred.</summary>
[Flags]
internal enum DriveRights
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>List, search, read and download files.</summary>
    Read = 1,

    /// <summary>Upload files, replace their content and create folders.</summary>
    Write = 2,

    /// <summary>Move and rename files and folders.</summary>
    Organize = 4,

    /// <summary>Share files with the allowed recipients, and create public links when allowed.</summary>
    Share = 8,

    /// <summary>Move files and folders to the trash (the recycle bin).</summary>
    Delete = 16,

    /// <summary>Delete files and folders permanently.</summary>
    Purge = 32,
}
