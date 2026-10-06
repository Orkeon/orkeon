using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Email.Dtos;

/// <summary>Request of <c>email_create_folder</c>.</summary>
internal sealed record EmailCreateFolderRequest
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Folder path.</summary>
    [JsonPropertyName("path")]
    [FieldSchema(Description = "Folder path, '/'-separated; missing parents are created too", Example = "Clients/ACME")]
    public string Path { get; init; } = "";
}

/// <summary>Response of <c>email_create_folder</c>.</summary>
internal sealed record EmailCreateFolderResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Created or already there.</summary>
    [JsonPropertyName("created")]
    [ReturnSchema(Description = "False when the folder already existed")]
    public bool Created { get; init; }

    /// <summary>The folder.</summary>
    [JsonPropertyName("folder")]
    [ReturnSchema(Description = "The folder")]
    public EmailFolderDto Folder { get; init; } = new();
}

/// <summary>Request of <c>email_rename_folder</c>.</summary>
internal sealed record EmailRenameFolderRequest
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Folder path.</summary>
    [JsonPropertyName("path")]
    [FieldSchema(Description = "Path of the folder to rename (system folders are refused)")]
    public string Path { get; init; } = "";

    /// <summary>New name.</summary>
    [JsonPropertyName("new_name")]
    [FieldSchema(Description = "New name of the last segment, without '/'")]
    public string NewName { get; init; } = "";
}

/// <summary>Response of <c>email_rename_folder</c>.</summary>
internal sealed record EmailRenameFolderResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Old path.</summary>
    [JsonPropertyName("previous_path")]
    [ReturnSchema(Description = "Path before the rename")]
    public string PreviousPath { get; init; } = "";

    /// <summary>The renamed folder.</summary>
    [JsonPropertyName("folder")]
    [ReturnSchema(Description = "The folder after the rename")]
    public EmailFolderDto Folder { get; init; } = new();
}

/// <summary>Request of <c>email_move</c>.</summary>
internal sealed record EmailMoveRequest
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Message ids.</summary>
    [JsonPropertyName("ids")]
    [FieldSchema(Description = "Message ids exactly as email_search returned them")]
    public IReadOnlyList<string> Ids { get; init; } = [];

    /// <summary>Destination.</summary>
    [JsonPropertyName("destination")]
    [FieldSchema(Description = "Destination folder path or role (archive, junk, inbox…)", Example = "Clients/ACME")]
    public string Destination { get; init; } = "";
}

/// <summary>One moved message.</summary>
internal sealed record MovedMessageDto
{
    /// <summary>Id before the move.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "Id before the move")]
    public string Id { get; init; } = "";

    /// <summary>Id after the move.</summary>
    [JsonPropertyName("new_id")]
    [ReturnSchema(Description = "Id after the move; null when the server does not say (search the destination)")]
    public string? NewId { get; init; }
}

/// <summary>Response of <c>email_move</c>.</summary>
internal sealed record EmailMoveResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Destination.</summary>
    [JsonPropertyName("destination")]
    [ReturnSchema(Description = "Destination folder")]
    public string Destination { get; init; } = "";

    /// <summary>Moved messages.</summary>
    [JsonPropertyName("moved")]
    [ReturnSchema(Description = "Moved messages with their new ids")]
    public IReadOnlyList<MovedMessageDto> Moved { get; init; } = [];
}

/// <summary>Request of <c>email_mark</c>.</summary>
internal sealed record EmailMarkRequest
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Message ids.</summary>
    [JsonPropertyName("ids")]
    [FieldSchema(Description = "Message ids exactly as email_search returned them")]
    public IReadOnlyList<string> Ids { get; init; } = [];

    /// <summary>Read mark.</summary>
    [JsonPropertyName("seen")]
    [FieldSchema(Description = "true marks read, false marks unread; omit to leave it", IsRequired = false)]
    public bool? Seen { get; init; }

    /// <summary>Flagged mark.</summary>
    [JsonPropertyName("flagged")]
    [FieldSchema(Description = "true flags (stars), false clears the flag; omit to leave it", IsRequired = false)]
    public bool? Flagged { get; init; }
}

/// <summary>Response of <c>email_mark</c>.</summary>
internal sealed record EmailMarkResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Messages updated.</summary>
    [JsonPropertyName("updated")]
    [ReturnSchema(Description = "Messages updated")]
    public int Updated { get; init; }
}

/// <summary>Request of <c>email_delete</c>.</summary>
internal sealed record EmailDeleteRequest
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Message ids.</summary>
    [JsonPropertyName("ids")]
    [FieldSchema(Description = "Message ids exactly as email_search returned them")]
    public IReadOnlyList<string> Ids { get; init; } = [];

    /// <summary>Delete for good.</summary>
    [JsonPropertyName("permanent")]
    [FieldSchema(Description = "Delete for good instead of moving to the trash (needs the Purge right)", IsRequired = false)]
    public bool? Permanent { get; init; }
}

/// <summary>One deleted message.</summary>
internal sealed record DeletedMessageDto
{
    /// <summary>Id it was deleted by.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "Id the message was deleted by; it no longer opens it")]
    public string Id { get; init; } = "";

    /// <summary>Id in the trash.</summary>
    [JsonPropertyName("new_id")]
    [ReturnSchema(Description = "Its id in the trash, to read it, move it back or delete it for good; null when it is gone for good or when the server does not say (search the trash)")]
    public string? NewId { get; init; }
}

/// <summary>Response of <c>email_delete</c>.</summary>
internal sealed record EmailDeleteResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Messages deleted.</summary>
    [JsonPropertyName("deleted")]
    [ReturnSchema(Description = "Messages deleted")]
    public int Deleted { get; init; }

    /// <summary>For good.</summary>
    [JsonPropertyName("permanent")]
    [ReturnSchema(Description = "True when they are gone for good")]
    public bool Permanent { get; init; }

    /// <summary>Trash folder.</summary>
    [JsonPropertyName("moved_to")]
    [ReturnSchema(Description = "Trash folder they went to, when not permanent")]
    public string? MovedTo { get; init; }

    /// <summary>Deleted messages.</summary>
    [JsonPropertyName("messages")]
    [ReturnSchema(Description = "Each deleted message: its id, and its new id in the trash")]
    public IReadOnlyList<DeletedMessageDto> Messages { get; init; } = [];
}
