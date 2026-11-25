using Orkeon.Domain.FileSystem;

namespace Orkeon.Host.Files;

/// <summary>
/// The physical side of the conversations' folders: what must exist before a mount can point at it, and the listing of
/// the root, which no mount covers (an Internal one would hide every conversation from its own runs). Every read and
/// write of a file goes through the VFS, in <see cref="ConversationFiles"/>.
/// </summary>
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: provisions the folders the conversations' mounts point at, and lists and measures their root, which no mount covers.")]
internal sealed class ConversationStore
{
    private const string RunPrefix = "run-";
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
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

    /// <summary>The bytes one conversation holds, what is being received left out.</summary>
    public long SizeOf(string name) => Measure(InboxOf(name)) + Measure(OutboxOf(name));

    /// <summary>The bytes everything under the root holds, what is being received left out.</summary>
    public long TotalSize() => Names().Sum(SizeOf);

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

    private static long Measure(string folder)
    {
        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Everything))
                total += file.Length;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Deleted while it was measured: what was counted so far is the measure.
        }

        return total;
    }

    private static void Create(string folder)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(folder);
        else
            Directory.CreateDirectory(folder, Private);
    }
}
