using System.Text.Json;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Host.Files;

/// <summary>So many bytes, in so many files.</summary>
internal readonly record struct Held(long Bytes, long Files)
{
    public static Held operator +(Held a, Held b) => new(a.Bytes + b.Bytes, a.Files + b.Files);
}

/// <summary>
/// The physical side of the conversations' folders: what must exist before a mount can point at it, and the listing of
/// the root, which no mount covers (an Internal one would hide every conversation from its own runs). Every read and
/// write of a file goes through the VFS, in <see cref="ConversationFiles"/>.
/// </summary>
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: provisions the folders the conversations' mounts point at, and lists, measures (with the counts of each record) and gives rights back on their root, which no mount covers.")]
internal sealed class ConversationStore
{
    private const string RunPrefix = "run-";
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly EnumerationOptions OneLevel = new()
    {
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public ConversationStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    /// <summary>The folder of a conversation, or of a run that has none.</summary>
    public string FolderOf(string name) => Path.Combine(Root, name);

    public string InboxOf(string name) => Path.Combine(Root, name, "in");

    public string OutboxOf(string name) => Path.Combine(Root, name, "out");

    /// <summary>The name of the folder of a run that belongs to no conversation.</summary>
    public static string NameOfRun(string runId) => RunPrefix + runId;

    /// <summary>Makes the root, open to the service's account alone.</summary>
    public void CreateRoot() => Create(Root);

    /// <summary>Makes the folders of <paramref name="name"/>, open to the service's account alone.</summary>
    public void CreateFolders(string name)
    {
        Create(Root);
        Create(FolderOf(name));
        Create(InboxOf(name));
        Create(OutboxOf(name));
        Create(Path.Combine(FolderOf(name), "part"));
    }

    /// <summary>Whether <paramref name="name"/> has a folder.</summary>
    public bool Exists(string name) => Directory.Exists(FolderOf(name));

    /// <summary>The conversations that have a folder, by key. A name the host did not write is not one.</summary>
    public IReadOnlyList<string> Conversations() => [.. Names().Where(IsKey)];

    /// <summary>The folders of runs without a conversation, with the day each was last written to.</summary>
    public IReadOnlyList<(string Name, DateTimeOffset Written)> RunFolders() =>
        [.. Names()
            .Where(n => n.StartsWith(RunPrefix, StringComparison.Ordinal))
            .Select(n => (n, (DateTimeOffset)Directory.GetLastWriteTimeUtc(FolderOf(n))))];

    /// <summary>
    /// What one conversation holds, and what was left from it in folders of crews, what is being received left out; null when
    /// a folder of it cannot be listed, or its record cannot be read, which counts as over any limit.
    /// </summary>
    public Held? HeldBy(string name) => Measure(InboxOf(name)) + Measure(OutboxOf(name)) + LeftBy(name);

    /// <summary>The bytes everything under the root holds, or left, what is being received left out.</summary>
    /// <param name="unmeasured">What a conversation that cannot be measured counts for: it must not stop the others.</param>
    public long TotalSize(long unmeasured) => Names().Sum(name => Math.Min(HeldBy(name)?.Bytes ?? unmeasured, long.MaxValue >> 20));

    /// <summary>Gives the service's account its rights back on the folders of <paramref name="name"/>, links left alone.</summary>
    public void MakeAccessible(string name)
    {
        if (OperatingSystem.IsWindows())
            return;
        var left = new Stack<DirectoryInfo>([new DirectoryInfo(FolderOf(name))]);
        while (left.TryPop(out var directory))
        {
            try
            {
                if ((directory.UnixFileMode & Private) != Private)
                    directory.UnixFileMode |= Private;
                foreach (var child in directory.EnumerateDirectories("*", OneLevel))
                    left.Push(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // Deleted meanwhile, or another account's: the delete that follows says so.
            }
        }
    }

    private IEnumerable<string> Names()
    {
        var root = new DirectoryInfo(Root);
        return root.Exists
            ? root.EnumerateDirectories()
                .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                .Select(d => d.Name)
            : [];
    }

    private static bool IsKey(string name) => name.Length == 64 && name.All(char.IsAsciiHexDigitLower);

    // As ConversationFiles writes it. A record that does not read as one counts for nothing, as it does there.
    private Held? LeftBy(string name)
    {
        try
        {
            using var file = new FileStream(
                Path.Combine(FolderOf(name), "meta.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<Left>(file) is { LeftBytes: >= 0, LeftFiles: >= 0 } left ? new Held(left.LeftBytes, left.LeftFiles) : new Held();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return new Held();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private sealed record Left(int LeftFiles, long LeftBytes);

    // Null when a folder cannot be listed: what it holds could be anything.
    private static Held? Measure(string folder)
    {
        var total = new Held();
        var left = new Stack<DirectoryInfo>([new DirectoryInfo(folder)]);
        while (left.TryPop(out var directory))
        {
            try
            {
                foreach (var entry in directory.EnumerateFileSystemInfos("*", OneLevel))
                {
                    if (entry is DirectoryInfo child)
                        left.Push(child);
                    else if (entry is FileInfo file)
                        total += new Held(LengthOf(file), 1);
                }
            }
            catch (DirectoryNotFoundException)
            {
                continue; // Deleted while it was measured.
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                return null;
            }
        }

        return total;
    }

    private static long LengthOf(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (FileNotFoundException)
        {
            return 0; // Deleted while it was measured.
        }
    }

    private static void Create(string folder)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(folder);
        else
            Directory.CreateDirectory(folder, Private);
    }
}
