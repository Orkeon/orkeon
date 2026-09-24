using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Retrieval;

namespace Orkeon.Scripting.Cli.Commands.UseCases;

/// <summary>How a search ranks the catalogue.</summary>
internal enum UseCaseSearchMode
{
    /// <summary>By the terms the query shares with each sheet, in any of the five languages.</summary>
    Bm25,

    /// <summary>By terms, fused by RRF with the closeness in meaning of the query to each English sheet.</summary>
    Hybrid,
}

/// <summary>Why a use case is in an answer.</summary>
internal enum UseCaseMatchReason
{
    /// <summary>It shares terms with the query.</summary>
    Terms,

    /// <summary>It shares no term, but the local model finds it close in meaning.</summary>
    Meaning,

    /// <summary>Both.</summary>
    TermsAndMeaning,
}

/// <summary>
/// The search mode of each language (STUDIO-38 D-02): meaning is fused in only where the golden
/// set (<c>examples/usecases.golden.yaml</c>) measured a gain — the local model reads English, and
/// its vector for a query in another language is closer to noise than to meaning.
/// </summary>
internal sealed class UseCaseSearchPolicy
{
    /// <summary>
    /// The shipped policy: hybrid where the golden set's recall@5 gains from it, terms elsewhere.
    /// <c>UseCaseGoldenSetTests</c> prints the recall of each language in each mode; the first
    /// measurement ran while the sheets' titles and problems were still empty (STUDIO-37), with
    /// French, English and Spanish gaining and German and Chinese not. Measure again whenever the
    /// texts change, and set each language from the report.
    /// </summary>
    public static UseCaseSearchPolicy Default { get; } = new(new Dictionary<string, UseCaseSearchMode>(StringComparer.Ordinal)
    {
        [UseCaseLanguages.French] = UseCaseSearchMode.Hybrid,
        [UseCaseLanguages.English] = UseCaseSearchMode.Hybrid,
        [UseCaseLanguages.Spanish] = UseCaseSearchMode.Hybrid,
        [UseCaseLanguages.German] = UseCaseSearchMode.Bm25,
        [UseCaseLanguages.SimplifiedChinese] = UseCaseSearchMode.Bm25,
    });

    /// <summary>A policy naming a mode per language; a language it leaves out is searched by terms.</summary>
    public UseCaseSearchPolicy(IReadOnlyDictionary<string, UseCaseSearchMode> modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        Modes = UseCaseLanguages.All.ToDictionary(
            language => language,
            language => modes.GetValueOrDefault(language, UseCaseSearchMode.Bm25),
            StringComparer.Ordinal);
    }

    /// <summary>The mode of each of the five languages.</summary>
    public IReadOnlyDictionary<string, UseCaseSearchMode> Modes { get; }

    /// <summary>One mode for every language — what the golden set measures each mode with.</summary>
    public static UseCaseSearchPolicy Everywhere(UseCaseSearchMode mode) =>
        new(UseCaseLanguages.All.ToDictionary(language => language, _ => mode, StringComparer.Ordinal));

    /// <summary>The mode <paramref name="language"/> is searched in.</summary>
    public UseCaseSearchMode ModeFor(string language) => Modes.GetValueOrDefault(language, UseCaseSearchMode.Bm25);
}

/// <summary>One search.</summary>
internal sealed record UseCaseQuery
{
    /// <summary>The need, in plain words.</summary>
    public required string Text { get; init; }

    /// <summary>How many use cases to answer with at most.</summary>
    public int Top { get; init; } = UseCaseSearchEngine.DefaultTop;

    /// <summary>The query's language; null reads it from the text.</summary>
    public string? Language { get; init; }
}

/// <summary>One use case of an answer.</summary>
internal sealed record UseCaseMatch
{
    /// <summary>1 for the best.</summary>
    public required int Rank { get; init; }

    /// <summary>The sheet.</summary>
    public required UseCase UseCase { get; init; }

    /// <summary>
    /// The BM25 score by terms, the RRF score in hybrid mode: comparable within one answer only.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>Terms, meaning, or both.</summary>
    public required UseCaseMatchReason Reason { get; init; }

