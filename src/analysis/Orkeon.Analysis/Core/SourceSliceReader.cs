using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Reads source slices for indexed nodes, preferring fresh file content (via the VFS)
/// over the indexed snapshot, with per-file caching and line-window bounding.
/// Extracted from <see cref="InMemoryRaggableStore"/> (R4.2 god-file decomposition)
/// so the source-extraction logic is testable in isolation.
/// </summary>
public sealed class SourceSliceReader
{
    private readonly IFileSystemService _fileSystem;
    private readonly Dictionary<string, string> _sourceFileCache = new(StringComparer.Ordinal);
    private readonly int _maxLines;

    /// <summary>Initializes a new reader.</summary>
    /// <param name="fileSystem">VFS used to read fresh file content.</param>
    /// <param name="maxLines">Maximum number of lines returned for body/full-span modes.</param>
    public SourceSliceReader(IFileSystemService fileSystem, int maxLines)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _maxLines = maxLines;
    }

    /// <summary>Clears the per-file content cache (call when the underlying index is replaced).</summary>
    public void ClearCache() => _sourceFileCache.Clear();

    /// <summary>
    /// Builds the source slice for <paramref name="node"/> in the requested <paramref name="mode"/>.
    /// Falls back to the indexed snapshot (marked <c>Stable = false</c>) when fresh content
    /// is unavailable.
    /// </summary>
    public Task<SourceSlice> ReadAsync(RaggableNode node, SourceMode mode, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        return ReadCoreAsync();

        async Task<SourceSlice> ReadCoreAsync()
        {
            var fresh = await TryReadFreshAsync(node.VirtualFilePath, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(node.VirtualFilePath) || fresh is null)
            {
                return SnapshotSlice(node, mode);
            }

            return FreshSlice(node, mode, fresh);
        }
    }

    private static SourceSlice SnapshotSlice(RaggableNode node, SourceMode mode) => new()
    {
        VirtualFilePath = node.VirtualFilePath,
        StartLine = node.Range.StartLine,
        EndLine = node.Range.EndLine,
        Language = node.Language,
        Source = mode == SourceMode.SignatureOnly ? node.Signature : node.SourceSnippet,
        Sha256 = node.Sha256,
        Stable = false,
    };

    private SourceSlice FreshSlice(RaggableNode node, SourceMode mode, string content)
    {
        var lines = content.Split('\n');
        var startLine = Math.Max(1, node.Range.StartLine);
        var endLine = node.Range.EndLine > 0 ? node.Range.EndLine : startLine;

        var source = ComposeSource(node, mode, lines, startLine, ref endLine);

        var sha = ComputeSha256(source);
        var stable = string.Equals(sha, node.Sha256, StringComparison.Ordinal) || string.IsNullOrEmpty(node.Sha256);
        return new SourceSlice
        {
            VirtualFilePath = node.VirtualFilePath,
            StartLine = startLine,
            EndLine = endLine,
            Language = node.Language,
            Source = source,
            Sha256 = sha,
            Stable = stable,
        };
    }

    private string ComposeSource(RaggableNode node, SourceMode mode, string[] lines, int startLine, ref int endLine)
    {
        string source;
        switch (mode)
        {
            case SourceMode.SignatureOnly:
                source = node.Signature;
                endLine = startLine;
                break;
            case SourceMode.SignatureAndDoc:
                source = string.IsNullOrEmpty(node.DocComment) ? node.Signature : node.DocComment + "\n" + node.Signature;
                endLine = startLine;
                break;
            case SourceMode.SignatureAndBody:
            case SourceMode.FullSpan:
                source = SliceLines(lines, startLine, endLine, _maxLines, out var bounded);
                if (bounded) endLine = startLine + _maxLines - 1;
                break;
            case SourceMode.StatementSpan:
                source = SliceLines(lines, startLine, endLine, _maxLines, out _);
                break;
            default:
                source = node.SourceSnippet;
                break;
        }

        return source;
    }

    private async Task<string?> TryReadFreshAsync(string virtualPath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(virtualPath)) return null;
        if (_sourceFileCache.TryGetValue(virtualPath, out var cached)) return cached;
        var content = await _fileSystem.TryReadAllTextAsync(virtualPath, ct).ConfigureAwait(false);
        if (content is null) return null;
        _sourceFileCache[virtualPath] = content;
        return content;
    }

    private static string SliceLines(string[] lines, int startLine, int endLine, int maxLines, out bool bounded)
    {
        bounded = false;
        var start = Math.Clamp(startLine - 1, 0, Math.Max(0, lines.Length - 1));
        var end = Math.Clamp(endLine - 1, start, Math.Max(0, lines.Length - 1));
        var count = end - start + 1;
        if (count > maxLines)
        {
            count = maxLines;
            bounded = true;
        }
        return string.Join('\n', lines, start, count);
    }

    private static string ComputeSha256(string source)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(source);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
