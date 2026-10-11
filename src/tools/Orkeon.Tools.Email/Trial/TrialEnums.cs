namespace Orkeon.Tools.Email.Trial;

/// <summary>The kinds of action a trial simulates.</summary>
public enum SimulatedMailActionKind
{
    /// <summary>A message sent.</summary>
    Send,
    /// <summary>A draft saved.</summary>
    Draft,
    /// <summary>A message moved.</summary>
    Move,
    /// <summary>A message marked.</summary>
    Mark,
    /// <summary>A message deleted.</summary>
    Delete,
    /// <summary>A folder created.</summary>
    CreateFolder,
    /// <summary>A folder renamed.</summary>
    RenameFolder,
    /// <summary>An attachment saved.</summary>
    SaveAttachment,
}