    /// <summary>The query's terms the sheet contains, normalized; empty for a match by meaning alone.</summary>
    public IReadOnlyList<string> Terms { get; init; } = [];

    /// <summary>
    /// In hybrid mode, the cosine between the query and the sheet's English text — how close in
    /// meaning the local model finds them, whatever the reason the sheet made the answer. Null when
    /// the search ran by terms.
    /// </summary>
    public double? Similarity { get; init; }
}

/// <summary>The answer to one search.</summary>
internal sealed record UseCaseAnswer
{
    /// <summary>The query as asked.</summary>
    public required string Query { get; init; }

    /// <summary>The language the query was taken for.</summary>
    public required string Language { get; init; }

    /// <summary>How that language was settled.</summary>
    public required UseCaseLanguageSource LanguageSource { get; init; }

    /// <summary>The mode the search actually ran in.</summary>
    public required UseCaseSearchMode Mode { get; init; }

    /// <summary>
    /// Why the search ran by terms although the language asked for meaning — the local model is
    /// missing, or failed. Null when nothing was given up (D-05: never silent).
    /// </summary>
    public string? Degraded { get; init; }

    /// <summary>The use cases, best first.</summary>
    public required IReadOnlyList<UseCaseMatch> Matches { get; init; }
}

/// <summary>
/// The local model is not installed where the CLI looks for it. Its message is the reason the
/// search reports when it falls back to terms.
/// </summary>
internal sealed class UseCaseModelUnavailableException : Exception
{
    /// <inheritdoc />
    public UseCaseModelUnavailableException()
    {
    }

    /// <inheritdoc />
    public UseCaseModelUnavailableException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public UseCaseModelUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The use-case search (STUDIO-38): no LLM, no network, no file. BM25 carries the five
/// languages over one normalized document per sheet (D-01); the local model's English vectors
/// are fused in by RRF for the languages whose policy says so (D-02) — loaded, and the sheets
/// embedded, at the first search that needs them, then kept for the life of the process (D-03).
/// Without the model it searches by terms and says why on every answer (D-05).
/// </summary>
internal sealed class UseCaseSearchEngine : IDisposable
{
    /// <summary>The default length of an answer.</summary>
    public const int DefaultTop = 5;

    /// <summary>
    /// How deep each ranking goes into the fusion. Past this rank a sheet is neither a term match
    /// nor close in meaning in any sense a reader would accept.
    /// </summary>
    private const int FusionDepth = 20;

    private const string CosineOrigin = "local-cosine";

    private readonly UseCaseSearchPolicy _policy;
    private readonly Func<IEmbeddingProvider> _loadModel;
    private readonly Bm25Index _index = new();
    private readonly Dictionary<string, Sheet> _sheets = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _modelGate = new(1, 1);

    private IEmbeddingProvider? _model;
    private List<(Sheet Sheet, float[] Vector)>? _vectors;
    private string? _modelUnavailable;

    /// <summary>Indexes <paramref name="catalog"/>; <paramref name="loadModel"/> runs at the first search that needs meaning.</summary>
    public UseCaseSearchEngine(UseCaseCatalog catalog, UseCaseSearchPolicy policy, Func<IEmbeddingProvider> loadModel)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _loadModel = loadModel ?? throw new ArgumentNullException(nameof(loadModel));

        foreach (var useCase in catalog.UseCases)
        {
            var terms = UseCaseText.Tokenize(string.Join(' ', IndexedText(useCase)));
            var chunk = new Chunk
            {
                Id = useCase.Id,
                DocumentId = useCase.Id,
                SourceId = useCase.Id,
                Content = string.Join(' ', terms),
            };
            _sheets[useCase.Id] = new Sheet(useCase, chunk, terms.ToHashSet(StringComparer.Ordinal));
        }

        _index.UpsertRange(_sheets.Values.Select(sheet => sheet.Chunk));
    }

    /// <summary>Runs one search.</summary>
    public async Task<UseCaseAnswer> SearchAsync(UseCaseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.Top);

