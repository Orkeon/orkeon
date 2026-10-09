namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// What an agent may do with an account (STUDIO-65): the six rights the engine's
/// <c>EmailRights</c> declares, with the same values, so that the numeric spelling the binder
/// accepts reads the same here. Written as the engine reads it, <c>"Read, Organize, Draft"</c>.
/// </summary>
[Flags]
public enum EmailRights
{
    /// <summary>Nothing granted: the engine refuses the account.</summary>
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
