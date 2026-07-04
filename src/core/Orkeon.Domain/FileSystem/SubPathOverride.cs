namespace Orkeon.Domain.FileSystem;

/// <summary>Overrides access rights for a specific sub-path within a mount.</summary>
/// <param name="RelativePath">Relative path from the mount root.</param>
/// <param name="Rights">Access rights granted for the sub-path.</param>
public sealed record SubPathOverride(string RelativePath, FileAccessRights Rights);
