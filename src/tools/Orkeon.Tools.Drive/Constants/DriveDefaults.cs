namespace Orkeon.Tools.Drive.Constants;

/// <summary>
/// Vendor endpoints, scopes and limits of the two providers, as each documents them (checked
/// 2026-10-17), and the sizes of the tools' answers.
/// </summary>
internal static class DriveDefaults
{
    /// <summary>The configuration section the family binds, relative to the root.</summary>
    public const string SectionName = Orkeon.Constants.Configuration.ConfigurationKeys.ToolsDrive;

    /// <summary>Named HTTP client used by both engines and the OAuth2 client.</summary>
    public const string HttpClientName = "orkeon.drive";

    /// <summary>The guide every configuration problem points at.</summary>
    public const string GuidePath = "docs/guides/drive.md";

    /// <summary>Google OAuth 2.0 authorization endpoint (installed-app flow).</summary>
    public static readonly Uri GoogleAuthorizationEndpoint = new("https://accounts.google.com/o/oauth2/v2/auth");

    /// <summary>Google OAuth 2.0 token endpoint.</summary>
    public static readonly Uri GoogleTokenEndpoint = new("https://oauth2.googleapis.com/token");

    /// <summary>Full access to the files of the user's Drive.</summary>
    public const string GoogleDriveScope = "https://www.googleapis.com/auth/drive";

    /// <summary>Read-only access to the files of the user's Drive.</summary>
    public const string GoogleDriveReadOnlyScope = "https://www.googleapis.com/auth/drive.readonly";

    /// <summary>Drive API v3 metadata base address.</summary>
    public static readonly Uri GoogleApiBase = new("https://www.googleapis.com/drive/v3/");

    /// <summary>Drive API v3 upload base address.</summary>
    public static readonly Uri GoogleUploadBase = new("https://www.googleapis.com/upload/drive/v3/");

    /// <summary>Host every Google request and paging link must point at before the bearer token is sent to it.</summary>
    public const string GoogleApiHost = "www.googleapis.com";

    /// <summary>Microsoft identity platform authority host.</summary>
    public static readonly Uri MicrosoftAuthorityHost = new("https://login.microsoftonline.com/");

    /// <summary>The tenant alias of personal Microsoft accounts (OneDrive personal).</summary>
    public const string MicrosoftDefaultTenant = "consumers";

    /// <summary>Microsoft Graph v1.0 base address.</summary>
    public static readonly Uri GraphBaseUri = new("https://graph.microsoft.com/v1.0/");

    /// <summary>Host every Graph request and paging link must point at before the bearer token is sent to it.</summary>
    public const string GraphHost = "graph.microsoft.com";

    /// <summary>Delegated Graph permission to read the user's files.</summary>
    public const string GraphFilesReadScope = "https://graph.microsoft.com/Files.Read";

    /// <summary>Delegated Graph permission to read and write the user's files.</summary>
    public const string GraphFilesReadWriteScope = "https://graph.microsoft.com/Files.ReadWrite";

    /// <summary>Asks the Microsoft identity platform for a refresh token.</summary>
    public const string OfflineAccessScope = "offline_access";

    /// <summary>The MIME type of a Google Drive folder.</summary>
    public const string GoogleFolderMimeType = "application/vnd.google-apps.folder";

    /// <summary>The MIME type prefix of Google's own documents (Docs, Sheets, Slides…), which have no bytes of their own.</summary>
    public const string GoogleNativePrefix = "application/vnd.google-apps.";

    /// <summary>OneDrive takes a file in a single PUT up to this size; above, it goes through an upload session.</summary>
    public const long OneDriveSimpleUploadBytes = 4L * 1024 * 1024;

    /// <summary>One chunk of a OneDrive upload session: 10 MiB, a multiple of 320 KiB as Graph requires.</summary>
    public const int OneDriveChunkBytes = 32 * 320 * 1024;

    /// <summary>
    /// One chunk of a Google resumable upload: 8 MiB, a multiple of 256 KiB as the Drive API
    /// requires. Each chunk is one request, so no request outlives the HTTP timeout however large
    /// the file; a file no larger than one chunk goes in a single request.
    /// </summary>
    public const int GoogleChunkBytes = 32 * 256 * 1024;

    /// <summary>The largest upload an account accepts when it does not say.</summary>
    public const int DefaultMaxUploadMegabytes = 100;

    /// <summary>The most bytes <c>drive_read</c> fetches to extract text from; a larger file is downloaded instead.</summary>
    public const long MaxReadBytes = 2L * 1024 * 1024;

    /// <summary>How many ancestors a root check climbs before it gives up (a Drive folder tree is rarely deeper).</summary>
    public const int MaxRootDepth = 32;

    /// <summary>How many items <c>drive_list</c> and <c>drive_search</c> return when the call does not say.</summary>
    public const int DefaultListLimit = 20;

    /// <summary>
    /// How many provider pages one <c>drive_search</c> call reads to fill its page with items from
    /// inside the account's root, which neither provider can search alone.
    /// </summary>
    public const int MaxSearchPages = 5;

    /// <summary>The largest page <c>drive_list</c> and <c>drive_search</c> accept.</summary>
    public const int MaxListLimit = 50;

    /// <summary>Characters of text <c>drive_read</c> returns per call when the call does not say.</summary>
    public const int DefaultReadChars = 2500;

    /// <summary>
    /// The most text characters one <c>drive_read</c> call returns. Sized with the metadata and the
    /// security notice under the agent loop's default 4000-character tool-result cap.
    /// </summary>
    public const int MaxReadChars = 3000;
}
