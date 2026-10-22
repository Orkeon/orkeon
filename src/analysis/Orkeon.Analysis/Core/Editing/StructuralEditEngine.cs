using System.Security.Cryptography;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core.Editing;

internal sealed class StructuralEditEngine(IFileSystemService fs, TreeSitterParserPool pool)
{
    public async Task<EditOutcome> ApplyAsync(
        string virtualPath, IReadOnlyList<IEditOperation> operations, CancellationToken cancellationToken = default)
    {
        var bytes = await fs.TryReadAllBytesAsync(virtualPath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("File not found.", virtualPath);
        EditResult? last = null;
        foreach (var operation in operations)
        {
            var parsed = StructuralFile.FromBytes(pool, bytes, virtualPath);
            last = operation.Apply(bytes, parsed);
            var introduced = ValidatorFor(parsed.Language).Introduced(bytes, last.Bytes, last.Range);
            if (introduced.Count > 0) throw new ParseBrokenException(introduced);
            bytes = last.Bytes;
        }

        if (last is null) throw new ArgumentException("At least one operation is required.", nameof(operations));
        await WriteAsync(virtualPath, bytes, cancellationToken).ConfigureAwait(false);
        var replaced = bytes.AsSpan(last.Range.StartByte, last.Range.EndByte - last.Range.StartByte);
        return new EditOutcome { Range = last.Range, NewHash = Convert.ToHexStringLower(SHA256.HashData(replaced)) };
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
