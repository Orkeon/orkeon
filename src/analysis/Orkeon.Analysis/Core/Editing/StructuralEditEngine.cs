using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Core.Structural;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core.Editing;

internal sealed class StructuralEditEngine(IFileSystemService fs, TreeSitterParserPool pool)
{
    public async Task<EditOutcome> ApplyAsync(
        string virtualPath, IReadOnlyList<IEditOperation> operations, CancellationToken cancellationToken = default)
    {
        var original = await fs.TryReadAllBytesAsync(virtualPath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("File not found.", virtualPath);
        var (bytes, ranges, _) = ApplyInMemory(virtualPath, original, operations);
        if (bytes.AsSpan().SequenceEqual(original))
        {
            var noOp = ranges[^1];
            var existing = original.AsSpan(noOp.StartByte, noOp.EndByte - noOp.StartByte);
            return new EditOutcome { Range = noOp, NewHash = Convert.ToHexStringLower(SHA256.HashData(existing)), Applied = false };
        }

        await WriteAsync(virtualPath, bytes, cancellationToken).ConfigureAwait(false);
        var last = ranges[^1];
        var replaced = bytes.AsSpan(last.StartByte, last.EndByte - last.StartByte);
        return new EditOutcome { Range = last, NewHash = Convert.ToHexStringLower(SHA256.HashData(replaced)) };
    }

    /// <summary>Rewrites the matches back to front with the fix, keeping the outermost of overlapping ones; nothing is written.</summary>
#pragma warning disable CA1822 // the engine is the public face of edits; callers hold an instance
    public RewriteOutcome Rewrite(string virtualPath, byte[] original, IReadOnlyList<StructuralMatch> matches, FixTemplate fix)
    {
        var ordered = matches.OrderBy(m => m.StartByte).ThenByDescending(m => m.EndByte).ToList();
        var kept = new List<StructuralMatch>(ordered.Count);
        var skipped = new List<SkippedMatch>();
        var reach = 0;
        foreach (var match in ordered)
        {
            if (kept.Count > 0 && match.StartByte < reach)
            {
                skipped.Add(new SkippedMatch(match, "overlaps"));
                continue;
            }

            kept.Add(match);
            reach = match.EndByte;
        }

        var output = new MemoryStream(original.Length);
        var position = 0;
        foreach (var match in kept)
        {
            output.Write(original, position, match.StartByte - position);
            var text = NodeReplaceOperation.WithLineEndingsOf(original, fix.Render(match, IndentationOf(original, match.StartByte)));
            output.Write(Encoding.UTF8.GetBytes(text));
            position = match.EndByte;
        }

        output.Write(original, position, original.Length - position);
        return new RewriteOutcome(output.ToArray(), kept.Count, skipped);
    }
#pragma warning restore CA1822

    private static string IndentationOf(byte[] bytes, int offset)
    {
        var start = offset;
        while (start > 0 && bytes[start - 1] != (byte)'\n') start--;
        var end = start;
        while (end < offset && (bytes[end] == (byte)' ' || bytes[end] == (byte)'\t')) end++;
        return Encoding.UTF8.GetString(bytes, start, end - start);
    }

    /// <summary>Applies the operations in order, each on the bytes the previous one produced; nothing is written.</summary>
    public (byte[] Bytes, IReadOnlyList<NodeRange> Ranges, IReadOnlyList<bool> Applied) ApplyInMemory(
        string virtualPath, byte[] original, IReadOnlyList<IEditOperation> operations)
    {
        if (operations.Count == 0) throw new ArgumentException("At least one operation is required.", nameof(operations));
        var bytes = original;
        var ranges = new List<NodeRange>(operations.Count);
        var applied = new List<bool>(operations.Count);
        foreach (var operation in operations)
        {
            var parsed = StructuralFile.FromBytes(pool, bytes, virtualPath);
            var result = operation.Apply(bytes, parsed);
            if (!result.Applied)
            {
                ranges.Add(result.Range);
                applied.Add(false);
                continue;
            }

            var introduced = ValidatorFor(parsed.Language).Introduced(bytes, result.Bytes, result.Range);
            if (introduced.Count > 0) throw new ParseBrokenException(introduced);
            var byteShift = result.Bytes.Length - bytes.Length;
            var lineShift = result.Bytes.AsSpan().Count((byte)'\n') - bytes.AsSpan().Count((byte)'\n');
            for (var i = 0; i < ranges.Count; i++)
            {
                var earlier = ranges[i];
                if (earlier.StartByte < result.Range.StartByte)
                {
                    if (result.Range.StartByte < earlier.EndByte)
                        ranges[i] = new NodeRange(
                            earlier.StartByte, earlier.EndByte + byteShift, earlier.StartLine, earlier.EndLine + lineShift);
                    continue;
                }

                ranges[i] = new NodeRange(
                    earlier.StartByte + byteShift, earlier.EndByte + byteShift, earlier.StartLine + lineShift, earlier.EndLine + lineShift);
            }

            bytes = result.Bytes;
            ranges.Add(result.Range);
            applied.Add(true);
        }

        return (bytes, ranges, applied);
    }

    private IParseValidator ValidatorFor(string language)
        => language == StructuralFile.MarkdownLanguage ? new NoParseValidator() : new TreeSitterParseValidator(pool, language);

    internal async Task WriteAsync(string virtualPath, byte[] bytes, CancellationToken cancellationToken)
    {
        var backup = virtualPath + ".orkeon-edit.bak";
        await fs.CopyAsync(virtualPath, backup, overwrite: true, cancellationToken).ConfigureAwait(false);
        try
        {
            await fs.WriteAllBytesAsync(virtualPath, bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception writeFailure)
        {
            try
            {
                await fs.CopyAsync(backup, virtualPath, overwrite: true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception restoreFailure)
            {
                throw new AggregateException(writeFailure, restoreFailure);
            }

            await fs.DeleteAsync(backup, recursive: false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await fs.DeleteAsync(backup, recursive: false, CancellationToken.None).ConfigureAwait(false);
    }
}
