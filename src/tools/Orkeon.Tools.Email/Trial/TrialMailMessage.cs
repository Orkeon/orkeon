using System.Collections.Immutable;

namespace Orkeon.Tools.Email.Trial;

/// <summary>A message of the trial mailbox.</summary>
public sealed record TrialMailMessage
{
    /// <summary>Folder name.</summary>
    public required string Folder { get; init; }

    /// <summary>Sender.</summary>
    public required string From { get; init; }

    /// <summary>Recipients.</summary>
    public ImmutableList<string> To { get; init; } = [];

    /// <summary>Subject.</summary>
    public required string Subject { get; init; }

    /// <summary>Body text.</summary>
    public string Body { get; init; } = "";

    /// <summary>Date.</summary>
    public DateTimeOffset Date { get; init; }

    /// <summary>Attachments.</summary>
    public ImmutableList<TrialAttachment> Attachments { get; init; } = [];
}

/// <summary>An attachment of a trial message.</summary>
public sealed record TrialAttachment
{
    /// <summary>File name.</summary>
    public required string Name { get; init; }

    /// <summary>Media type.</summary>
    public required string MediaType { get; init; }

    /// <summary>Content.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The seed JSON carries the bytes as base64; the property is read-only data of a record.")]
    public required byte[] Content { get; init; }
}
