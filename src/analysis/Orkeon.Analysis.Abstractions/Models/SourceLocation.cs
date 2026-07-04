namespace Orkeon.Analysis.Abstractions.Models;

public sealed record SourceLocation(string VirtualFilePath, int StartLine, int EndLine, string Sha256);
