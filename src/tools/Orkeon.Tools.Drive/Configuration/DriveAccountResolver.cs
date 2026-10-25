using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Drive.Constants;
using Orkeon.Tools.Drive.Security;

namespace Orkeon.Tools.Drive.Configuration;

/// <summary>The outcome of resolving one declared account.</summary>
/// <param name="Account">The resolved account, or null when <paramref name="Problems"/> is not empty.</param>
/// <param name="Problems">Everything wrong with the declaration, each an actionable sentence.</param>
internal sealed record DriveAccountResolution(ResolvedDriveAccount? Account, IReadOnlyList<string> Problems);

/// <summary>
/// Applies the provider presets to a declared account and validates the result. Every problem
/// is collected, not only the first, so an operator fixes a declaration in one pass.
/// </summary>
internal static class DriveAccountResolver
{
    private const int MaxNameLength = 64;
    private const int MaxUploadMegabytesCeiling = 10 * 1024;

    /// <summary>Resolves <paramref name="options"/>, declared under <paramref name="name"/>.</summary>
    public static DriveAccountResolution Resolve(string name, DriveAccountOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);

        var problems = new List<string>();
        if (!IsValidName(name))
            problems.Add("the account name may only hold letters, digits, '.', '_' and '-', starts with a letter or a digit, and has 64 characters at most");

        if (options.Provider is null)
            problems.Add("Provider is required: GoogleDrive or OneDrive");
        if (options.Rights == DriveRights.None)
            problems.Add("Rights is required and says what an agent may do, e.g. \"Rights\": \"Read, Write, Organize\"");

        var root = ResolveRoot(options.Root, problems);
        var oauth = options.Provider is { } provider ? ResolveOAuth(provider, options, problems) : null;
        var share = ResolveShare(options, problems);
        var maxUpload = ResolveMaxUpload(options, problems);
        var timeout = ResolveTimeout(options, problems);

        // The problems name no account: whoever shows them says which (the registry's message,
        // the account list).
        if (problems.Count > 0 || options.Provider is null || oauth is null)
            return new DriveAccountResolution(null, problems);

