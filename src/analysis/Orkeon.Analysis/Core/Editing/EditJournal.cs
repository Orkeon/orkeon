using System.Security.Cryptography;
using System.Text;

namespace Orkeon.Analysis.Core.Editing;

/// <summary>The original bytes of every file of a batch with the hash of its edited bytes, written before its first write so a crash can be undone.</summary>
internal static class EditJournal
{
    public const string Folder = "/output/edit-batch";
    public const string Extension = ".journal";

    public static string PathOf(string batchId) => $"{Folder}/{batchId}{Extension}";

    public static byte[] Serialize(IReadOnlyList<string> paths, IReadOnlyList<byte[]> originals, IReadOnlyList<byte[]> edited)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(paths.Count);
            for (var i = 0; i < paths.Count; i++)
            {
                writer.Write(paths[i]);
                writer.Write(originals[i].Length);
                writer.Write(originals[i]);
                writer.Write(SHA256.HashData(edited[i]));
            }
        }

        return stream.ToArray();
    }

    public static IReadOnlyList<(string Path, byte[] Bytes, byte[] EditedHash)> Deserialize(byte[] journal)
    {
        using var reader = new BinaryReader(new MemoryStream(journal), Encoding.UTF8);
        var entries = new List<(string, byte[], byte[])>();
        for (var remaining = reader.ReadInt32(); remaining > 0; remaining--)
        {
            var path = reader.ReadString();
            var bytes = reader.ReadBytes(reader.ReadInt32());
            entries.Add((path, bytes, reader.ReadBytes(SHA256.HashSizeInBytes)));
        }

        return entries;
    }
}
