namespace Orkeon.Tools.Email.Constants;

/// <summary>
/// Vendor endpoints, hosts and conventional ports of the account presets, as each provider
/// documents them (checked 2026-09-26). An explicit <c>Host</c>, <c>Port</c> or
/// <c>Security</c> in an account's settings always wins over these values.
/// </summary>
internal static class EmailDefaults
{
    /// <summary>The configuration section the family binds, relative to the root.</summary>
    public const string SectionName = Orkeon.Constants.Configuration.ConfigurationKeys.ToolsEmail;

    /// <summary>Named HTTP client used by the Graph backend and the OAuth2 client.</summary>
    public const string HttpClientName = "orkeon.email";

    /// <summary>Gmail IMAP host.</summary>
    public const string GmailImapHost = "imap.gmail.com";

    /// <summary>Gmail POP3 host.</summary>
    public const string GmailPop3Host = "pop.gmail.com";

    /// <summary>Gmail SMTP host.</summary>
    public const string GmailSmtpHost = "smtp.gmail.com";

    /// <summary>Outlook.com / Microsoft 365 IMAP host.</summary>
    public const string OutlookImapHost = "outlook.office365.com";

    /// <summary>Outlook.com / Microsoft 365 POP3 host.</summary>
    public const string OutlookPop3Host = "outlook.office365.com";

    /// <summary>Outlook.com SMTP submission host.</summary>
    public const string OutlookSmtpHost = "smtp-mail.outlook.com";

    /// <summary>IMAP over implicit TLS.</summary>
    public const int ImapSslPort = 993;

    /// <summary>IMAP with STARTTLS.</summary>
    public const int ImapStartTlsPort = 143;

    /// <summary>POP3 over implicit TLS.</summary>
    public const int Pop3SslPort = 995;

    /// <summary>POP3 with STARTTLS.</summary>
    public const int Pop3StartTlsPort = 110;

    /// <summary>SMTP submission over implicit TLS.</summary>
    public const int SmtpSslPort = 465;

    /// <summary>SMTP submission with STARTTLS.</summary>
    public const int SmtpStartTlsPort = 587;

    /// <summary>Google OAuth 2.0 authorization endpoint (installed-app flow).</summary>
    public static readonly Uri GoogleAuthorizationEndpoint = new("https://accounts.google.com/o/oauth2/v2/auth");

    /// <summary>Google OAuth 2.0 token endpoint.</summary>
    public static readonly Uri GoogleTokenEndpoint = new("https://oauth2.googleapis.com/token");

    /// <summary>The one scope Gmail grants IMAP, POP3 and SMTP access with.</summary>
    public const string GoogleMailScope = "https://mail.google.com/";

    /// <summary>Microsoft identity platform authority host.</summary>
    public static readonly Uri MicrosoftAuthorityHost = new("https://login.microsoftonline.com/");

    /// <summary>The tenant alias of personal Microsoft accounts (Outlook.com, Hotmail, Live).</summary>
    public const string MicrosoftDefaultTenant = "consumers";

    /// <summary>Microsoft Graph v1.0 base address.</summary>
    public static readonly Uri GraphBaseUri = new("https://graph.microsoft.com/v1.0/");

    /// <summary>Host every Graph paging link must point at before the bearer token is sent to it.</summary>
    public const string GraphHost = "graph.microsoft.com";

    /// <summary>Delegated Graph permission to read and organize mail.</summary>
    public const string GraphMailReadWriteScope = "https://graph.microsoft.com/Mail.ReadWrite";

    /// <summary>Delegated Graph permission to send mail.</summary>
    public const string GraphMailSendScope = "https://graph.microsoft.com/Mail.Send";

    /// <summary>Exchange Online IMAP scope.</summary>
    public const string OutlookImapScope = "https://outlook.office.com/IMAP.AccessAsUser.All";

    /// <summary>Exchange Online POP3 scope.</summary>
    public const string OutlookPopScope = "https://outlook.office.com/POP.AccessAsUser.All";

    /// <summary>Exchange Online SMTP AUTH scope.</summary>
    public const string OutlookSmtpScope = "https://outlook.office.com/SMTP.Send";

    /// <summary>Asks the Microsoft identity platform for a refresh token.</summary>
    public const string OfflineAccessScope = "offline_access";

    /// <summary>
    /// Microsoft Graph refuses a request body above 4 MB; a MIME message is sent base64-encoded,
    /// so roughly 3 MB of raw content fits.
    /// </summary>
    public const int GraphMaxRequestBytes = 4 * 1024 * 1024;

    /// <summary>How many messages <c>email_search</c> returns when the call does not say.</summary>
    public const int DefaultSearchLimit = 10;

    /// <summary>The largest page <c>email_search</c> accepts.</summary>
    public const int MaxSearchLimit = 50;

    /// <summary>Characters of body text <c>email_read</c> returns per call when the call does not say.</summary>
    public const int DefaultReadChars = 2500;

    /// <summary>
    /// The most body characters one <c>email_read</c> call returns. Sized with the headers and
    /// the security notice under the agent loop's default 4000-character tool-result cap.
    /// </summary>
    public const int MaxReadChars = 3000;

    /// <summary>Characters of preview text per message in a search page.</summary>
    public const int PreviewChars = 100;

    /// <summary>Messages a POP3 search scans, newest first, since POP3 has no server-side search.</summary>
    public const int Pop3ScanWindow = 200;
}
