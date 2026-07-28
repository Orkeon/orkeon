using System.Collections.Immutable;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Orkeon.Rag.Evaluation;

/// <summary>Loads a golden evaluation dataset from a YAML file (plan §9.1).</summary>
public interface IRagEvalDatasetLoader
{
    /// <summary>Loads and validates the dataset at <paramref name="datasetVirtualPath"/> (VFS path).</summary>
    Task<RagEvalDataset> LoadAsync(string datasetVirtualPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// YAML dataset loader (VFS-routed). Two accepted shapes, snake_case keys:
/// <list type="bullet">
///   <item><description>
///     A mapping — <c>name</c>, <c>corpus</c> (dir relative to the dataset file
///     or absolute virtual path), <c>collection</c>, <c>cases</c> (the list below).
///   </description></item>
///   <item><description>
///     A bare list of cases — <c>{ id, question, relevant, expected_substrings,
///     reference_answer?, tags }</c>; the dataset name falls back to the file name
///     and no corpus is ingested.
///   </description></item>
/// </list>
/// Validation is loud: empty file, missing/duplicate ids, or a blank question fail
/// with an actionable message.
/// </summary>
public sealed class RagEvalDatasetYamlLoader : IRagEvalDatasetLoader
{
    private static readonly IDeserializer s_deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly IFileSystemService _fileSystem;

    /// <summary>Initializes the loader over the virtual file system.</summary>
    public RagEvalDatasetYamlLoader(IFileSystemService fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public async Task<RagEvalDataset> LoadAsync(
        string datasetVirtualPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetVirtualPath);

        var text = await _fileSystem.TryReadAllTextAsync(datasetVirtualPath, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FileNotFoundException(
                $"Evaluation dataset not found: '{datasetVirtualPath}'.", datasetVirtualPath);

        var fallbackName = Path.GetFileNameWithoutExtension(datasetVirtualPath);
        return Parse(text, fallbackName, datasetVirtualPath);
    }

    /// <summary>Parses <paramref name="yaml"/> into a validated dataset.</summary>
    internal static RagEvalDataset Parse(string yaml, string fallbackName, string origin)
    {
        DatasetDocument document;
        try
        {
            document = IsBareList(yaml)
                ? new DatasetDocument { Cases = s_deserializer.Deserialize<List<CaseDocument>>(yaml) }
                : s_deserializer.Deserialize<DatasetDocument>(yaml)
                    ?? throw new InvalidOperationException($"Dataset '{origin}' is empty.");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new InvalidOperationException(
                $"Dataset '{origin}' is not valid YAML: {ex.Message}", ex);
        }

        if (document.Cases is not { Count: > 0 })
            throw new InvalidOperationException($"Dataset '{origin}' declares no case.");

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var cases = ImmutableList.CreateBuilder<RagEvalCase>();
        for (var i = 0; i < document.Cases.Count; i++)
        {
            var raw = document.Cases[i];
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidOperationException($"Dataset '{origin}': case #{i + 1} has no 'id'.");
            if (!seenIds.Add(raw.Id))
                throw new InvalidOperationException($"Dataset '{origin}': duplicate case id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.Question))
                throw new InvalidOperationException($"Dataset '{origin}': case '{raw.Id}' has no 'question'.");

            cases.Add(new RagEvalCase
            {
                Id = raw.Id,
                Question = raw.Question,
                Relevant = Clean(raw.Relevant),
                ExpectedSubstrings = Clean(raw.ExpectedSubstrings),
                ReferenceAnswer = string.IsNullOrWhiteSpace(raw.ReferenceAnswer) ? null : raw.ReferenceAnswer,
                Tags = Clean(raw.Tags),
            });
        }

        return new RagEvalDataset
        {
            Name = string.IsNullOrWhiteSpace(document.Name) ? fallbackName : document.Name,
            CorpusPath = string.IsNullOrWhiteSpace(document.Corpus) ? null : document.Corpus,
            DefaultCollection = string.IsNullOrWhiteSpace(document.Collection) ? null : document.Collection,
            Cases = cases.ToImmutable(),
        };
    }

    /// <summary>True when the first significant YAML line starts a sequence (bare case list).</summary>
    private static bool IsBareList(string yaml)
    {
        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("---", StringComparison.Ordinal))
                continue;
            return line.StartsWith('-');
        }

        return false;
    }

    private static ImmutableList<string> Clean(List<string>? values)
        => values is null
            ? ImmutableList<string>.Empty
            : [.. values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())];

    // These two types are YAML binding shapes: YamlDotNet populates every property by
    // reflection, so the analyzer sees no assignment and reports the properties as
    // unassigned (S3459) and their setters as unused (S1144). Both are false positives.
    // `init` is not an option here — YamlDotNet requires accessible setters.
#pragma warning disable S3459, S1144 // Populated by YamlDotNet reflection, not by code

    /// <summary>Mutable YAML binding shape of a dataset file.</summary>
    private sealed class DatasetDocument
    {
        public string? Name { get; set; }
        public string? Corpus { get; set; }
        public string? Collection { get; set; }
        public List<CaseDocument>? Cases { get; set; }
    }

    /// <summary>Mutable YAML binding shape of one case.</summary>
    private sealed class CaseDocument
    {
        public string? Id { get; set; }
        public string? Question { get; set; }
        public List<string>? Relevant { get; set; }
        public List<string>? ExpectedSubstrings { get; set; }
        public string? ReferenceAnswer { get; set; }
        public List<string>? Tags { get; set; }
    }

#pragma warning restore S3459, S1144
}
