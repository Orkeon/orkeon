// EXCEPTION-BOOTSTRAP — reads corpus files directly (no VFS); read-only.
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Corpus;

/// <summary>
/// Lazy semantic index over the experiment corpus (.txt, .md, glossary JSON).
/// Built once per session on the first <see cref="SearchAsync"/> call;
/// subsequent queries only embed the query vector (no file re-reads).
/// </summary>
public sealed class CorpusSearchIndex : IDisposable
{
    private readonly IEmbeddingProvider _embedder;
    private readonly TranscriptsCatalog _catalog;
    private readonly ILogger<CorpusSearchIndex> _logger;

    private readonly List<IndexedChunk> _chunks = new();
    private bool _isBuilt;
    private readonly SemaphoreSlim _buildLock = new(1, 1);

    public bool IsBuilt => _isBuilt;
    public int ChunkCount => _chunks.Count;

    public CorpusSearchIndex(
        IEmbeddingProvider embedder,
        TranscriptsCatalog catalog,
        ILogger<CorpusSearchIndex>? logger = null)
    {
        _embedder = embedder;
        _catalog = catalog;
        _logger = logger ?? NullLogger<CorpusSearchIndex>.Instance;
    }

    /// <summary>Releases the build-gate semaphore.</summary>
    public void Dispose() => _buildLock.Dispose();

    /// <summary>
    /// Searches the corpus for chunks semantically similar to <paramref name="query"/>.
    /// Triggers index build on first call.
    /// </summary>
    public async Task<IReadOnlyList<CorpusHit>> SearchAsync(
        string query,
        int topK = 10,
        CancellationToken ct = default)
    {
        await EnsureBuiltAsync(ct).ConfigureAwait(false);

        var queryVecs = await _embedder.EmbedBatchAsync([query], ct).ConfigureAwait(false);
        var queryVec = queryVecs[0];

        return _chunks
            .Select(c => new CorpusHit(c.RelativePath, c.Excerpt, Cosine(queryVec, c.Embedding), c.LineHint))
            .OrderByDescending(h => h.Score)
            .Where(h => h.Score > 0.05f)
            .Take(topK)
            .ToList();
    }

    /// <summary>Ensures the index is built; triggers build on first call.</summary>
    public async Task EnsureBuiltAsync(CancellationToken ct = default)
    {
        if (_isBuilt) return;
        await _buildLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isBuilt) return;
            await BuildAsync(ct).ConfigureAwait(false);
            _isBuilt = true;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    // ── Build ──────────────────────────────────────────────────────────────

    private async Task BuildAsync(CancellationToken ct)
    {
        var raw = new List<(string RelativePath, string Text, int LineHint)>();

        CollectTextFiles(_catalog.TranscriptsRoot, "*.txt", raw);
        CollectTextFiles(_catalog.SummariesRoot,   "*.md",  raw);
        CollectTextFiles(_catalog.ReportsRoot,     "*.md",  raw);
        CollectTextFiles(_catalog.TopicsRoot,      "*.md",  raw);
        CollectTextFiles(_catalog.TasksRoot,       "*.md",  raw);
        await CollectGlossaryAsync(raw, ct).ConfigureAwait(false);

        _logger.LogInformation("CorpusSearchIndex: {Count} raw chunks collected, embedding…", raw.Count);

        const int batchSize = 50;
        for (int i = 0; i < raw.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = raw.Skip(i).Take(batchSize).ToList();
            var texts = batch.ConvertAll(c => c.Text);
            var vecs  = await _embedder.EmbedBatchAsync(texts, ct).ConfigureAwait(false);
            for (int j = 0; j < batch.Count; j++)
                _chunks.Add(new IndexedChunk(batch[j].RelativePath, TruncateExcerpt(batch[j].Text), vecs[j], batch[j].LineHint));
        }

        _logger.LogInformation("CorpusSearchIndex: index built — {Count} chunks.", _chunks.Count);
    }

