namespace Orkeon.Tools.Email.Configuration;

/// <summary>The provider preset an account starts from.</summary>
internal enum EmailProvider
{
    /// <summary>No preset: every host and port is explicit.</summary>
    Custom,

    /// <summary>Gmail and Google Workspace (IMAP/POP3 + SMTP).</summary>
    Gmail,

    /// <summary>Outlook.com, Hotmail, Live and Microsoft 365 (Graph by default).</summary>
    Outlook,
}

/// <summary>How an account reads mail.</summary>
internal enum IncomingProtocol
{
    /// <summary>IMAP4rev1 / IMAP4rev2.</summary>
    Imap,

    /// <summary>POP3: the inbox only, no folders.</summary>
    Pop3,

    /// <summary>Microsoft Graph REST API.</summary>
    Graph,
}

/// <summary>How an account sends mail.</summary>
internal enum OutgoingProtocol
{
    /// <summary>SMTP submission.</summary>
    Smtp,

    /// <summary>Microsoft Graph <c>sendMail</c>.</summary>
    Graph,
}

/// <summary>Transport security of an IMAP, POP3 or SMTP connection.</summary>
internal enum TransportSecurity
{
    /// <summary>TLS from the first byte (ports 993, 995, 465).</summary>
    SslOnConnect,

    /// <summary>Plain connection upgraded with STARTTLS, which is then mandatory.</summary>
    StartTls,

    /// <summary>No encryption: accepted only towards a loopback address (a local test server).</summary>
    None,
}

/// <summary>How an account authenticates.</summary>
internal enum EmailAuthMethod
{
    /// <summary>A password (an app password for Gmail) read from a named environment variable.</summary>
    Password,

    /// <summary>OAuth 2.0 with a token obtained by <c>orkeon email login</c>.</summary>
    OAuth2,
}

/// <summary>What an agent may do with an account. Declared per account, never inferred.</summary>
[Flags]
internal enum EmailRights
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>List folders, search, read messages and save attachments.</summary>
    Read = 1,

    /// <summary>Create and rename folders, move messages, set read/flagged marks.</summary>
    Organize = 2,

    /// <summary>Save drafts in the mailbox without sending them.</summary>
    Draft = 4,

    /// <summary>Send messages, to the allowed recipients only.</summary>
    Send = 8,

    /// <summary>Move messages to the trash.</summary>
    Delete = 16,

    /// <summary>Delete messages permanently.</summary>
    Purge = 32,
}

/// <summary>How an OAuth 2.0 account obtains its first token.</summary>
internal enum OAuthFlow
{
    /// <summary>RFC 8628 device authorization grant (Microsoft).</summary>
    DeviceCode,

    /// <summary>Authorization code with PKCE and a loopback redirect (Google).</summary>
    LoopbackPkce,
}
