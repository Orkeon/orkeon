using System.Security.Cryptography;
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
        var (bytes, ranges) = ApplyInMemory(virtualPath, original, operations);
        await WriteAsync(virtualPath, bytes, cancellationToken).ConfigureAwait(false);
        var last = ranges[^1];
        var replaced = bytes.AsSpan(last.StartByte, last.EndByte - last.StartByte);
        return new EditOutcome { Range = last, NewHash = Convert.ToHexStringLower(SHA256.HashData(replaced)) };
    }

    /// <summary>Applies the operations in order, each on the bytes the previous one produced; nothing is written.</summary>
    public (byte[] Bytes, IReadOnlyList<NodeRange> Ranges) ApplyInMemory(
        string virtualPath, byte[] original, IReadOnlyList<IEditOperation> operations)
    {
        if (operations.Count == 0) throw new ArgumentException("At least one operation is required.", nameof(operations));
        var bytes = original;
        var ranges = new List<NodeRange>(operations.Count);
        foreach (var operation in operations)
        {
            var parsed = StructuralFile.FromBytes(pool, bytes, virtualPath);
            var result = operation.Apply(bytes, parsed);
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
        }

        return (bytes, ranges);
    }

    private IParseValidator ValidatorFor(string language)
        => language == StructuralFile.MarkdownLanguage ? new NoParseValidator() : new TreeSitterParseValidator(pool, language);

    private async Task WriteAsync(string virtualPath, byte[] bytes, CancellationToken cancellationToken)
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
