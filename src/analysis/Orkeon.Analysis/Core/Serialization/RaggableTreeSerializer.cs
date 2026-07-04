using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Analysis.Core.Serialization;

public sealed class RaggableTreeSerializer : IRaggableTreeSerializer
{
    internal const string SupportedVersion = "2.0";

    private static readonly JsonSerializerOptions s_options = BuildOptions(indented: false);
    private static readonly JsonSerializerOptions s_indentedOptions = BuildOptions(indented: true);

    private readonly JsonSerializerOptions _options;

    public RaggableTreeSerializer(bool writeIndented = false)
    {
        _options = writeIndented ? s_indentedOptions : s_options;
    }

    public Task SerializeAsync(RaggableTree tree, string indexId, Stream output, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrEmpty(indexId);
        return SerializeCoreAsync(tree, indexId, output, ct);
    }

    private async Task SerializeCoreAsync(RaggableTree tree, string indexId, Stream output, CancellationToken ct)
    {
        var envelope = new SerializationEnvelope
        {
            Version = SupportedVersion,
            IndexId = indexId,
            CreatedAt = DateTimeOffset.UtcNow,
            NodeCount = tree.Nodes.Count,
            EdgeCount = tree.Edges.Count,
            Nodes = [.. tree.Nodes.Select(NodeMapper.ToDto)],
            Edges = [.. tree.Edges.Select(NodeMapper.ToDto)],
        };

        await JsonSerializer.SerializeAsync(output, envelope, _options, ct).ConfigureAwait(false);
    }

    public Task<RaggableTreeSnapshot?> DeserializeAsync(Stream input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        return DeserializeCoreAsync(input, ct);
    }

    private async Task<RaggableTreeSnapshot?> DeserializeCoreAsync(Stream input, CancellationToken ct)
    {
        var envelope = await JsonSerializer.DeserializeAsync<SerializationEnvelope>(input, _options, ct).ConfigureAwait(false);
        if (envelope is null) return null;

        if (!string.Equals(envelope.Version, SupportedVersion, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Unsupported RaggableTree serialization version '{envelope.Version}' (expected '{SupportedVersion}').");
        }

        var nodes = envelope.Nodes.Select(NodeMapper.ToModel).ToList();
        var edges = envelope.Edges.Select(NodeMapper.ToModel).ToList();

        var index = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node.Fqn)) continue;
            index.TryAdd(node.Fqn, node);
        }

        var tree = new RaggableTree(nodes, edges, index);
        return new RaggableTreeSnapshot(tree, envelope.IndexId, envelope.CreatedAt);
    }

    private static JsonSerializerOptions BuildOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
            MaxDepth = SerializationDefaults.JsonMaxDepth,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
