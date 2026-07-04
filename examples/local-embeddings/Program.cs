using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Embeddings.Local;

namespace Orkeon.Examples.LocalEmbeddings;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Locate sample-files: argv override, then the binary's BaseDirectory (csproj copies them),
        // then the source layout (handy when running from the repo root via `dotnet run`).
        var sampleDir = ResolveSampleDir(args);
        Console.WriteLine($"Loading sample files from {sampleDir}");

        var files = Directory.EnumerateFiles(sampleDir, "*.ts", SearchOption.TopDirectoryOnly).ToArray();
        if (files.Length == 0)
        {
            await Console.Error.WriteLineAsync("No sample files found.");
            return 1;
        }

        var snippets = files
            .Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .ToArray();

        // Zero-config: BGE-micro-v2, 384 dims, CPU, no network, no API key.
        // For DI usage call services.AddOrkeonLocalEmbeddings() instead — that wires a real
        // IFileSystemService. Here we pass a no-op one: the embedded model needs no VFS access
        // (ModelPath is empty), so the file system is never consulted.
        using var provider = new LocalEmbeddingProvider(new NoOpFileSystemService());
        Console.WriteLine($"Provider ready: {provider.Dimensions} dimensions (BGE-micro-v2)");

        var corpusVectors = await provider.EmbedBatchAsync(
            snippets.Select(s => s.Text).ToArray(),
            CancellationToken.None);

        const string query = "session expiration check";
        Console.WriteLine();
        Console.WriteLine($"Query: \"{query}\"");

        var queryVectors = await provider.EmbedBatchAsync([query], CancellationToken.None);
        var queryVec = queryVectors[0];

        var ranked = snippets
            .Select((s, i) => (s.Name, s.Text, Score: CosineSimilarity(queryVec.Span, corpusVectors[i].Span)))
            .OrderByDescending(r => r.Score)
            .ToArray();

        Console.WriteLine();
        Console.WriteLine("Results (descending similarity):");
        foreach (var (name, text, score) in ranked)
        {
            var snippet = text.ReplaceLineEndings(" ").Trim();
            if (snippet.Length > 80) snippet = string.Concat(snippet.AsSpan(0, 77), "...");
            Console.WriteLine($"  {score,6:F4}  {name,-15}  {snippet}");
        }

        return 0;
    }

    private static string ResolveSampleDir(string[] args)
    {
        if (args.Length > 0) return Path.GetFullPath(args[0]);

        var beside = Path.Combine(AppContext.BaseDirectory, "sample-files");
        if (Directory.Exists(beside)) return beside;

        // Source layout fallback: bin/Debug/net10.0/ -> ../../../sample-files/
        var sourceLayout = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "sample-files"));
        return sourceLayout;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Vector length mismatch.");
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom == 0f ? 0f : dot / denom;
    }
}

/// <summary>
/// Minimal no-op <see cref="IFileSystemService"/> for this standalone example. The embedded
/// BGE-micro-v2 model needs no VFS access (no <c>ModelPath</c> set), so every member throws to
/// make any accidental I/O obvious. Real applications register a proper provider via
/// <c>services.AddOrkeonFileSystem(configuration)</c>.
/// </summary>
internal sealed class NoOpFileSystemService : IFileSystemService
{
    private static T Fail<T>(string member)
        => throw new NotSupportedException($"NoOpFileSystemService: {member} is not available in this example.");

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight) => Fail<PathValidationResult>("ResolveAndValidate");
    public string? ToVirtualPath(string physicalPath) => Fail<string>("ToVirtualPath");
    public IReadOnlyList<MountInfo> GetAvailableMounts() => Fail<IReadOnlyList<MountInfo>>("GetAvailableMounts");
    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct) => Fail<IAsyncEnumerable<VirtualFileEntry>>("EnumerateFilesAsync");
    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => Fail<Task<Stream>>("OpenReadStreamAsync");
    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => Fail<Task<byte[]?>>("TryReadAllBytesAsync");
    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) => Fail<Task<string?>>("TryReadAllTextAsync");
    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => Fail<Task<VirtualEntryKind>>("GetEntryKindAsync");
    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => Fail<Task<int>>("WriteAllTextAsync");
    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => Fail<Task<bool>>("ExistsAsync");
    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => Fail<Task>("CreateDirectoryAsync");
    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => Fail<Task<bool>>("DeleteAsync");
    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => Fail<Task<int>>("WriteAllBytesAsync");
    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => Fail<Task<int>>("AppendAllTextAsync");
    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => Fail<Task<VirtualFileEntry?>>("TryGetEntryAsync");
    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => Fail<Task<Stream>>("OpenWriteStreamAsync");
    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => Fail<Task<Stream>>("OpenAppendStreamAsync");
    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => Fail<Task>("CopyAsync");
}