        var (language, source) = ResolveLanguage(query);
        var terms = UseCaseText.Tokenize(query.Text);
        IReadOnlyList<ScoredChunk> byTerms = terms.Count == 0 || _sheets.Count == 0
            ? []
            : _index.Search(string.Join(' ', terms), _sheets.Count);

        var mode = UseCaseSearchMode.Bm25;
        string? degraded = null;
        var ranking = byTerms;
        var closeInMeaning = new HashSet<string>(StringComparer.Ordinal);
        var similarity = new Dictionary<string, double>(StringComparer.Ordinal);

        if (_policy.ModeFor(language) == UseCaseSearchMode.Hybrid && _sheets.Count > 0)
        {
            var (byMeaning, failure) = await RankByMeaningAsync(query.Text, cancellationToken).ConfigureAwait(false);
            if (byMeaning is null)
            {
                degraded = failure;
            }
            else
            {
                mode = UseCaseSearchMode.Hybrid;
                foreach (var candidate in byMeaning)
                    similarity[candidate.Chunk.Id] = candidate.Score;

                var meaningCandidates = byMeaning.Take(FusionDepth).ToList();
                closeInMeaning.UnionWith(meaningCandidates.Select(candidate => candidate.Chunk.Id));
                ranking = ReciprocalRankFusion.Fuse(
                    ReciprocalRankFusion.DefaultK, query.Top, byTerms.Take(FusionDepth).ToList(), meaningCandidates);
            }
        }

        var matches = ranking
            .Take(query.Top)
            .Select((candidate, index) =>
            {
                var sheet = _sheets[candidate.Chunk.Id];
                var shared = terms.Where(sheet.Terms.Contains).Distinct(StringComparer.Ordinal).ToList();
                return new UseCaseMatch
                {
                    Rank = index + 1,
                    UseCase = sheet.UseCase,
                    Score = candidate.Score,
                    Reason = (shared.Count > 0, closeInMeaning.Contains(sheet.UseCase.Id)) switch
                    {
                        (true, true) => UseCaseMatchReason.TermsAndMeaning,
                        (true, false) => UseCaseMatchReason.Terms,
                        _ => UseCaseMatchReason.Meaning,
                    },
                    Terms = shared,
                    Similarity = similarity.TryGetValue(sheet.UseCase.Id, out var cosine) ? cosine : null,
                };
            })
            .ToList();