        var account = new ResolvedDriveAccount
        {
            Name = name,
            Provider = options.Provider.Value,
            Rights = options.Rights,
            Root = root,
            OAuth = oauth,
            Share = share,
            MaxUploadBytes = maxUpload,
            Timeout = timeout,
        };
        return new DriveAccountResolution(account, []);
    }

    /// <summary>Whether <paramref name="name"/> is usable as an account name (and a token file stem).</summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxNameLength
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>
    /// The scopes an account asks for: read-only when it grants nothing but <c>Read</c>, so that a
    /// leaked token of a reading crew cannot write either.
    /// </summary>
    public static IReadOnlyList<string> Scopes(DriveProvider provider, DriveRights rights)
    {
        var readOnly = (rights & ~DriveRights.Read) == DriveRights.None;
        if (provider == DriveProvider.GoogleDrive)
            return [readOnly ? DriveDefaults.GoogleDriveReadOnlyScope : DriveDefaults.GoogleDriveScope];
        return [readOnly ? DriveDefaults.GraphFilesReadScope : DriveDefaults.GraphFilesReadWriteScope, DriveDefaults.OfflineAccessScope];
    }

    /// <summary>The path segments of <paramref name="root"/>; a problem when a segment is not a folder name.</summary>
    private static string[] ResolveRoot(string? root, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(root))
            return [];

        var segments = root.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.Any(char.IsControl) || segment.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0)
            {
                problems.Add($"Root '{root}' is not a folder path from the root of the drive (e.g. \"Orkeon/agents\"): '{segment}' is not a folder name");
                return [];
            }
        }

        return segments;
    }

    private static OAuthSettings? ResolveOAuth(DriveProvider provider, DriveAccountOptions options, List<string> problems)
    {
        var declared = options.Auth;
        var clientId = declared.ClientId?.Trim();
        if (string.IsNullOrEmpty(clientId))
        {
            problems.Add("Auth:ClientId is required (the id of your Google Cloud or Microsoft Entra application)");
            return null;
        }

        if (IsReference(declared.ClientSecretEnvVar, "Auth:ClientSecretEnvVar", problems))
            return null;

        var secretEnvVar = string.IsNullOrWhiteSpace(declared.ClientSecretEnvVar) ? null : declared.ClientSecretEnvVar.Trim();
        var scopes = Scopes(provider, options.Rights);
        if (provider == DriveProvider.GoogleDrive)
        {
            if (secretEnvVar is null)
                problems.Add("Auth:ClientSecretEnvVar is required for Google Drive: Google authenticates a desktop client's token requests with its client secret");
            if (!string.IsNullOrWhiteSpace(declared.Tenant))
                problems.Add("Auth:Tenant is a Microsoft setting; a Google Drive account has none");
            return new OAuthSettings
            {
                ClientId = clientId,
                ClientSecretEnvVar = secretEnvVar,
                Flow = OAuthFlow.LoopbackPkce,
                TokenEndpoint = DriveDefaults.GoogleTokenEndpoint,
                AuthorizationEndpoint = DriveDefaults.GoogleAuthorizationEndpoint,
                Scopes = scopes,
            };
        }

        var tenant = string.IsNullOrWhiteSpace(declared.Tenant) ? DriveDefaults.MicrosoftDefaultTenant : declared.Tenant.Trim();
        if (!tenant.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
        {
            problems.Add($"Auth:Tenant '{tenant}' is not a tenant alias or id");
            return null;
        }

        var authority = new Uri(DriveDefaults.MicrosoftAuthorityHost, $"{tenant}/oauth2/v2.0/");
        return new OAuthSettings
        {
            ClientId = clientId,
            ClientSecretEnvVar = secretEnvVar,
            Flow = OAuthFlow.DeviceCode,
            TokenEndpoint = new Uri(authority, "token"),
            AuthorizationEndpoint = new Uri(authority, "authorize"),
            DeviceCodeEndpoint = new Uri(authority, "devicecode"),
            Scopes = scopes,
            ScopesOnRefresh = true,
        };
    }

    // Orkeon expands no variable in a drive setting: the problem names the field, never the value.
    private static bool IsReference(string? value, string field, List<string> problems)
    {
        if (!SettingPlaceholder.TryMatch(value, out _))
            return false;
        problems.Add($"{field} holds a variable reference, and Orkeon expands no variable here: write the NAME of the environment variable alone");
        return true;
    }

    private static SharePolicy ResolveShare(DriveAccountOptions options, List<string> problems)
    {
        var declared = options.Share;
        var patterns = declared.AllowedRecipients
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .ToList();

        foreach (var pattern in patterns.Where(pattern => !RecipientPatterns.IsValidPattern(pattern)))
            problems.Add($"Share:AllowedRecipients entry '{pattern}' is neither an address, '*@domain' nor '*'");

        return new SharePolicy(patterns, declared.AllowPublicLinks ?? false);
    }

    private static long ResolveMaxUpload(DriveAccountOptions options, List<string> problems)
    {
        var megabytes = options.MaxUploadMegabytes ?? DriveDefaults.DefaultMaxUploadMegabytes;
        if (megabytes is <= 0 or > MaxUploadMegabytesCeiling)
        {
            problems.Add($"MaxUploadMegabytes must lie between 1 and {MaxUploadMegabytesCeiling}");
            return 0;
        }

        return megabytes * 1024L * 1024L;
    }

    private static TimeSpan? ResolveTimeout(DriveAccountOptions options, List<string> problems)
    {
        if (options.TimeoutSeconds is not { } seconds)
            return null;

        if (seconds <= 0)
        {
            problems.Add("TimeoutSeconds must be positive");
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