    private void CollectTextFiles(string dir, string pattern, List<(string, string, int)> target)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, pattern))
        {
            var rel  = MakeRelative(file);
            var text = File.ReadAllText(file);
            foreach (var (chunk, line) in ChunkText(text))
                target.Add((rel, chunk, line));
        }
    }

    private async Task CollectGlossaryAsync(List<(string, string, int)> target, CancellationToken ct)
    {
        var path = Path.Combine(_catalog.GlossaryRoot, "GLOSSAIRE_METIER.json");
        if (!File.Exists(path)) return;
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var relBase = MakeRelative(path);
        foreach (var (text, relPath) in ExtractGlossaryChunks(json, relBase))
            target.Add((relPath, text, 0));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private string MakeRelative(string absolutePath)
    {
        var root = _catalog.ExperimentRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? absolutePath[root.Length..]
            : Path.GetFileName(absolutePath);
    }

    private static IEnumerable<(string text, int lineHint)> ChunkText(string content)
    {
        var lineNum = 1;
        foreach (var para in content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = para.Trim();
            if (trimmed.Length < 10)
            {
                lineNum += para.Count(c => c == '\n') + 2;
                continue;
            }

            if (trimmed.Length <= 400)
            {
                yield return (trimmed, lineNum);
            }
            else
            {
                foreach (var sentence in trimmed.Split(". ", StringSplitOptions.RemoveEmptyEntries))
                {
                    var s = sentence.Trim();
                    if (s.Length >= 10) yield return (s, lineNum);
                }
            }

            lineNum += para.Count(c => c == '\n') + 2;
        }
    }

    private static IEnumerable<(string text, string relativePath)> ExtractGlossaryChunks(string json, string baseRelPath)
    {
        JsonDocument? doc;
        try { doc = JsonDocument.Parse(json); }
        catch { yield break; }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("terms", out var terms) || terms.ValueKind != JsonValueKind.Object)
                yield break;

            foreach (var term in terms.EnumerateObject())
            {
                var slug = term.Name;
                var def     = term.Value.TryGetProperty("definition", out var d)   ? d.GetString() ?? ""   : "";
                var aliases = term.Value.TryGetProperty("aliases",    out var a)   && a.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", a.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0)) : "";
                var domaine = term.Value.TryGetProperty("domaine",    out var dom) ? dom.GetString() ?? "" : "";
                var sessions = term.Value.TryGetProperty("sessions_associees", out var s) && s.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", s.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0)) : "";

                var sb = new StringBuilder().Append(slug).Append(" : ").Append(def);
                if (aliases.Length  > 0) sb.Append('\n').Append("aliases: ").Append(aliases);
                if (domaine.Length  > 0) sb.Append(" | domaine: ").Append(domaine);
                if (sessions.Length > 0) sb.Append('\n').Append("sessions: ").Append(sessions);

                if (sb.Length >= 10)
                    yield return (sb.ToString(), $"{baseRelPath}#{slug}");
            }
        }
    }

    private static string TruncateExcerpt(string text)
    {
        const int max = 180;
        var flat = text.Replace('\n', ' ');
        return flat.Length <= max ? flat : flat[..max].TrimEnd() + "…";
    }

    private static float Cosine(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var aSpan = a.Span;
        var bSpan = b.Span;
        float dot = 0, normA = 0, normB = 0;
        var len = Math.Min(aSpan.Length, bSpan.Length);
        for (var i = 0; i < len; i++)
        {
            dot   += aSpan[i] * bSpan[i];
            normA += aSpan[i] * aSpan[i];
            normB += bSpan[i] * bSpan[i];
        }
        return normA == 0 || normB == 0 ? 0f : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    private sealed record IndexedChunk(
        string RelativePath,
        string Excerpt,
        ReadOnlyMemory<float> Embedding,
        int LineHint);
}

/// <summary>One search result from the corpus, with source citation.</summary>
public sealed record CorpusHit(string RelativePath, string Excerpt, float Score, int LineHint);
