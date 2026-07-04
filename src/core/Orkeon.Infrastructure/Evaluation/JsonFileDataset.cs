using System.Text.Json;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using System.Text.Json.Serialization;
using Orkeon.Application.Evaluation;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Dataset that loads evaluation inputs from a JSON file via the virtual file system.
/// Expected format: an array of objects with fields: output, expected_output,
/// task_description, context, expected_format, metadata.
/// </summary>
public sealed class JsonFileDataset : IEvaluationDataset
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    private readonly IFileSystemService _fs;
    private readonly string _vPath;

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Initializes a new instance of <see cref="JsonFileDataset"/>.</summary>
    /// <param name="fs">The virtual file system service.</param>
    /// <param name="name">The dataset name.</param>
    /// <param name="virtualPath">The virtual path to the JSON file.</param>
    public JsonFileDataset(IFileSystemService fs, string name, string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(virtualPath);
        _fs = fs;
        Name = name;
        _vPath = virtualPath;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EvaluationInput>> LoadAsync(CancellationToken ct = default)
    {
        if (!await _fs.ExistsAsync(_vPath, ct).ConfigureAwait(false))
            throw new FileNotFoundException($"Evaluation dataset file not found: {_vPath}", _vPath);

        var json = await _fs.TryReadAllTextAsync(_vPath, ct).ConfigureAwait(false)
                   ?? throw new IOException($"Dataset file could not be read: {_vPath}");

        var items = JsonSerializer.Deserialize<List<JsonEvaluationInputDto>>(json, s_jsonOptions)
                    ?? [];

        return items.Select(dto => new EvaluationInput(
            Output: dto.Output ?? string.Empty,
            ExpectedOutput: dto.ExpectedOutput,
            TaskDescription: dto.TaskDescription,
            Context: dto.Context,
            ExpectedFormat: dto.ExpectedFormat,
            Metadata: dto.Metadata
        )).ToList().AsReadOnly();
    }

    private sealed class JsonEvaluationInputDto
    {
        [JsonPropertyName("output")]
        public string? Output { get; set; }

        [JsonPropertyName("expected_output")]
        public string? ExpectedOutput { get; set; }

        [JsonPropertyName("task_description")]
        public string? TaskDescription { get; set; }

        [JsonPropertyName("context")]
        public string? Context { get; set; }

        [JsonPropertyName("expected_format")]
        public OutputFormat? ExpectedFormat { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