        return new UseCaseAnswer
        {
            Query = query.Text,
            Language = language,
            LanguageSource = source,
            Mode = mode,
            Degraded = degraded,
            Matches = matches,
        };
    }

    /// <summary>Releases the local model, if a search loaded it.</summary>
    public void Dispose()
    {
        (_model as IDisposable)?.Dispose();
        _model = null;
        _modelGate.Dispose();
    }

    /// <summary>
    /// What BM25 reads of a sheet: its title and problem in every language, its tags and tools,
    /// and the words of its id and category — which is all a sheet whose texts are still empty
    /// can be found by.
    /// </summary>
    private static IEnumerable<string> IndexedText(UseCase useCase) =>
        useCase.Title.Values
            .Concat(useCase.Problem.Values)
            .Concat(useCase.Tags)
            .Concat(useCase.Tools)
            .Append(Words(useCase.Category))
            .Append(Words(useCase.Id));

    /// <summary>
    /// What the local model reads of a sheet: its English title and problem, else the words of
    /// its id and category; its tags either way.
    /// </summary>
    internal static string EnglishText(UseCase useCase)
    {
        var parts = new List<string>();
        foreach (var text in (IReadOnlyDictionary<string, string>[])[useCase.Title, useCase.Problem])
        {
            if (text.TryGetValue(UseCaseLanguages.English, out var english) && !string.IsNullOrWhiteSpace(english))
                parts.Add(english.Trim().TrimEnd('.'));
        }

        if (parts.Count == 0)
            parts.Add($"{Words(useCase.Id)}, {Words(useCase.Category)}");

        if (useCase.Tags.Count > 0)
            parts.Add(string.Join(", ", useCase.Tags));

        return string.Join(". ", parts) + ".";
    }

    /// <summary>A folder name as words, its number dropped: <c>03-finance-trading</c> reads "finance trading".</summary>
    private static string Words(string folderName)
    {
        var dash = folderName.IndexOf('-', StringComparison.Ordinal);
        var name = dash > 0 && folderName[..dash].All(char.IsAsciiDigit) ? folderName[(dash + 1)..] : folderName;
        return name.Replace('-', ' ');
    }

    private static (string Language, UseCaseLanguageSource Source) ResolveLanguage(UseCaseQuery query)
    {
        if (UseCaseLanguages.TryParse(query.Language, out var named))
            return (named, UseCaseLanguageSource.Option);

        return UseCaseLanguages.Detect(query.Text) is { } detected
            ? (detected, UseCaseLanguageSource.Detected)
            : (UseCaseLanguages.Fallback, UseCaseLanguageSource.Default);
    }

    /// <summary>
    /// Every sheet ranked by cosine to the query, best first — or null, with the reason, when the
    /// model is not there. The model is loaded and the sheets embedded once, under a gate, so a
    /// burst of queries at start-up pays the load a single time; a load that failed is not tried
    /// again, for its reason will not change within the process.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification =
        "D-05: whatever stops the local model — missing files, a native runtime that will not load, a model that throws — "
        + "must degrade the search to terms with the reason, never end it: the answer by terms is still an answer.")]
    private async Task<(IReadOnlyList<ScoredChunk>? Ranking, string? Failure)> RankByMeaningAsync(
        string text, CancellationToken cancellationToken)
    {
        var vectors = await EnsureSheetVectorsAsync(cancellationToken).ConfigureAwait(false);
        if (vectors is null)
            return (null, _modelUnavailable);

        float[] query;
        try
        {
            var embedded = await _model!.EmbedBatchAsync([text], cancellationToken).ConfigureAwait(false);
            query = Unit(embedded[0].Span);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, $"the local embedding model failed on this query: {ex.Message}");
        }

        return (
            [
                .. vectors
                    .Select(entry => new ScoredChunk
                    {
                        Chunk = entry.Sheet.Chunk,
                        Score = Dot(query, entry.Vector),
                        ScoreOrigin = CosineOrigin,
                    })
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Chunk.Id, StringComparer.Ordinal),
            ],
            null);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification =
        "D-05: a model that cannot load degrades the search to terms, with the reason recorded and reported on every answer.")]
    private async Task<List<(Sheet Sheet, float[] Vector)>?> EnsureSheetVectorsAsync(CancellationToken cancellationToken)
    {
        // Every caller takes the gate, even once the vectors exist: one uncontended wait per
        // query is nothing next to an embedding, and it keeps the load strictly single.
        await _modelGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_vectors is not null || _modelUnavailable is not null)
                return _vectors;

            try
            {
                _model = _loadModel();
                var sheets = _sheets.Values.ToList();
                var embedded = await _model
                    .EmbedBatchAsync([.. sheets.Select(sheet => EnglishText(sheet.UseCase))], cancellationToken)
                    .ConfigureAwait(false);

                _vectors = [.. sheets.Select((sheet, i) => (sheet, Unit(embedded[i].Span)))];
                return _vectors;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _modelUnavailable = ex is UseCaseModelUnavailableException
                    ? ex.Message
                    : $"the local embedding model failed to load: {ex.Message}";
                (_model as IDisposable)?.Dispose();
                _model = null;
                return null;
            }
        }
        finally
        {
            _modelGate.Release();
        }
    }

    private static float[] Unit(ReadOnlySpan<float> vector)
    {
        var norm = 0.0;
        foreach (var value in vector)
            norm += value * value;

        var unit = vector.ToArray();
        if (norm <= 0)
            return unit;

        var scale = (float)(1.0 / Math.Sqrt(norm));
        for (var i = 0; i < unit.Length; i++)
            unit[i] *= scale;

        return unit;
    }

    private static double Dot(float[] left, float[] right)
    {
        var sum = 0.0;
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
            sum += left[i] * right[i];

        return sum;
    }

    private sealed record Sheet(UseCase UseCase, Chunk Chunk, HashSet<string> Terms);
}
