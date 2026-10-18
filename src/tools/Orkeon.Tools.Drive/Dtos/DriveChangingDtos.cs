using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Drive.Dtos;

/// <summary>Request of <c>drive_upload</c>.</summary>
internal sealed record DriveUploadRequest
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Source path.</summary>
    [JsonPropertyName("path")]
    [FieldSchema(Description = "Virtual path of the file to upload", Example = "/output/report.pdf")]
    public string Path { get; init; } = "";

    /// <summary>Destination folder.</summary>
    [JsonPropertyName("folder_id")]
    [FieldSchema(Description = "Id of the folder to upload into; omit for the account's root folder. Ignored with `replace_id`", IsRequired = false)]
    public string? FolderId { get; init; }

    /// <summary>Name.</summary>
    [JsonPropertyName("name")]
    [FieldSchema(Description = "Name in the drive; omit to keep the file's own name. A name already taken in the folder is refused", IsRequired = false)]
    public string? Name { get; init; }

    /// <summary>File to replace.</summary>
    [JsonPropertyName("replace_id")]
    [FieldSchema(Description = "Id of an existing file whose content this upload replaces (same id, name and folder)", IsRequired = false)]
    public string? ReplaceId { get; init; }
}

/// <summary>Response of <c>drive_upload</c>.</summary>
internal sealed record DriveUploadResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Whether an existing file was replaced.</summary>
    [JsonPropertyName("replaced")]
    [ReturnSchema(Description = "True when the content of an existing file was replaced")]
    public bool Replaced { get; init; }

    /// <summary>The file.</summary>
    [JsonPropertyName("item")]
    [ReturnSchema(Description = "The file in the drive")]
    public DriveItemDto Item { get; init; } = new();
}

/// <summary>Request of <c>drive_create_folder</c>.</summary>
internal sealed record DriveCreateFolderRequest
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Parent folder.</summary>
    [JsonPropertyName("parent_id")]
    [FieldSchema(Description = "Id of the folder to create it in; omit for the account's root folder", IsRequired = false)]
    public string? ParentId { get; init; }

    /// <summary>Name.</summary>
    [JsonPropertyName("name")]
    [FieldSchema(Description = "Name of the new folder, without '/'", Example = "Reports")]
    public string Name { get; init; } = "";
}

/// <summary>Response of <c>drive_create_folder</c>.</summary>
internal sealed record DriveCreateFolderResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>The folder.</summary>
    [JsonPropertyName("folder")]
    [ReturnSchema(Description = "The new folder")]
    public DriveItemDto Folder { get; init; } = new();
}

/// <summary>Request of <c>drive_move</c>.</summary>
internal sealed record DriveMoveRequest
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Item id.</summary>
    [JsonPropertyName("id")]
    [FieldSchema(Description = "Id of the file or folder to move or rename")]
    public string Id { get; init; } = "";

    /// <summary>Destination folder.</summary>
    [JsonPropertyName("destination_id")]
    [FieldSchema(Description = "Id of the folder to move it into (`root` for the account's root folder); omit to only rename", IsRequired = false)]
    public string? DestinationId { get; init; }

    /// <summary>New name.</summary>
    [JsonPropertyName("new_name")]
    [FieldSchema(Description = "New name; omit to only move", IsRequired = false)]
    public string? NewName { get; init; }
}

/// <summary>Response of <c>drive_move</c>.</summary>
internal sealed record DriveMoveResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>The item.</summary>
    [JsonPropertyName("item")]
    [ReturnSchema(Description = "The item where it now is")]
    public DriveItemDto Item { get; init; } = new();
}

/// <summary>Request of <c>drive_delete</c>.</summary>
internal sealed record DriveDeleteRequest
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Item id.</summary>
    [JsonPropertyName("id")]
    [FieldSchema(Description = "Id of the file or folder to delete (a folder goes with everything in it)")]
    public string Id { get; init; } = "";

    /// <summary>Delete for good.</summary>
    [JsonPropertyName("permanent")]
    [FieldSchema(Description = "Delete for good instead of moving to the trash (needs the Purge right; not offered by a personal OneDrive)", IsRequired = false)]
    public bool? Permanent { get; init; }
}

/// <summary>Response of <c>drive_delete</c>.</summary>
internal sealed record DriveDeleteResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Item id.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "Id of the deleted item")]
    public string Id { get; init; } = "";

    /// <summary>Item name.</summary>
    [JsonPropertyName("name")]
    [ReturnSchema(Description = "Name of the deleted item")]
    public string Name { get; init; } = "";

    /// <summary>Deleted for good.</summary>
    [JsonPropertyName("permanent")]
    [ReturnSchema(Description = "True when deleted for good; false when moved to the trash, from where a person can restore it")]
    public bool Permanent { get; init; }
}

/// <summary>Request of <c>drive_share</c>.</summary>
internal sealed record DriveShareRequestDto
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Item id.</summary>
    [JsonPropertyName("id")]
    [FieldSchema(Description = "Id of the file or folder to share")]
    public string Id { get; init; } = "";

    /// <summary>Recipients.</summary>
    [JsonPropertyName("recipients")]
    [FieldSchema(Description = "E-mail addresses to give access to; each must be on the account's share allow-list. Omit with `public_link`", IsRequired = false)]
    public IReadOnlyList<string>? Recipients { get; init; }

    /// <summary>Role.</summary>
    [JsonPropertyName("role")]
    [FieldSchema(Description = "reader (default) or writer", IsRequired = false)]
    public string? Role { get; init; }

    /// <summary>Public link.</summary>
    [JsonPropertyName("public_link")]
    [FieldSchema(Description = "Create a link anyone holding it can open, instead of inviting recipients (only when the account allows public links)", IsRequired = false)]
    public bool? PublicLink { get; init; }

    /// <summary>Notify.</summary>
    [JsonPropertyName("notify")]
    [FieldSchema(Description = "Have the provider e-mail the recipients (default false)", IsRequired = false)]
    public bool? Notify { get; init; }

    /// <summary>Message.</summary>
    [JsonPropertyName("message")]
    [FieldSchema(Description = "Message of the notification e-mail, with `notify`", IsRequired = false)]
    public string? Message { get; init; }
}

/// <summary>Response of <c>drive_share</c>.</summary>
internal sealed record DriveShareResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Item id.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "Id of the shared item")]
    public string Id { get; init; } = "";

    /// <summary>Role.</summary>
    [JsonPropertyName("role")]
    [ReturnSchema(Description = "reader or writer")]
    public string Role { get; init; } = "reader";

    /// <summary>Recipients.</summary>
    [JsonPropertyName("recipients")]
    [ReturnSchema(Description = "Addresses that now have access; empty for a public link")]
    public IReadOnlyList<string> Recipients { get; init; } = [];

    /// <summary>Link.</summary>
    [JsonPropertyName("link")]
    [ReturnSchema(Description = "The address to open the item at: the public link, or the item's own address")]
    public string? Link { get; init; }
}
