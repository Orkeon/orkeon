namespace Orkeon.Tools.Chat.Configuration;

/// <summary>The chat service an account opens.</summary>
internal enum ChatProvider
{
    /// <summary>Microsoft Teams, through Microsoft Graph, as the signed-in user.</summary>
    Teams,

    /// <summary>A Microsoft Teams Workflows webhook. Refused: no provider serves it.</summary>
    TeamsWebhook,
}

/// <summary>What an agent may do with an account. Declared per account, never inferred.</summary>
[Flags]
internal enum ChatRights
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>List and read the allowed places, and their members.</summary>
    Read = 1,

    /// <summary>Post in the allowed places.</summary>
    Send = 2,

    /// <summary>Rewrite messages the account sent.</summary>
    Edit = 4,

    /// <summary>Delete messages the account sent.</summary>
    Delete = 8,

    /// <summary>Set and remove reactions.</summary>
    React = 16,

    /// <summary>Read meeting transcripts.</summary>
    Meetings = 32,
}

/// <summary>How an account authenticates.</summary>
internal enum ChatAuthMethod
{
    /// <summary>A user signs in.</summary>
    OAuth2,

    /// <summary>An application signs in as itself. Refused: no provider serves it.</summary>
    ClientCredentials,
}

/// <summary>The Microsoft cloud an account lives in.</summary>
internal enum ChatCloud
{
    /// <summary>The worldwide cloud.</summary>
    Global,

    /// <summary>Microsoft 365 US Government L4 (GCC High).</summary>
    UsGov,

    /// <summary>Microsoft 365 US Government L5 (DoD).</summary>
    UsGovDoD,

    /// <summary>The cloud operated by 21Vianet. Refused: Microsoft's pages give two sign-in hosts for it.</summary>
    China,
}

/// <summary>What a tenant administrator has consented to for the account's application.</summary>
[Flags]
internal enum ChatAdminGrants
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Reading a channel's messages.</summary>
    ChannelMessages = 1,

    /// <summary>Editing and deleting in a channel.</summary>
    ChannelEdit = 2,

    /// <summary>Reading meeting transcripts.</summary>
    Meetings = 4,
}
